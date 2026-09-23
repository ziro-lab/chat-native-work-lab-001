using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4FilteredJimakuSourceProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Filtered JimakuSource Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

[VideoEffect("CNWL 字幕制御記号除去", ["CNWL"], [])]
public sealed class FilteredSubtitleEffect : VideoEffectBase
{
    public override string Label => "CNWL 字幕制御記号除去";
    public string Marker { get; set; } = "§";

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
    {
        ProbeState.ProcessorCreated++;
        if (!OwnerRegistry.TryGet(this, out var owner))
            throw new InvalidOperationException("Runtime owner is not registered.");
        return new FilteredSubtitleProcessor(devices, owner, Marker);
    }

    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal static class OwnerRegistry
{
    static readonly Dictionary<FilteredSubtitleEffect, VoiceItem> owners =
        new(ReferenceEqualityComparer.Instance);

    internal static void Register(FilteredSubtitleEffect effect, VoiceItem owner) => owners[effect] = owner;
    internal static bool TryGet(FilteredSubtitleEffect effect, out VoiceItem owner) => owners.TryGetValue(effect, out owner!);
}

internal static class ProbeState
{
    internal static int ProcessorCreated;
    internal static int ProcessorUpdated;
    internal static int InputSet;
    internal static string? LastFilteredSerif;
}

internal sealed class FilteredSubtitleProcessor : IVideoEffectProcessor
{
    readonly object source;
    readonly MethodInfo update;
    readonly PropertyInfo outputs;
    ID2D1Image? nestedOutput;

    internal FilteredSubtitleProcessor(IGraphicsDevicesAndContext devices, VoiceItem owner, string marker)
    {
        var clone = CloneForSubtitle(owner);
        clone.Serif = (owner.Serif ?? "").Replace(marker, "", StringComparison.Ordinal);
        clone.JimakuVideoEffects = ImmutableList<IVideoEffect>.Empty;
        ProbeState.LastFilteredSerif = clone.Serif;

        var type = typeof(VoiceItem).Assembly.GetType("YukkuriMovieMaker.Player.Video.Items.JimakuSource")
            ?? throw new InvalidOperationException("JimakuSource type not found.");
        var ctor = type.GetConstructor([typeof(IGraphicsDevices), typeof(VoiceItem), typeof(int)])
            ?? throw new MissingMethodException(type.FullName, ".ctor(IGraphicsDevices, VoiceItem, int)");

        source = ctor.Invoke([devices, clone, 0]);
        update = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(type.FullName, "Update");
        outputs = type.GetProperty("Outputs", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(type.FullName, "Outputs");
    }

    public ID2D1Image Output => nestedOutput ?? throw new InvalidOperationException("Filtered JimakuSource has no output yet.");

    public DrawDescription Update(EffectDescription effectDescription)
    {
        ProbeState.ProcessorUpdated++;
        update.Invoke(source, [effectDescription]);
        nestedOutput = ReadFirstOutput(outputs.GetValue(source))
            ?? throw new InvalidOperationException("Nested JimakuSource output is null.");
        return effectDescription.DrawDescription;
    }

    public void SetInput(ID2D1Image? input) => ProbeState.InputSet++;
    public void ClearInput() { }

    public void Dispose()
    {
        nestedOutput = null;
        (source as IDisposable)?.Dispose();
    }

    static VoiceItem CloneForSubtitle(VoiceItem owner)
    {
        var clone = new VoiceItem();
        foreach (var p in typeof(VoiceItem).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length != 0 || p.GetMethod?.IsPublic != true || p.SetMethod?.IsPublic != true)
                continue;
            if (p.Name is nameof(VoiceItem.Serif) or nameof(VoiceItem.JimakuVideoEffects))
                continue;
            try { p.SetValue(clone, p.GetValue(owner)); }
            catch { }
        }
        return clone;
    }

    static ID2D1Image? ReadFirstOutput(object? value)
    {
        if (value is not IEnumerable values)
            return null;
        foreach (var entry in values)
        {
            if (entry is null)
                continue;
            var p = entry.GetType().GetProperty("Output", BindingFlags.Instance | BindingFlags.Public);
            if (p?.GetValue(entry) is ID2D1Image image)
                return image;
        }
        return null;
    }
}

internal static class Probe
{
    static bool scheduled;
    static string output="";
    static readonly List<object> requirements=[];

