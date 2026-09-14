using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4LayerProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Layer Probe";
    public void SetCulture(CultureInfo cultureInfo) => LayerProof.Schedule();
}

internal static class LayerProof
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_LAYER_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true; output = Path.GetFullPath(dir); Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }

    private static void Start()
    {
        var ticks = 0; var projectCreated = false;
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
                        projectCreated = true; main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null); break;
                    }
                    var timeline = active?.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (timeline == null) continue;
                    timer.Stop(); Run(timeline); return;
                }
                if (ticks >= 90) { timer.Stop(); Result("FAIL_TIMEOUT", []); }
            }
            catch (Exception ex)
            {
                timer.Stop(); File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));
                Result("FAIL_EXCEPTION", ["message="+ex.GetBaseException().Message]);
            }
        };
        timer.Start();
    }

    private static void Run(Timeline timeline)
    {
        const int frame = 130, length = 30;
        var a = new Character { Name = "CNWL_A" };
        var b = new Character { Name = "CNWL_B" };

        var fixtures = new IItem[]
        {
            new VoiceItem(a) { Frame=100, Length=100, Layer=10, Serif="A", Remark="CNWL_LAYER_FIXTURE" },
            new TachieFaceItem(a) { Frame=100, Length=100, Layer=18, Remark="CNWL_LAYER_FIXTURE" },
            new TachieFaceItem(a) { Frame=120, Length=40, Layer=22, Remark="CNWL_LAYER_FIXTURE" },
            new TachieFaceItem(b) { Frame=100, Length=100, Layer=40, Remark="CNWL_OTHER_CHARACTER" },
            new TachieFaceItem(b) { Frame=150, Length=20, Layer=23, Remark="CNWL_FRONT_BLOCKER" },
            new TachieFaceItem(b) { Frame=145, Length=10, Layer=9, Remark="CNWL_BACK_BLOCKER" }
        };
        foreach (var item in fixtures)
            if (!timeline.TryAddItems([item], item.Frame, item.Layer)) throw new InvalidOperationException("Fixture insertion failed at Layer " + item.Layer);

        var same = timeline.Items.Where(x => Overlaps(x,frame,length) && SameCharacter(x,a)).ToArray();
        var min = same.Min(x=>x.Layer); var max = same.Max(x=>x.Layer);
        var front = FindFree(timeline, max + 1, +1, frame, length);
        var back = FindFree(timeline, min - 1, -1, frame, length);

        var frontItem = new TachieFaceItem(a) { Frame=frame, Length=length, Layer=front, Remark="CNWL_FRONT_RESULT" };
        var backItem = new TachieFaceItem(a) { Frame=frame, Length=length, Layer=back, Remark="CNWL_BACK_RESULT" };
        if (!timeline.TryAddItems([frontItem],frame,front)) throw new InvalidOperationException("Front placement rejected by Timeline.");
        if (!timeline.TryAddItems([backItem],frame,back)) throw new InvalidOperationException("Back placement rejected by Timeline.");

        var frontBlockerCoversLaterOnly = fixtures.Single(x=>x.Remark=="CNWL_FRONT_BLOCKER").Frame > frame;
        var backBlockerCoversLaterOnly = fixtures.Single(x=>x.Remark=="CNWL_BACK_BLOCKER").Frame > frame;
        var pass = min==10 && max==22 && front==24 && back==8 && frontItem.Frame==frame && backItem.Frame==frame
            && frontItem.Length==length && backItem.Length==length && frontBlockerCoversLaterOnly && backBlockerCoversLaterOnly;
        Result(pass ? "PASS_CHARACTER_LAYER_PLACEMENT" : "FAIL_ASSERTION",
        [
            $"same_character_min={min}", $"same_character_max={max}", $"front_layer={front}", $"back_layer={back}",
            $"front_length={frontItem.Length}", $"back_length={backItem.Length}",
            $"front_late_blocker_test={frontBlockerCoversLaterOnly}", $"back_late_blocker_test={backBlockerCoversLaterOnly}",
            $"other_character_layer40_ignored_for_baseline={max < 40}"
        ]);
    }

    private static int FindFree(Timeline timeline, int first, int step, int frame, int length)
    {
        for (var layer=first; layer>=0 && layer<10000; layer+=step)
            if (!timeline.Items.Any(x=>x.Layer==layer && Overlaps(x,frame,length))) return layer;
        throw new InvalidOperationException("No free Layer in search direction.");
    }

    private static bool Overlaps(IItem x, int frame, int length)
        => (long)x.Frame < (long)frame + length && (long)frame < (long)x.Frame + x.Length;

    private static bool SameCharacter(IItem item, Character character) => item switch
    {
        VoiceItem v => Equals(v.Character, character),
        TachieFaceItem f => Equals(f.Character, character),
        _ => false
    };

    private static void Result(string status, IEnumerable<string> details)
        => File.WriteAllLines(Path.Combine(output,"result.txt"),new[]{"status="+status}.Concat(details),new UTF8Encoding(false));
}
