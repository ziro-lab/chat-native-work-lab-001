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

namespace Ymm4PreviewRateUiProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Preview Rate UI Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_PREVIEW_RATE_UI_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        UiProbe.Schedule(Path.GetFullPath(dir));
    }
}

internal static class UiProbe
{
    private static bool scheduled;

    public static void Schedule(string outputDir)
    {
        if (scheduled) return;
        scheduled = true;
        Directory.CreateDirectory(outputDir);
        Application.Current.Dispatcher.BeginInvoke(new Action(() => Start(outputDir)));
    }

    private static void Start(string outputDir)
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        var ticks = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var rows = new List<string>();
                ComboBox? playback = null;

                foreach (Window window in Application.Current.Windows)
                {
                    rows.Add($"WINDOW type={window.GetType().FullName} title={window.Title} dc={window.DataContext?.GetType().FullName}");
                    foreach (var combo in Descendants<ComboBox>(window))
                    {
                        var selectedIndex = BindingPath(combo, Selector.SelectedIndexProperty);
                        var selectedItem = BindingPath(combo, Selector.SelectedItemProperty);
                        var selectedValue = BindingPath(combo, Selector.SelectedValueProperty);
                        var text = BindingPath(combo, ComboBox.TextProperty);
                        var first = combo.Items.Count > 0 ? DescribeItem(combo.Items[0]) : "<empty>";
                        var last = combo.Items.Count > 0 ? DescribeItem(combo.Items[combo.Items.Count - 1]) : "<empty>";
                        rows.Add($"COMBO name={combo.Name};dc={combo.DataContext?.GetType().FullName};count={combo.Items.Count};itemsSource={combo.ItemsSource?.GetType().FullName ?? "<null>"};selectedIndex={selectedIndex};selectedItem={selectedItem};selectedValue={selectedValue};text={text};first={first};last={last}");

                        if (ContainsPlaybackRate(selectedIndex) || ContainsPlaybackRate(selectedItem) || ContainsPlaybackRate(selectedValue) || ContainsPlaybackRate(text))
                            playback = combo;
                    }
                }

                File.WriteAllLines(Path.Combine(outputDir, "ui-dump.txt"), rows, new UTF8Encoding(false));
                if (playback is not null)
                {
                    var details = new StringBuilder()
                        .AppendLine("status=PASS")
                        .AppendLine($"type={playback.GetType().FullName}")
                        .AppendLine($"name={playback.Name}")
                        .AppendLine($"data_context={playback.DataContext?.GetType().FullName}")
                        .AppendLine($"item_count={playback.Items.Count}")
                        .AppendLine($"items_source={playback.ItemsSource?.GetType().FullName ?? "<null>"}")
                        .AppendLine($"selected_index_binding={BindingPath(playback, Selector.SelectedIndexProperty)}")
                        .AppendLine($"selected_item_binding={BindingPath(playback, Selector.SelectedItemProperty)}")
                        .AppendLine($"selected_value_binding={BindingPath(playback, Selector.SelectedValueProperty)}")
                        .AppendLine($"text_binding={BindingPath(playback, ComboBox.TextProperty)}")
                        .AppendLine($"first={DescribeItem(playback.Items.Count > 0 ? playback.Items[0] : null)}")
                        .AppendLine($"last={DescribeItem(playback.Items.Count > 0 ? playback.Items[playback.Items.Count - 1] : null)}");
                    File.WriteAllText(Path.Combine(outputDir, "result.txt"), details.ToString(), new UTF8Encoding(false));
                    timer.Stop();
                    return;
                }

                if (ticks >= 80)
                {
                    File.WriteAllText(Path.Combine(outputDir, "result.txt"), "status=FAIL\nreason=PlaybackRate-bound ComboBox not found\n", new UTF8Encoding(false));
                    timer.Stop();
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(outputDir, "result.txt"), "status=FAIL\n" + ex, new UTF8Encoding(false));
                timer.Stop();
            }
        };
        timer.Start();
    }

    private static bool ContainsPlaybackRate(string? value) => value?.Contains("PlaybackRate", StringComparison.OrdinalIgnoreCase) == true;

    private static string BindingPath(DependencyObject target, DependencyProperty property)
    {
        if (BindingOperations.GetBindingBase(target, property) is Binding binding)
            return binding.Path?.Path ?? "<binding-no-path>";
        return "<none>";
    }

    private static string DescribeItem(object? item)
    {
        if (item is null) return "<null>";
        if (item is ComboBoxItem cbi)
            return $"ComboBoxItem(content={cbi.Content},visibility={cbi.Visibility})";
        return $"{item.GetType().FullName}({item})";
    }

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
