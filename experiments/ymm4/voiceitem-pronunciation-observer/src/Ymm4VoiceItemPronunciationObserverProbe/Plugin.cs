using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceItemPronunciationObserverProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Pronunciation Observer Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

[VideoEffect("CNWL 発音補助キー監視", ["CNWL"], [])]
public sealed class PronunciationAssistKeyEffect : VideoEffectBase
{
    public override string Label => "CNWL 発音補助キー監視";
    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new PassThroughProcessor();
    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class PassThroughProcessor : IVideoEffectProcessor
{
    ID2D1Image? input;
    public ID2D1Image Output => input ?? throw new InvalidOperationException("input is null");
    public DrawDescription Update(EffectDescription effectDescription) => effectDescription.DrawDescription;
    public void SetInput(ID2D1Image? value) => input = value;
    public void ClearInput() => input = null;
    public void Dispose() { }
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEITEM_OBSERVER_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    static void Start()
    {
        int ticks = 0;
        bool created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows.Cast<Window>()
                    .Select(w => w.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (main is null)
                    return;

                var active = main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                    }
                    if (ticks > 160)
                        throw new TimeoutException("ActiveTimelineViewModel was not created.");
                    return;
                }

                timer.Stop();
                Run(active);
                Write("PASS_VOICEITEM_PRONUNCIATION_OBSERVER", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_PRONUNCIATION_OBSERVER", ex.ToString());
            }
        };
        timer.Start();
    }

    static void Run(object active)
    {
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException("Timeline could not be resolved.");
        Check("timeline_resolved", true);

        var voice = new VoiceItem
        {
            Serif = "before serif",
            Hatsuon = "before hatsuon",
            CharacterName = "CNWL Probe"
        };

        Check("voice_added_to_timeline", timeline.TryAddItems([voice], 120, 8));

        var changed = new List<string>();
        var changing = new List<string>();

        Check("voiceitem_is_inotifypropertychanged", voice is INotifyPropertyChanged);
        if (voice is INotifyPropertyChanged npc)
            npc.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "<null>");

        var supportsChanging = voice is INotifyPropertyChanging;
        if (voice is INotifyPropertyChanging npcg)
            npcg.PropertyChanging += (_, e) => changing.Add(e.PropertyName ?? "<null>");

        var effect = new PronunciationAssistKeyEffect();
        var effectChanged = new List<string>();
        Check("effect_is_inotifypropertychanged", effect is INotifyPropertyChanged);
        if (effect is INotifyPropertyChanged epc)
            epc.PropertyChanged += (_, e) => effectChanged.Add(e.PropertyName ?? "<null>");

        voice.Serif = "after serif";
        voice.Hatsuon = "after hatsuon";
        voice.JimakuVideoEffects = voice.JimakuVideoEffects.Add(effect);

        effect.IsEnabled = false;
        effect.IsEnabled = true;

        Check("serif_change_notified", changed.Contains(nameof(VoiceItem.Serif)));
        Check("hatsuon_change_notified", changed.Contains(nameof(VoiceItem.Hatsuon)));
        Check("jimaku_effects_change_notified", changed.Contains(nameof(VoiceItem.JimakuVideoEffects)));
        Check("effect_enabled_change_notified", effectChanged.Contains(nameof(VideoEffectBase.IsEnabled)));
        Check("key_still_detectable_after_notifications",
            voice.JimakuVideoEffects.OfType<PronunciationAssistKeyEffect>().Any(x => x.IsEnabled));

        var selected = timeline.SelectedItems;
        timeline.SelectedItems = selected.Add(voice);
        Check("voice_present_in_real_timeline", timeline.Items.Contains(voice));

        File.WriteAllText(Path.Combine(output, "behavior.json"),
            JsonSerializer.Serialize(new
            {
                host = "4.56.1.0 Lite",
                voiceType = voice.GetType().FullName,
                supportsPropertyChanging = supportsChanging,
                propertyChanged = changed,
                propertyChanging = changing,
                effectPropertyChanged = effectChanged,
                final = new
                {
                    voice.Serif,
                    voice.Hatsuon,
                    jimakuEffects = voice.JimakuVideoEffects.Select(x => new
                    {
                        type = x.GetType().FullName,
                        x.IsEnabled
                    }).ToArray()
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static Timeline? FindTimeline(object active)
    {
        for (var t = active.GetType(); t is not null; t = t.BaseType)
        {
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance |
                                          System.Reflection.BindingFlags.Public |
                                          System.Reflection.BindingFlags.NonPublic))
                if (typeof(Timeline).IsAssignableFrom(f.FieldType) && f.GetValue(active) is Timeline ft)
                    return ft;

            foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Instance |
                                              System.Reflection.BindingFlags.Public |
                                              System.Reflection.BindingFlags.NonPublic))
            {
                if (p.GetIndexParameters().Length != 0 || !typeof(Timeline).IsAssignableFrom(p.PropertyType))
                    continue;
                try
                {
                    if (p.GetValue(active) is Timeline pt)
                        return pt;
                }
                catch { }
            }
        }
        return null;
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voiceitem-pronunciation-observer.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
