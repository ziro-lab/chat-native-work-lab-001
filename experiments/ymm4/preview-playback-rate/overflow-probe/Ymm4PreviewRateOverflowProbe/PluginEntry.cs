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

namespace Ymm4PreviewRateOverflowProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Preview Rate Overflow Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_PREVIEW_RATE_OVERFLOW_DIR");
        var phase = Environment.GetEnvironmentVariable("CNWL_YMM4_PREVIEW_RATE_OVERFLOW_PHASE");
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(phase)) return;
        OverflowProbe.Schedule(Path.GetFullPath(dir), phase);
    }
}

internal static class OverflowProbe
{
    private static bool scheduled;

    public static void Schedule(string outputDir, string phase)
    {
        if (scheduled) return;
        scheduled = true;
        Directory.CreateDirectory(outputDir);
        Application.Current.Dispatcher.BeginInvoke(new Action(() => Start(outputDir, phase)), DispatcherPriority.ApplicationIdle);
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
        File.WriteAllText(Path.Combine(outputDir, "original-setting.txt"), original.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));

        // Validate the synthetic Ctrl+. route one step below the target boundary.
        YMMSettings.Default.PlaybackRate = 126;
        binding.UpdateTarget();

        var log = new StringBuilder()
            .AppendLine("status=RUNNING")
            .AppendLine($"host_item_count={combo.Items.Count}")
            .AppendLine($"original_setting={original}")
            .AppendLine($"validation_start_setting={YMMSettings.Default.PlaybackRate}")
            .AppendLine($"validation_start_selected_index={combo.SelectedIndex}")
            .AppendLine($"validation_start_text={SafeText(combo)}");

        File.WriteAllText(Path.Combine(outputDir, "seed-progress.txt"), log.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDir, "input-validation-ready.txt"), "ready\n", new UTF8Encoding(false));

        var observed = new List<int> { YMMSettings.Default.PlaybackRate };
        var last = YMMSettings.Default.PlaybackRate;
        var routeValidated = false;
        var monitorTicks = 0;

        var monitor = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        monitor.Tick += (_, _) =>
        {
            try
            {
                monitorTicks++;
                var current = YMMSettings.Default.PlaybackRate;
                if (current != last)
                {
                    last = current;
                    observed.Add(current);
                }

                if (!routeValidated && current == 127)
                {
                    routeValidated = true;
                    File.WriteAllText(Path.Combine(outputDir, "boundary-ready.txt"), "ready\n", new UTF8Encoding(false));
                }

                if (!File.Exists(Path.Combine(outputDir, "shortcut-complete.txt")))
                {
                    if (monitorTicks < 600) return;
                    throw new TimeoutException("shortcut-complete.txt was not produced.");
                }

                monitor.Stop();

                var beforeDirect = YMMSettings.Default.PlaybackRate;
                binding.UpdateTarget();
                var afterShortcutSelectedIndex = combo.SelectedIndex;
                var afterShortcutText = SafeText(combo);

                YMMSettings.Default.PlaybackRate = 128;
                binding.UpdateTarget();
                var directAccepted = YMMSettings.Default.PlaybackRate == 128;
                var directSelectedIndex = combo.SelectedIndex;
                var directText = SafeText(combo);

                var result = new StringBuilder()
                    .AppendLine("status=PASS")
                    .AppendLine($"host_item_count={combo.Items.Count}")
                    .AppendLine($"input_route_validated={routeValidated.ToString().ToLowerInvariant()}")
                    .AppendLine($"shortcut_observed_settings={string.Join(",", observed)}")
                    .AppendLine($"shortcut_final_setting={beforeDirect}")
                    .AppendLine($"shortcut_selected_index={afterShortcutSelectedIndex}")
                    .AppendLine($"shortcut_text={afterShortcutText}")
                    .AppendLine($"direct_set_128_accepted={directAccepted.ToString().ToLowerInvariant()}")
                    .AppendLine($"direct_set_128_selected_index={directSelectedIndex}")
                    .AppendLine($"direct_set_128_text={directText}");

                Write(outputDir, "seed", result.ToString());

                // Leave the out-of-range value in place and perform a normal window
                // close so YMM4 gets its ordinary shutdown/persistence path.
                YMMSettings.Default.PlaybackRate = 128;
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
                monitor.Stop();
                Write(outputDir, "seed", "status=FAIL\n" + ex);
                Application.Current.MainWindow?.Close();
            }
        };
        monitor.Start();
    }

    private static void RunRestart(string outputDir, ComboBox combo)
    {
        var binding = combo.GetBindingExpression(Selector.SelectedIndexProperty)
            ?? throw new InvalidOperationException("PlaybackRate SelectedIndex binding expression is missing.");

        var loaded = YMMSettings.Default.PlaybackRate;
        binding.UpdateTarget();

        var loadedSelectedIndex = combo.SelectedIndex;
        var loadedText = SafeText(combo);
        var original = 3;
        var originalPath = Path.Combine(outputDir, "original-setting.txt");
        if (File.Exists(originalPath) && int.TryParse(File.ReadAllText(originalPath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            original = parsed;

        var result = new StringBuilder()
            .AppendLine("status=PASS")
            .AppendLine($"loaded_setting={loaded}")
            .AppendLine($"overflow_128_persisted={(loaded == 128).ToString().ToLowerInvariant()}")
            .AppendLine($"restart_selected_index={loadedSelectedIndex}")
            .AppendLine($"restart_text={loadedText}")
            .AppendLine($"host_item_count={combo.Items.Count}")
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

    private static string SafeText(ComboBox combo)
    {
        var selected = combo.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : combo.SelectedItem?.ToString();
        return $"text={combo.Text};selected_item={selected ?? "<null>"}".Replace("\r", " ").Replace("\n", " ");
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
