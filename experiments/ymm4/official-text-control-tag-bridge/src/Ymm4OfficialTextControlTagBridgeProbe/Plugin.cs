using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4OfficialTextControlTagBridgeProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Official Text Control Tag Bridge Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    const string BaselineText = "AB";
    const string ValidTagText = "A<w0>B";
    const string InvalidTagText = "A<cnwl_invalid>B";

    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_OFFICIAL_CONTROL_TAG_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(() => _ = RunAsync()), DispatcherPriority.ApplicationIdle);
    }

    static async Task RunAsync()
    {
        try
        {
            using var devices = new GraphicsDevices();
            using var context = devices.CreateContext();
            var desc = CreateDescription();

            var textObservation = RenderTextSource(context, desc);
            Check("textsource_baseline_rendered", textObservation.Baseline.Success);
            Check("textsource_valid_tag_rendered", textObservation.Valid.Success);
            Check("textsource_invalid_tag_rendered", textObservation.Invalid.Success);
            Check("official_tag_is_layout_invisible_textsource",
                textObservation.Baseline.Success && textObservation.Valid.Success &&
                Math.Abs(textObservation.Valid.Width - textObservation.Baseline.Width) <= 2.0f);
            Check("invalid_tag_remains_layout_visible_textsource",
                textObservation.Invalid.Success && textObservation.Baseline.Success &&
                textObservation.Invalid.Width > textObservation.Baseline.Width + 5.0f);

            var jimakuObservation = RenderJimakuSource(context, desc);
            Check("jimakusource_available", jimakuObservation.Available);
            Check("jimakusource_baseline_rendered", jimakuObservation.Baseline.Success);
            Check("jimakusource_valid_tag_rendered", jimakuObservation.Valid.Success);
            Check("official_tag_is_layout_invisible_jimaku",
                jimakuObservation.Baseline.Success && jimakuObservation.Valid.Success &&
                Math.Abs(jimakuObservation.Valid.Width - jimakuObservation.Baseline.Width) <= 2.0f);
            Check("voice_serif_retained_after_render",
                jimakuObservation.StoredSerifAfterValid == ValidTagText);

            var audio = await ObserveSerifToHatsuonAsync();
            var tagTypes = DiscoverTagRelatedTypes();

            File.WriteAllText(Path.Combine(output, "behavior.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    candidate = new
                    {
                        marker = "<w0>",
                        officialMeaning = "wait 0 seconds"
                    },
                    textSource = textObservation,
                    jimakuSource = jimakuObservation,
                    pronunciationObservation = audio,
                    tagRelatedTypes = tagTypes
                }, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_OFFICIAL_CONTROL_TAG_BRIDGE", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_OFFICIAL_CONTROL_TAG_BRIDGE", ex.ToString());
        }
    }

    static SourceObservation RenderTextSource(IGraphicsDevicesAndContext context, TimelineItemSourceDescription desc)
    {
        RenderResult One(string text)
        {
            var item = new TextItem
            {
                Text = text,
                Font = "Yu Gothic UI",
                BasePoint = BasePoint.LeftTop
            };
            return RenderInternalSource(context, item, "YukkuriMovieMaker.Player.Video.Items.TextSource", desc);
        }

        return new SourceObservation(
            Available: true,
            Baseline: One(BaselineText),
            Valid: One(ValidTagText),
            Invalid: One(InvalidTagText),
            StoredSerifAfterValid: null,
            Error: null);
    }

    static SourceObservation RenderJimakuSource(IGraphicsDevicesAndContext context, TimelineItemSourceDescription desc)
    {
        try
        {
            RenderResult One(string text, out string storedAfter)
            {
                var item = CreateVoiceItem();
                item.Serif = text;
                item.Font = "Yu Gothic UI";
                item.BasePoint = BasePoint.LeftTop;
                var result = RenderInternalSource(context, item, "YukkuriMovieMaker.Player.Video.Items.JimakuSource", desc);
                storedAfter = item.Serif;
                return result;
            }

            var baseline = One(BaselineText, out _);
            var valid = One(ValidTagText, out var storedValid);
            var invalid = One(InvalidTagText, out _);
            return new SourceObservation(true, baseline, valid, invalid, storedValid, null);
        }
        catch (Exception ex)
        {
            return new SourceObservation(
                Available: false,
                Baseline: RenderResult.Fail(ex.Message),
                Valid: RenderResult.Fail(ex.Message),
                Invalid: RenderResult.Fail(ex.Message),
                StoredSerifAfterValid: null,
                Error: ex.ToString());
        }
    }

    static VoiceItem CreateVoiceItem()
    {
        try
        {
            var character = Activator.CreateInstance(typeof(Character)) as Character;
            if (character is not null)
                return new VoiceItem(character);
        }
        catch { }
        return new VoiceItem();
    }

    static RenderResult RenderInternalSource(
        IGraphicsDevicesAndContext context,
        object item,
        string sourceTypeName,
        TimelineItemSourceDescription desc)
    {
        object? source = null;
        try
        {
            var type = typeof(VoiceItem).Assembly.GetType(sourceTypeName)
                ?? throw new InvalidOperationException(sourceTypeName + " not found.");

            source = CreateSource(type, context, item)
                ?? throw new InvalidOperationException("Could not construct " + sourceTypeName);

            var update = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name == "Update" &&
                                     x.GetParameters().Length == 1 &&
                                     x.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(TimelineItemSourceDescription)))
                ?? type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(type.FullName, "Update");

            update.Invoke(source, [desc]);

            var outputsProp = type.GetProperty("Outputs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(type.FullName, "Outputs");
            if (outputsProp.GetValue(source) is not IEnumerable outputs)
                throw new InvalidOperationException("Outputs is not enumerable.");

            int count = 0;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            foreach (var entry in outputs)
            {
                if (entry is null) continue;
                count++;

                var outProp = entry.GetType().GetProperty("Output", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (outProp?.GetValue(entry) is not ID2D1Image image)
                    continue;

                var rect = context.DeviceContext.GetImageLocalBounds(image);
                var (ox, oy) = ReadOffset(entry);
                minX = Math.Min(minX, rect.Left + ox);
                minY = Math.Min(minY, rect.Top + oy);
                maxX = Math.Max(maxX, rect.Right + ox);
                maxY = Math.Max(maxY, rect.Bottom + oy);
            }

            if (count == 0 || float.IsInfinity(minX))
                throw new InvalidOperationException("No drawable Outputs.");

            return new RenderResult(
                Success: true,
                Width: Math.Max(0, maxX - minX),
                Height: Math.Max(0, maxY - minY),
                OutputCount: count,
                Error: null);
        }
        catch (TargetInvocationException ex)
        {
            return RenderResult.Fail((ex.InnerException ?? ex).ToString());
        }
        catch (Exception ex)
        {
            return RenderResult.Fail(ex.ToString());
        }
        finally
        {
            (source as IDisposable)?.Dispose();
        }
    }

    static object? CreateSource(Type type, IGraphicsDevicesAndContext context, object item)
    {
        foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .OrderBy(x => x.GetParameters().Length))
        {
            var ps = ctor.GetParameters();
            if (ps.Length < 2)
                continue;
            if (!ps[0].ParameterType.IsInstanceOfType(context) &&
                !ps[0].ParameterType.IsAssignableFrom(context.GetType()))
                continue;
            if (!ps[1].ParameterType.IsInstanceOfType(item) &&
                !ps[1].ParameterType.IsAssignableFrom(item.GetType()))
                continue;

            var args = new object?[ps.Length];
            args[0] = context;
            args[1] = item;
            bool ok = true;
            for (int i = 2; i < ps.Length; i++)
            {
                if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                else if (ps[i].ParameterType.IsValueType) args[i] = Activator.CreateInstance(ps[i].ParameterType);
                else args[i] = null;
            }

            if (!ok) continue;
            try { return ctor.Invoke(args); } catch { }
        }
        return null;
    }

    static (float X, float Y) ReadOffset(object entry)
    {
        try
        {
            var p = entry.GetType().GetProperty("DrawingOffset",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var v = p?.GetValue(entry);
            if (v is null) return (0, 0);
            var t = v.GetType();
            var x = t.GetProperty("X")?.GetValue(v) ?? t.GetField("X")?.GetValue(v);
            var y = t.GetProperty("Y")?.GetValue(v) ?? t.GetField("Y")?.GetValue(v);
            return (x is null ? 0 : Convert.ToSingle(x), y is null ? 0 : Convert.ToSingle(y));
        }
        catch { return (0, 0); }
    }

    sealed record PronunciationObservation(
        string? MethodSignature,
        bool InvokedBaseline,
        string? BaselineHatsuon,
        string? BaselineError,
        bool InvokedTagged,
        string? TaggedHatsuon,
        string? TaggedError,
        bool? EqualAfterConversion,
        bool? TaggedContainsLiteralControlTag);

    static async Task<PronunciationObservation> ObserveSerifToHatsuonAsync()
    {
        var method = typeof(VoiceItem).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(x => x.Name == "SerifToHatsuonAsync")
            .OrderBy(x => x.GetParameters().Length)
            .FirstOrDefault();

        if (method is null)
            return new PronunciationObservation(null, false, null, "method not found", false, null, "method not found", null, null);

        async Task<(bool Invoked, string? Hatsuon, string? Error)> One(string serif)
        {
            var item = CreateVoiceItem();
            item.Serif = serif;
            try
            {
                var args = BuildArguments(method.GetParameters());
                var value = method.Invoke(item, args);
                if (value is Task task)
                    await task;
                return (true, item.Hatsuon, null);
            }
            catch (TargetInvocationException ex)
            {
                return (true, item.Hatsuon, (ex.InnerException ?? ex).ToString());
            }
            catch (Exception ex)
            {
                return (false, item.Hatsuon, ex.ToString());
            }
        }

        var baseline = await One("あい");
        var tagged = await One("あ<w0>い");
        bool? equal = baseline.Error is null && tagged.Error is null
            ? baseline.Hatsuon == tagged.Hatsuon
            : null;
        bool? contains = tagged.Hatsuon is null ? null : tagged.Hatsuon.Contains("<w0>", StringComparison.OrdinalIgnoreCase);

        return new PronunciationObservation(
            method.ToString(),
            baseline.Invoked,
            baseline.Hatsuon,
            baseline.Error,
            tagged.Invoked,
            tagged.Hatsuon,
            tagged.Error,
            equal,
            contains);
    }

    static object?[] BuildArguments(ParameterInfo[] ps)
    {
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var t = ps[i].ParameterType;
            if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
            else if (t == typeof(CancellationToken)) args[i] = CancellationToken.None;
            else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
            else args[i] = null;
        }
        return args;
    }

    static string[] DiscoverTagRelatedTypes()
    {
        var terms = new[] { "tag", "control", "textlayout", "richtext" };
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>(); }
                catch { return []; }
            })
            .Where(t =>
            {
                var n = t.FullName ?? t.Name;
                return terms.Any(term => n.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .Select(t => t.FullName ?? t.Name)
            .Distinct()
            .OrderBy(x => x)
            .Take(300)
            .ToArray();
    }

    static TimelineItemSourceDescription CreateDescription()
    {
        const int fps = 30;
        const int length = 300;
        var timeline = new TimelineSourceDescription(
            new System.Drawing.Size(1920, 1080),
            new YukkuriMovieMaker.Player.Video.FrameTime(0, fps),
            new YukkuriMovieMaker.Player.Video.FrameTime(length, fps),
            fps,
            TimelineSourceUsage.Playing,
            Guid.Empty,
            []);
        return new TimelineItemSourceDescription(timeline, 0, length, 0);
    }

    sealed record RenderResult(bool Success, float Width, float Height, int OutputCount, string? Error)
    {
        internal static RenderResult Fail(string error) => new(false, 0, 0, 0, error);
    }

    sealed record SourceObservation(
        bool Available,
        RenderResult Baseline,
        RenderResult Valid,
        RenderResult Invalid,
        string? StoredSerifAfterValid,
        string? Error);

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.official-control-tag-bridge.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
