#pragma warning disable CA2255 // Intentional validation-only module bootstrap inside the host process.
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4SelectionProbe;

internal static class BehavioralProbeBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SELECTION_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _ = dispatcher.BeginInvoke(new Action(() => TimelineSelectionBehaviorProof.Start(Path.GetFullPath(dir))));
                    return;
                }
                await Task.Delay(100);
            }
        });
    }
}

internal static class TimelineSelectionBehaviorProof
{
    private static string output = "";

    internal static void Start(string outputDir)
    {
        output = outputDir;
        var ticks = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows.Cast<Window>()
                    .Select(x => x.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                var active = main?.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                if (active == null) return;
                var timeline = active.GetType().GetField("timeline", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                if (timeline == null) return;
                var fixtures = timeline.Items.Where(x => x.Remark == "CNWL_SELECTION_FIXTURE").ToArray();
                if (fixtures.Length != 2) return;

                timer.Stop();
                Run(timeline, fixtures.OfType<VoiceItem>().Single(), fixtures.OfType<TachieFaceItem>().Single());
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", false, false, false, false, false, []);
            }

            if (ticks >= 100)
            {
                timer.Stop();
                WriteResult("FAIL_TIMEOUT", false, false, false, false, false, []);
            }
        };
        timer.Start();
    }

    private static void Run(Timeline timeline, VoiceItem voice, TachieFaceItem face)
    {
        var changed = new List<string>();
        var propertyChangedSupported = timeline is INotifyPropertyChanged;
        if (timeline is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += (_, e) =>
            {
                changed.Add(e.PropertyName ?? "<null>");
                Append("PROPERTY_CHANGED " + (e.PropertyName ?? "<null>"));
            };
        }

        Append("PUBLIC_API SelectedItem=" + timeline.GetType().GetProperty("SelectedItem")?.PropertyType.FullName);
        Append("PUBLIC_API SelectedItems=" + timeline.GetType().GetProperty("SelectedItems")?.PropertyType.FullName);
        Append("PUBLIC_API SelectedItems.setter.public=" + (timeline.GetType().GetProperty("SelectedItems")?.SetMethod?.IsPublic == true));

        timeline.SelectedItems = ImmutableList.Create<IItem>(voice);
        Pump();
        var voiceSelected = ReferenceEquals(timeline.SelectedItem, voice)
            && timeline.SelectedItems.Count == 1
            && ReferenceEquals(timeline.SelectedItems[0], voice)
            && voice.CharacterName == "CNWL_SelectA";
        Append("ASSERT voice_selected=" + voiceSelected);

        timeline.SelectedItems = ImmutableList.Create<IItem>(face);
        Pump();
        var faceSelected = ReferenceEquals(timeline.SelectedItem, face)
            && timeline.SelectedItems.Count == 1
            && ReferenceEquals(timeline.SelectedItems[0], face)
            && face.CharacterName == "CNWL_SelectA";
        Append("ASSERT face_selected=" + faceSelected);

        timeline.SelectedItems = ImmutableList<IItem>.Empty;
        Pump();
        var clearObserved = timeline.SelectedItem == null && timeline.SelectedItems.Count == 0;
        Append("ASSERT selection_clear=" + clearObserved);

        var selectionEventObserved = changed.Any(x => x.Contains("Selected", StringComparison.OrdinalIgnoreCase));
        Append("ASSERT selection_property_changed=" + selectionEventObserved);

        var characterReadable = voice.CharacterName == "CNWL_SelectA" && face.CharacterName == "CNWL_SelectA";
        Append("ASSERT character_readable=" + characterReadable);

        var status = voiceSelected && faceSelected && clearObserved && characterReadable
            ? "PASS_TIMELINE_SELECTION"
            : "FAIL_ASSERTION";
        WriteResult(status, voiceSelected, faceSelected, clearObserved, characterReadable, propertyChangedSupported && selectionEventObserved, changed);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void WriteResult(string status, bool voiceSelected, bool faceSelected, bool clearObserved, bool characterReadable, bool eventObserved, IEnumerable<string> changed)
    {
        File.WriteAllLines(Path.Combine(output, "behavior-result.txt"),
        [
            "status=" + status,
            "voice_selected=" + voiceSelected,
            "face_selected=" + faceSelected,
            "selection_clear=" + clearObserved,
            "character_readable=" + characterReadable,
            "selection_event_observed=" + eventObserved,
            "property_changed_names=" + string.Join(",", changed.Distinct())
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "behavior-surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
