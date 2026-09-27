using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Settings;

namespace Ymm4PreviewStartupForegroundProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Preview Startup Foreground Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_PREVIEW_STARTUP_FOREGROUND_DIR");
        var phase = Environment.GetEnvironmentVariable("CNWL_YMM4_PREVIEW_STARTUP_FOREGROUND_PHASE");
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(phase)) return;
        StartupForegroundProbe.Schedule(Path.GetFullPath(dir), phase);
    }
}

internal static class StartupForegroundProbe
{
    private const int SeedIndex = 63; // x16 in YMM4's quarter-step PlaybackRate scale.
    private const int ExtendedMaxIndex = 127;
    private static bool scheduled;

    public static void Schedule(string outputDir, string phase)
    {
        if (scheduled) return;
        scheduled = true;
        Directory.CreateDirectory(outputDir);
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => Start(outputDir, phase)),
            DispatcherPriority.ApplicationIdle);
    }

    private static void Start(string outputDir, string phase)
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        var ticks = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var combo = FindPlaybackRateSelector();
                if (combo is null)
                {
                    if (ticks < 400) return;
                    Write(outputDir, phase, "status=FAIL\nreason=PlaybackRate-bound ComboBox not found\n");
                    timer.Stop();
                    return;
                }

                timer.Stop();
                if (string.Equals(phase, "seed", StringComparison.OrdinalIgnoreCase))
                    RunSeed(outputDir, combo);
                else if (string.Equals(phase, "restart", StringComparison.OrdinalIgnoreCase))
                    RunRestart(outputDir, combo);
                else
                    Write(outputDir, phase, $"status=FAIL\nreason=Unknown phase: {phase}\n");
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write(outputDir, phase, "status=FAIL\n" + ex);
            }
        };
        timer.Start();
    }

    private static void RunSeed(string outputDir, ComboBox combo)
    {
        var binding = combo.GetBindingExpression(Selector.SelectedIndexProperty)
            ?? throw new InvalidOperationException("PlaybackRate SelectedIndex binding expression is missing.");

        var original = YMMSettings.Default.PlaybackRate;
        File.WriteAllText(
            Path.Combine(outputDir, "original-setting.txt"),
            original.ToString(CultureInfo.InvariantCulture),
            new UTF8Encoding(false));

        YMMSettings.Default.PlaybackRate = SeedIndex;
        binding.UpdateTarget();

        var result = new StringBuilder()
            .AppendLine("status=PASS")
            .AppendLine($"original_setting={original}")
            .AppendLine($"seed_setting={YMMSettings.Default.PlaybackRate}")
            .AppendLine($"seed_native_item_count={combo.Items.Count}")
            .AppendLine($"seed_selected_index={combo.SelectedIndex}")
            .AppendLine($"seed_foreground={DescribeBrush(combo.Foreground)}");

        Write(outputDir, "seed", result.ToString());

        var closeTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            Application.Current.MainWindow?.Close();
        };
        closeTimer.Start();
    }

    private static void RunRestart(string outputDir, ComboBox combo)
    {
        var binding = combo.GetBindingExpression(Selector.SelectedIndexProperty)
            ?? throw new InvalidOperationException("PlaybackRate SelectedIndex binding expression is missing.");

        var loadedSetting = YMMSettings.Default.PlaybackRate;
        var nativeItemCount = combo.Items.Count;
        var nativeSelectedIndex = combo.SelectedIndex;
        var nativeForeground = DescribeBrush(combo.Foreground);

        ExtendSelector(combo);
        binding.UpdateTarget();

        var selectedAfterExtend = combo.SelectedIndex;
        var foregroundAfterExtend = DescribeBrush(combo.Foreground);

        var immediateMarker = CreateMarkerBrush();
        combo.Foreground = immediateMarker;

        var beforeIdleMarker = ReferenceEquals(combo.Foreground, immediateMarker);
        var beforeIdleForeground = DescribeBrush(combo.Foreground);

        combo.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var markerSurvivedUntilIdle = ReferenceEquals(combo.Foreground, immediateMarker);
                var foregroundBeforeSettledReapply = DescribeBrush(combo.Foreground);

                // This mirrors the downstream fix: once startup work is settled,
                // refresh the native SelectedIndex binding and then re-apply the
                // high-speed local foreground exactly once.
                binding.UpdateTarget();
                var settledMarker = CreateMarkerBrush();
                combo.Foreground = settledMarker;

                var selectedAtSettledReapply = combo.SelectedIndex;
                var foregroundAfterSettledReapply = DescribeBrush(combo.Foreground);

                var settleTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
                {
                    Interval = TimeSpan.FromMilliseconds(900)
                };
                settleTimer.Tick += (_, _) =>
                {
                    settleTimer.Stop();
                    try
                    {
                        var markerStillOwned = ReferenceEquals(combo.Foreground, settledMarker);
                        var finalSelectedIndex = combo.SelectedIndex;
                        var finalForeground = DescribeBrush(combo.Foreground);

                        var original = ReadOriginal(outputDir);
                        var pass = loadedSetting == SeedIndex
                            && selectedAfterExtend == SeedIndex
                            && selectedAtSettledReapply == SeedIndex
                            && finalSelectedIndex == SeedIndex
                            && markerStillOwned;

                        var result = new StringBuilder()
                            .AppendLine($"status={(pass ? "PASS" : "FAIL")}")
                            .AppendLine($"loaded_setting={loadedSetting}")
                            .AppendLine($"persisted_x16={(loadedSetting == SeedIndex).ToString().ToLowerInvariant()}")
                            .AppendLine($"native_item_count={nativeItemCount}")
                            .AppendLine($"native_selected_index={nativeSelectedIndex}")
                            .AppendLine($"native_foreground={nativeForeground}")
                            .AppendLine($"selected_after_extend={selectedAfterExtend}")
                            .AppendLine($"foreground_after_extend={foregroundAfterExtend}")
                            .AppendLine($"immediate_marker_owned={beforeIdleMarker.ToString().ToLowerInvariant()}")
                            .AppendLine($"immediate_marker_foreground={beforeIdleForeground}")
                            .AppendLine($"immediate_marker_survived_until_idle={markerSurvivedUntilIdle.ToString().ToLowerInvariant()}")
                            .AppendLine($"foreground_before_settled_reapply={foregroundBeforeSettledReapply}")
                            .AppendLine($"selected_at_settled_reapply={selectedAtSettledReapply}")
                            .AppendLine($"foreground_after_settled_reapply={foregroundAfterSettledReapply}")
                            .AppendLine($"final_selected_index={finalSelectedIndex}")
                            .AppendLine($"final_marker_owned={markerStillOwned.ToString().ToLowerInvariant()}")
                            .AppendLine($"final_foreground={finalForeground}")
                            .AppendLine($"restore_setting={original}");

                        Write(outputDir, "restart", result.ToString());

                        YMMSettings.Default.PlaybackRate = original;
                        binding.UpdateTarget();

                        var closeTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
                        {
                            Interval = TimeSpan.FromSeconds(1)
                        };
                        closeTimer.Tick += (_, _) =>
                        {
                            closeTimer.Stop();
                            Application.Current.MainWindow?.Close();
                        };
                        closeTimer.Start();
                    }
                    catch (Exception ex)
                    {
                        Write(outputDir, "restart", "status=FAIL\n" + ex);
                        Application.Current.MainWindow?.Close();
                    }
                };
                settleTimer.Start();
            }
            catch (Exception ex)
            {
                Write(outputDir, "restart", "status=FAIL\n" + ex);
                Application.Current.MainWindow?.Close();
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    private static void ExtendSelector(ComboBox combo)
    {
        if (combo.ItemsSource is not null)
            throw new InvalidOperationException("PlaybackRate selector unexpectedly has ItemsSource.");

        for (var index = combo.Items.Count; index <= ExtendedMaxIndex; index++)
        {
            var multiplier = (index + 1) / 4.0;
            combo.Items.Add(new ComboBoxItem { Content = $"x {multiplier:0.##}" });
        }
    }

    private static Brush CreateMarkerBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(215, 35, 150), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 145, 190), 1.0));
        return brush;
    }

    private static int ReadOriginal(string outputDir)
    {
        var path = Path.Combine(outputDir, "original-setting.txt");
        if (File.Exists(path)
            && int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 3;
    }

    private static ComboBox? FindPlaybackRateSelector()
    {
        foreach (Window window in Application.Current.Windows)
        {
            foreach (var combo in Descendants<ComboBox>(window))
            {
                if (BindingOperations.GetBindingBase(combo, Selector.SelectedIndexProperty) is Binding binding
                    && string.Equals(binding.Path?.Path, "PlaybackRate", StringComparison.Ordinal)
                    && combo.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.PreviewViewModel")
                    return combo;
            }
        }

        return null;
    }

    private static string DescribeBrush(Brush? brush)
    {
        if (brush is null) return "<null>";
        if (brush is SolidColorBrush solid) return $"Solid:{solid.Color}:{solid.Opacity:0.###}";
        if (brush is LinearGradientBrush gradient) return $"LinearGradient:stops={gradient.GradientStops.Count}:opacity={gradient.Opacity:0.###}";
        return brush.GetType().Name + ":" + brush;
    }

    private static void Write(string outputDir, string phase, string content)
        => File.WriteAllText(Path.Combine(outputDir, $"{phase}-result.txt"), content, new UTF8Encoding(false));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
