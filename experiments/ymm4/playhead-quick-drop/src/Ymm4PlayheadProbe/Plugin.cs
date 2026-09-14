using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4PlayheadProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Playhead Probe";
    public void SetCulture(CultureInfo cultureInfo) => PlayheadHost.Schedule();
}

public sealed class PlayheadToolPlugin : IToolPlugin
{
    public string Name => "CNWL Playhead Probe";
    public Type ViewModelType => typeof(PlayheadProbeViewModel);
    public Type ViewType => typeof(PlayheadProbeView);
    public bool AllowMultipleInstances => false;
}

public sealed class PlayheadProbeView : UserControl
{
    public PlayheadProbeView() => Content = new TextBlock { Text = "CNWL Playhead Probe", Margin = new Thickness(12) };
}

public sealed class PlayheadProbeViewModel : ITimelineToolViewModel, IToolViewModel, IDisposable
{
    public string Title => "CNWL Playhead Probe";
    public bool CanSuspend => false;
    public void SetTimelineToolInfo(TimelineToolInfo info) => PlayheadProof.Run(info.Timeline, info.UndoRedoManager);
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public void Dispose() { }
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
}

internal static class PlayheadHost
{
    private static bool scheduled;
    public static void Schedule()
    {
        var output = Environment.GetEnvironmentVariable("CNWL_YMM4_PLAYHEAD_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(output)) return;
        scheduled = true;
        Application.Current.Dispatcher.BeginInvoke(new Action(() => Start(Path.GetFullPath(output))));
    }

    private static void Start(string output)
    {
        Directory.CreateDirectory(output);
        var ticks = 0;
        var projectCreated = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                    if (active == null && !projectCreated)
                    {
                        projectCreated = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        break;
                    }
                    if (active == null) continue;
                    if (OpenTool(main))
                    {
                        File.WriteAllText(Path.Combine(output, "host.txt"), "tool_open_requested=True\n", new UTF8Encoding(false));
                        timer.Stop();
                        return;
                    }
                }
                if (ticks >= 90)
                {
                    timer.Stop();
                    File.WriteAllText(Path.Combine(output, "result.txt"), "status=FAIL_TOOL_OPEN_TIMEOUT\n", new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(output, "host-error.txt"), ex.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(output, "result.txt"), "status=FAIL_HOST_EXCEPTION\n", new UTF8Encoding(false));
            }
        };
        timer.Start();
    }

    private static bool OpenTool(object main)
    {
        var items = main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) as IEnumerable;
        if (items == null) return false;
        bool Visit(object item, int depth)
        {
            if (depth > 6) return false;
            var type = item.GetType();
            var header = type.GetProperty("Header")?.GetValue(item)?.ToString()
                ?? type.GetProperty("Title")?.GetValue(item)?.ToString()
                ?? type.GetProperty("Name")?.GetValue(item)?.ToString() ?? "";
            if (header.Contains("CNWL Playhead Probe", StringComparison.Ordinal))
            {
                if (type.GetProperty("Command")?.GetValue(item) is ICommand command)
                {
                    var parameter = type.GetProperty("CommandParameter")?.GetValue(item);
                    if (command.CanExecute(parameter)) { command.Execute(parameter); return true; }
                }
                if (type.GetProperty("ViewModelType")?.GetValue(item) is Type vmType && vmType == typeof(PlayheadProbeViewModel))
                {
                    type.GetProperty("IsVisible")?.SetValue(item, true);
                    type.GetProperty("IsSelected")?.SetValue(item, true);
                    type.GetProperty("IsActive")?.SetValue(item, true);
                    return true;
                }
            }
            var children = (type.GetProperty("Children")?.GetValue(item) ?? type.GetProperty("Items")?.GetValue(item)) as IEnumerable;
            if (children != null)
                foreach (var child in children)
                    if (child != null && Visit(child, depth + 1)) return true;
            return false;
        }
        foreach (var item in items) if (item != null && Visit(item, 0)) return true;
        return false;
    }
}

internal static class PlayheadProof
{
    private static readonly string[] ExactFrameNames = ["CurrentFrame", "CurrentPositionFrame", "PlayheadFrame", "CursorFrame"];

