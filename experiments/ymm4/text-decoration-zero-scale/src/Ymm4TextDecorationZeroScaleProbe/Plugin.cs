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
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;
using YmmTextDecoration = YukkuriMovieMaker.Commons.TextDecoration;

namespace Ymm4TextDecorationZeroScaleProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL TextDecoration Zero Scale Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_TEXT_DECORATION_ZERO_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    static void Run()
    {
        try
        {
            using var devices = new GraphicsDevices();
            using var context = devices.CreateContext();

            var desc = CreateDescription();

            using var baseline = new TextRenderer(context, "AB", []);
            using var visible = new TextRenderer(context, "A§B", []);
            using var hidden = new TextRenderer(context, "A§B",
            [
                new YmmTextDecoration(
                    Start: 1,
                    Length: 1,
                    IsBold: false,
                    IsItalic: false,
                    Scale: 0.0,
                    Font: "",
                    Foreground: null,
                    IsLineBreak: false)
            ]);

            var baselineBounds = baseline.Measure(desc);
            var visibleBounds = visible.Measure(desc);
            var hiddenBounds = hidden.Measure(desc);

            Check("baseline_rendered", baselineBounds.Width > 0 && baselineBounds.Height > 0);
            Check("visible_marker_rendered", visibleBounds.Width > 0 && visibleBounds.Height > 0);
            Check("zero_scale_marker_rendered", hiddenBounds.Width > 0 && hiddenBounds.Height > 0);

            var visibleExtra = visibleBounds.Width - baselineBounds.Width;
            var hiddenExtra = hiddenBounds.Width - baselineBounds.Width;
            var collapseGain = visibleBounds.Width - hiddenBounds.Width;

            Check("visible_marker_adds_width", visibleExtra > 1.0f);
            Check("zero_scale_collapses_marker_width", collapseGain > 1.0f);
            Check("zero_scale_approximately_matches_baseline", Math.Abs(hiddenExtra) <= 2.0f);

            File.WriteAllText(Path.Combine(output, "behavior.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    baseline = baselineBounds,
                    visible = visibleBounds,
                    hidden = hiddenBounds,
                    visibleExtra,
                    hiddenExtra,
                    collapseGain,
                    decoration = new
                    {
                        start = 1,
                        length = 1,
                        scale = 0.0
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_TEXT_DECORATION_ZERO_SCALE", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_TEXT_DECORATION_ZERO_SCALE", ex.ToString());
        }
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

    readonly record struct Bounds(float Width, float Height);

    sealed class TextRenderer : IDisposable
    {
        readonly IGraphicsDevicesAndContext context;
        readonly TextItem item;
        readonly object source;
        readonly MethodInfo update;
        readonly PropertyInfo outputs;

        internal TextRenderer(IGraphicsDevicesAndContext context, string text, ImmutableList<YmmTextDecoration> decorations)
        {
            this.context = context;
            item = new TextItem
            {
                Text = text,
                Font = "Yu Gothic UI",
                BasePoint = BasePoint.LeftTop,
                Decorations = decorations
            };

            var type = typeof(TextItem).Assembly.GetType("YukkuriMovieMaker.Player.Video.Items.TextSource")
                ?? throw new InvalidOperationException("TextSource type not found.");

            source = Activator.CreateInstance(type, context, item)
                ?? throw new InvalidOperationException("Failed to create TextSource.");

            update = type.GetMethod("Update")
                ?? throw new MissingMethodException(type.FullName, "Update");
            outputs = type.GetProperty("Outputs")
                ?? throw new MissingMemberException(type.FullName, "Outputs");
        }

        internal Bounds Measure(TimelineItemSourceDescription desc)
        {
            update.Invoke(source, [desc]);
            var list = outputs.GetValue(source) as IList
                ?? throw new InvalidOperationException("TextSource.Outputs is not IList.");
            if (list.Count == 0 || list[0] is null)
                throw new InvalidOperationException("TextSource.Outputs is empty.");

            var outputProperty = list[0]!.GetType().GetProperty("Output")
                ?? throw new MissingMemberException(list[0]!.GetType().FullName, "Output");
            var image = outputProperty.GetValue(list[0]!) as ID2D1Image
                ?? throw new InvalidOperationException("Text output image is null.");

            var rect = context.DeviceContext.GetImageLocalBounds(image);
            return new Bounds(
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top));
        }

        public void Dispose() => (source as IDisposable)?.Dispose();
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.text-decoration-zero-scale.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