    internal static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_FILTERED_JIMAKU_OUTPUT");
        if(scheduled||string.IsNullOrWhiteSpace(dir)) return;
        scheduled=true;
        output=Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run),DispatcherPriority.ApplicationIdle);
    }

    static void Run()
    {
        try
        {
            using var devices=new GraphicsDevices();
            using var context=devices.CreateContext();
            var desc=CreateDescription();

            var baselineItem=CreateVoice("AB");
            var visibleItem=CreateVoice("A§B");
            var filteredItem=CreateVoice("A§B");
            var effect=new FilteredSubtitleEffect();
            OwnerRegistry.Register(effect,filteredItem);
            filteredItem.JimakuVideoEffects=filteredItem.JimakuVideoEffects.Add(effect);

            using var baseline=new JimakuRenderer(devices,baselineItem);
            using var visible=new JimakuRenderer(devices,visibleItem);
            using var filtered=new JimakuRenderer(devices,filteredItem);

            var baselineBounds=baseline.Measure(context,desc);
            var visibleBounds=visible.Measure(context,desc);
            var filteredBounds=filtered.Measure(context,desc);

            Check("baseline_rendered",baselineBounds.Width>0&&baselineBounds.Height>0);
            Check("visible_marker_rendered",visibleBounds.Width>0&&visibleBounds.Height>0);
            Check("filtered_effect_rendered",filteredBounds.Width>0&&filteredBounds.Height>0);
            Check("effect_processor_created",ProbeState.ProcessorCreated>0);
            Check("effect_processor_updated",ProbeState.ProcessorUpdated>0);
            Check("effect_received_rendered_input",ProbeState.InputSet>0);
            Check("stored_serif_unchanged",filteredItem.Serif=="A§B");
            Check("temporary_serif_filtered",ProbeState.LastFilteredSerif=="AB");
            Check("visible_marker_adds_width",visibleBounds.Width-baselineBounds.Width>1f);
            Check("filtered_output_collapses_marker_width",visibleBounds.Width-filteredBounds.Width>1f);
            Check("filtered_output_matches_baseline",Math.Abs(filteredBounds.Width-baselineBounds.Width)<=2f);

            File.WriteAllText(Path.Combine(output,"behavior.json"),
                JsonSerializer.Serialize(new{
                    host="4.56.1.0 Lite",
                    baseline=baselineBounds,
                    visible=visibleBounds,
                    filtered=filteredBounds,
                    originalSerif=filteredItem.Serif,
                    filteredSerif=ProbeState.LastFilteredSerif,
                    processor=new{
                        ProbeState.ProcessorCreated,
                        ProbeState.ProcessorUpdated,
                        ProbeState.InputSet
                    }
                },new JsonSerializerOptions{WriteIndented=true}));

            Write("PASS_FILTERED_JIMAKU_SOURCE",null);
        }
        catch(Exception ex)
        {
            Write("FAIL_FILTERED_JIMAKU_SOURCE",ex.ToString());
        }
    }

    static VoiceItem CreateVoice(string serif)
    {
        var item=new VoiceItem
        {
            Serif=serif,
            CharacterName="CNWL Probe"
        };
        TrySet(item,"Font","Yu Gothic UI");
        TrySet(item,"BasePoint",BasePoint.LeftTop);
        return item;
    }

    static void TrySet(object target,string propertyName,object value)
    {
        try
        {
            var p=target.GetType().GetProperty(propertyName,BindingFlags.Instance|BindingFlags.Public);
            if(p?.SetMethod?.IsPublic==true) p.SetValue(target,value);
        }
        catch { }
    }

    static TimelineItemSourceDescription CreateDescription()
    {
        const int fps=30;
        const int length=300;
        var timeline=new TimelineSourceDescription(
            new System.Drawing.Size(1920,1080),
            new FrameTime(0,fps),
            new FrameTime(length,fps),
            fps,
            TimelineSourceUsage.Playing,
            Guid.Empty,
            []);
        return new TimelineItemSourceDescription(timeline,0,length,0);
    }

    readonly record struct Bounds(float Width,float Height);

    sealed class JimakuRenderer : IDisposable
    {
        readonly object source;
        readonly MethodInfo update;
        readonly PropertyInfo outputs;

        internal JimakuRenderer(IGraphicsDevices devices,VoiceItem item)
        {
            var type=typeof(VoiceItem).Assembly.GetType("YukkuriMovieMaker.Player.Video.Items.JimakuSource")
                ?? throw new InvalidOperationException("JimakuSource type not found.");
            var ctor=type.GetConstructor([typeof(IGraphicsDevices),typeof(VoiceItem),typeof(int)])
                ?? throw new MissingMethodException(type.FullName,".ctor");
            source=ctor.Invoke([devices,item,0]);
            update=type.GetMethod("Update",BindingFlags.Instance|BindingFlags.Public)
                ?? throw new MissingMethodException(type.FullName,"Update");
            outputs=type.GetProperty("Outputs",BindingFlags.Instance|BindingFlags.Public)
                ?? throw new MissingMemberException(type.FullName,"Outputs");
        }

        internal Bounds Measure(IGraphicsDevicesAndContext context,TimelineItemSourceDescription desc)
        {
            update.Invoke(source,[desc]);
            var image=ReadFirstOutput(outputs.GetValue(source))
                ?? throw new InvalidOperationException("JimakuSource output is null.");
            var rect=context.DeviceContext.GetImageLocalBounds(image);
            return new Bounds(Math.Max(0,rect.Right-rect.Left),Math.Max(0,rect.Bottom-rect.Top));
        }

        public void Dispose()=>(source as IDisposable)?.Dispose();
    }

    static ID2D1Image? ReadFirstOutput(object? value)
    {
        if(value is not IEnumerable values) return null;
        foreach(var entry in values)
        {
            if(entry is null) continue;
            var p=entry.GetType().GetProperty("Output",BindingFlags.Instance|BindingFlags.Public);
            if(p?.GetValue(entry) is ID2D1Image image) return image;
        }
        return null;
    }

    static void Check(string id,bool passed)=>requirements.Add(new{id,passed});

    static void Write(string status,string? error)
    {
        File.WriteAllText(Path.Combine(output,"result.json"),
            JsonSerializer.Serialize(new{
                schema="cnwl.filtered-jimaku-source.v1",
                status,
                host="4.56.1.0 Lite",
                sourceHead=Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            },new JsonSerializerOptions{WriteIndented=true}));
    }
}