    public static void Run(Timeline timeline, UndoRedoManager undo)
    {
        var output = Environment.GetEnvironmentVariable("CNWL_YMM4_PLAYHEAD_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        output = Path.GetFullPath(output);
        try
        {
            DumpPublicSurface(timeline, output);
            var prop = FindFrameProperty(timeline.GetType());
            if (prop == null)
            {
                WriteResult(output, "DISCOVERY_NO_PUBLIC_FRAME", ["public_frame_property=<none>"]);
                return;
            }

            var beforeFrame = (int)prop.GetValue(timeline)!;
            var targetFrame = beforeFrame == 321 ? 654 : 321;
            var moved = TryMoveFrame(timeline, prop, targetFrame, output);
            var playhead = (int)prop.GetValue(timeline)!;
            if (!moved || playhead != targetFrame)
            {
                WriteResult(output, "DISCOVERY_PUBLIC_FRAME_READ_ONLY", [$"public_frame_property={prop.Name}", $"before_frame={beforeFrame}", $"after_frame={playhead}"]);
                return;
            }

            var character = new Character { Name = "CNWL_QuickDrop" };
            var source = new TachieFaceItem(character) { Frame = 17, Length = 37, Layer = 12, Remark = "CNWL_TEMPLATE_SOURCE" };
            var template = new ItemTemplate(ItemTemplateGroup.TachieFaceItem, "CNWL Quick Drop", new IItem[] { source }, timeline, Guid.Empty);
            ItemSettings.Default.Templates.Add(template);
            try
            {
                var liveSource = template.Items.OfType<TachieFaceItem>().Single();
                var clone = liveSource.GetClone() as TachieFaceItem ?? throw new InvalidOperationException("Template clone failed.");
                clone.Frame = playhead;
                clone.Remark = "CNWL_QUICK_DROP";
                clone.Group = 0;

                var before = timeline.Items;
                undo.Record();
                timeline.Items = before.Add(clone);
                timeline.RefreshTimelineLengthAndMaxLayer();
                undo.Record();

                var independent = !ReferenceEquals(clone, liveSource);
                var placed = timeline.Items.Contains(clone);
                var frameMatches = clone.Frame == playhead;
                var lengthPreserved = clone.Length == 37 && liveSource.Length == 37;
                var sourceUnchanged = liveSource.Frame == 17 && liveSource.Layer == 12 && liveSource.Remark == "CNWL_TEMPLATE_SOURCE";
                var pass = independent && placed && frameMatches && lengthPreserved && sourceUnchanged;
                WriteResult(output, pass ? "PASS_PLAYHEAD_QUICK_DROP" : "FAIL_QUICK_DROP_ASSERTION",
                [
                    $"public_frame_property={prop.Name}",
                    $"before_frame={beforeFrame}",
                    $"playhead_frame={playhead}",
                    $"clone_frame={clone.Frame}",
                    $"clone_length={clone.Length}",
                    $"clone_layer={clone.Layer}",
                    $"independent_clone={independent}",
                    $"placed={placed}",
                    $"frame_matches={frameMatches}",
                    $"length_preserved={lengthPreserved}",
                    $"source_unchanged={sourceUnchanged}"
                ]);
            }
            finally { ItemSettings.Default.Templates.Remove(template); }
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(output, "surface.txt"), "ERROR " + ex + Environment.NewLine, new UTF8Encoding(false));
            WriteResult(output, "FAIL_EXCEPTION", ["exception=" + ex.GetBaseException().GetType().Name, "message=" + ex.GetBaseException().Message]);
        }
    }

    private static PropertyInfo? FindFrameProperty(Type type)
    {
        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => x.CanRead && x.PropertyType == typeof(int)).ToArray();
        foreach (var name in ExactFrameNames)
        {
            var exact = props.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }
        return props.FirstOrDefault(x => x.Name.Contains("Frame", StringComparison.OrdinalIgnoreCase)
            && (x.Name.Contains("Current", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Position", StringComparison.OrdinalIgnoreCase))
            && !x.Name.Contains("Max", StringComparison.OrdinalIgnoreCase)
            && !x.Name.Contains("Length", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryMoveFrame(Timeline timeline, PropertyInfo prop, int frame, string output)
    {
        if (prop.SetMethod?.IsPublic == true)
        {
            prop.SetValue(timeline, frame);
            File.AppendAllText(Path.Combine(output, "surface.txt"), $"MOVE via property {prop.Name}={frame}\n", new UTF8Encoding(false));
            return true;
        }
        var method = timeline.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x => x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(int)
                && new[] { "SetCurrentFrame", "SetFrame", "Seek", "SeekFrame", "MoveToFrame" }.Any(n => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
        if (method == null) return false;
        method.Invoke(timeline, [frame]);
        File.AppendAllText(Path.Combine(output, "surface.txt"), $"MOVE via method {method.Name}({frame})\n", new UTF8Encoding(false));
        return true;
    }

    private static void DumpPublicSurface(Timeline timeline, string output)
    {
        var text = new StringBuilder().AppendLine("TYPE " + timeline.GetType().FullName);
        foreach (var m in timeline.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public)
                     .Where(x => new[] { "frame", "current", "position", "seek", "play", "cursor" }.Any(k => x.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(x => x.MemberType).ThenBy(x => x.Name))
            text.AppendLine(m.ToString());
        File.WriteAllText(Path.Combine(output, "surface.txt"), text.ToString(), new UTF8Encoding(false));
    }

    private static void WriteResult(string output, string status, IEnumerable<string> details)
        => File.WriteAllLines(Path.Combine(output, "result.txt"), new[] { "status=" + status }.Concat(details), new UTF8Encoding(false));
}
