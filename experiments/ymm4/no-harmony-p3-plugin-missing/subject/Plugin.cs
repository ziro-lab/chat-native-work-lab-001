using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4P3MissingSubject;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL P3 missing-plugin subject";
    public void SetCulture(CultureInfo cultureInfo) => Seeder.Schedule();
}

public sealed class SubjectToolPlugin : IToolPlugin
{
    public Type ViewModelType => typeof(SubjectToolViewModel);
    public Type ViewType => typeof(SubjectToolView);
    public string Name => "CNWL P3 Missing Subject";
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
    public int DefaultOrder => 9993;
}

public sealed class SubjectToolView : UserControl { }

public sealed class SubjectToolViewModel : IToolViewModel
{
    private string? savedState;
    event EventHandler<CreateNewToolViewRequestedEventArgs>? IToolViewModel.CreateNewToolViewRequested { add { } remove { } }
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add { } remove { } }
    public string Title => "CNWL P3 Missing Subject";
    public void LoadState(ToolState stateData) => savedState = stateData.SavedState;
    public ToolState SaveState() => new() { Title = Title, SavedState = savedState };
}

internal static class Seeder
{
    private static bool scheduled;
    private static string output = "";

    internal static void Schedule()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CNWL_P3_MISSING_PHASE"), "seed", StringComparison.Ordinal))
            return;
        var dir = Environment.GetEnvironmentVariable("CNWL_P3_MISSING_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static object? PublicProperty(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

    private static MethodInfo PublicMethod(object target, string name, params Type[] types) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, types)
        ?? throw new MissingMethodException(target.GetType().FullName, name);

    private static Timeline? TimelineOf(object root)
    {
        var active = PublicProperty(root, "ActiveTimelineViewModel");
        if (active is null) return null;
        return PublicProperty(active, "Timeline") as Timeline
            ?? active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
    }

    private static object FindArea(object root)
    {
        if (PublicProperty(root, "AnchorableAreaViewModels") is not IEnumerable areas)
            throw new InvalidOperationException("AnchorableAreaViewModels missing.");
        return areas.Cast<object>().First(x =>
            x.GetType().GetProperty("ViewModelType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(x) as Type
            == typeof(SubjectToolViewModel));
    }

    private static void Bootstrap()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(350) };
        var ticks = 0;
        var created = false;
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 120) throw new TimeoutException("seed bootstrap");
                var root = Application.Current.Windows.Cast<Window>().Select(x => x.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (root is null) return;
                var timeline = TimelineOf(root);
                if (timeline is null)
                {
                    if (!created)
                    {
                        created = true;
                        PublicMethod(root, "CreateProject", Type.EmptyTypes).Invoke(root, null);
                    }
                    return;
                }

                timer.Stop();
                var character = new Character { Name = "CNWL P3 Missing Marker Character" };
                var marker = new VoiceItem(character)
                {
                    Frame = 123,
                    Layer = 7,
                    Length = 60,
                    Serif = "marker",
                    Remark = "CNWL_P3_MISSING_MARKER"
                };
                if (!timeline.TryAddItems([marker], marker.Frame, marker.Layer))
                    throw new InvalidOperationException("Failed to add marker item.");

                var expected = FolderDocumentCodec.Save(new FolderDocument
                {
                    Timelines =
                    [
                        new TimelineFolderState
                        {
                            TimelineKey = timeline.ID.ToString("D"),
                            Folders =
                            [
                                new PersistedFolder
                                {
                                    Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                                    Start = 2,
                                    End = 6,
                                    Name = "Preserve Me",
                                    IsCollapsed = true
                                }
                            ]
                        }
                    ]
                });

                var area = FindArea(root);
                var load = area.GetType().GetMethod("LoadState", BindingFlags.Instance | BindingFlags.Public, [typeof(ToolState)])
                    ?? throw new MissingMethodException(area.GetType().FullName, "LoadState");
                load.Invoke(area, [new ToolState { Title = "CNWL P3 Missing Subject", SavedState = expected }]);

                File.WriteAllText(Path.Combine(output, "expected-state.txt"), expected);
                var source = Path.Combine(output, "subject-source.ymmp");
                PublicMethod(root, "SaveProject", typeof(string)).Invoke(root, [source]);
                File.WriteAllText(Path.Combine(output, "seed.done"), timeline.ID.ToString("D"));
            }
            catch (Exception ex)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(output, "seed.error.txt"), ex.ToString());
                File.WriteAllText(Path.Combine(output, "seed.done"), "FAIL");
            }
        };
        timer.Start();
    }
}
