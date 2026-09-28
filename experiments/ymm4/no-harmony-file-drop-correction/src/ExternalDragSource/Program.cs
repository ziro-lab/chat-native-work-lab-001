using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class Native
{
    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hWnd, StringBuilder value, int maxCount);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);

    internal const uint LeftDown = 0x0002;
    internal const uint LeftUp = 0x0004;
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 5)
                throw new ArgumentException("mode file targetX targetY resultPath");

            var mode = args[0];
            var file = Path.GetFullPath(args[1]);
            var targetX = double.Parse(args[2], CultureInfo.InvariantCulture);
            var targetY = double.Parse(args[3], CultureInfo.InvariantCulture);
            var resultPath = Path.GetFullPath(args[4]);

            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);

            return mode switch
            {
                "wpf" => RunWpf(file, targetX, targetY, resultPath),
                "explorer" => RunExplorer(file, targetX, targetY, resultPath),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode")
            };
        }
        catch (Exception ex)
        {
            try
            {
                var path = args.Length >= 5 ? Path.GetFullPath(args[4]) : Path.Combine(Path.GetTempPath(), "external-drag-source-error.txt");
                File.WriteAllText(path, "status=FAIL\n" + ex);
            }
            catch
            {
            }

            return 1;
        }
        finally
        {
            Native.mouse_event(Native.LeftUp, 0, 0, 0, 0);
        }
    }

    private static int RunWpf(string file, double targetX, double targetY, string resultPath)
    {
        var app = new Application();
        var border = new Border
        {
            Width = 96,
            Height = 72,
            Background = Brushes.DimGray
        };

        var window = new Window
        {
            Width = 120,
            Height = 100,
            Left = 30,
            Top = 30,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
            Topmost = true,
            Content = border,
            Title = "ExternalDragSource"
        };

        DragDropEffects effect = DragDropEffects.None;
        Exception? failure = null;
        Task? mover = null;

        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(250);
                window.Activate();
                window.UpdateLayout();

                var start = border.PointToScreen(new Point(border.ActualWidth / 2, border.ActualHeight / 2));
                Native.SetCursorPos((int)Math.Round(start.X), (int)Math.Round(start.Y));
                await Task.Delay(120);
                Native.mouse_event(Native.LeftDown, 0, 0, 0, 0);
                await Task.Delay(120);
                mover = StartMoverAfterPress(start, new Point(targetX, targetY));

                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, new[] { file });
                effect = DragDrop.DoDragDrop(border, data, DragDropEffects.Copy);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Native.mouse_event(Native.LeftUp, 0, 0, 0, 0);
                try { mover?.Wait(TimeSpan.FromSeconds(5)); } catch { }
                window.Close();
                app.Shutdown();
            }
        };

        app.Run(window);

        if (failure is not null)
            throw new InvalidOperationException("WPF external drag failed", failure);

        File.WriteAllLines(resultPath,
        [
            "status=PASS",
            "mode=wpf",
            "source_process=" + Environment.ProcessId,
            "effect=" + effect,
            "file=" + file
        ]);
        return 0;
    }

    private static int RunExplorer(string file, double targetX, double targetY, string resultPath)
    {
        var directory = Path.GetDirectoryName(file) ?? throw new InvalidOperationException("Missing directory");
        var fileName = Path.GetFileName(file);
        var displayName = Path.GetFileNameWithoutExtension(file);

        using var explorer = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + file + "\"",
            UseShellExecute = true
        });

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        nint explorerWindow = 0;
        AutomationElement? fileElement = null;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var hwnd in EnumerateExplorerWindows())
            {
                try
                {
                    var root = AutomationElement.FromHandle(hwnd);
                    var items = root.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

                    foreach (AutomationElement item in items)
                    {
                        var name = item.Current.Name;
                        if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var rect = item.Current.BoundingRectangle;
                        if (rect.Width <= 2 || rect.Height <= 2)
                            continue;

                        explorerWindow = hwnd;
                        fileElement = item;
                        break;
                    }
                }
                catch
                {
                }

                if (fileElement is not null)
                    break;
            }

            if (fileElement is not null)
                break;

            Thread.Sleep(200);
        }

        if (fileElement is null || explorerWindow == 0)
            throw new InvalidOperationException("Could not locate Explorer file item: " + file);

        // Keep Explorer away from the YMM4 drop target so the target remains
        // physically visible during the real shell drag.
        Native.SetWindowPos(explorerWindow, 0, 0, 0, 430, 500, 0);
        Native.SetForegroundWindow(explorerWindow);
        Thread.Sleep(500);

        var box = fileElement.Current.BoundingRectangle;
        var start = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);

        Native.SetCursorPos((int)Math.Round(start.X), (int)Math.Round(start.Y));
        Thread.Sleep(120);
        Native.mouse_event(Native.LeftDown, 0, 0, 0, 0);
        Thread.Sleep(140);

        for (var i = 1; i <= 18; i++)
        {
            Native.SetCursorPos(
                (int)Math.Round(start.X + (targetX - start.X) * i / 18.0),
                (int)Math.Round(start.Y + (targetY - start.Y) * i / 18.0));
            Thread.Sleep(70);
        }

        Thread.Sleep(220);
        Native.mouse_event(Native.LeftUp, 0, 0, 0, 0);
        Thread.Sleep(1400);

        try
        {
            if (explorerWindow != 0)
                Native.PostMessage(explorerWindow, 0x0010, 0, 0);
        }
        catch
        {
        }

        File.WriteAllLines(resultPath,
        [
            "status=PASS",
            "mode=explorer",
            "source_process=" + Environment.ProcessId,
            "explorer_window=" + explorerWindow,
            "directory=" + directory,
            "file=" + file
        ]);
        return 0;
    }

    private static Task StartMoverAfterPress(Point start, Point target) =>
        Task.Run(() =>
        {
            Thread.Sleep(220);

            for (var i = 1; i <= 18; i++)
            {
                Native.SetCursorPos(
                    (int)Math.Round(start.X + (target.X - start.X) * i / 18.0),
                    (int)Math.Round(start.Y + (target.Y - start.Y) * i / 18.0));
                Thread.Sleep(70);
            }

            Thread.Sleep(220);
            Native.mouse_event(Native.LeftUp, 0, 0, 0, 0);
        });

    private static IEnumerable<nint> EnumerateExplorerWindows()
    {
        var result = new List<nint>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd))
                return true;

            var name = new StringBuilder(256);
            Native.GetClassName(hwnd, name, name.Capacity);
            if (string.Equals(name.ToString(), "CabinetWClass", StringComparison.Ordinal))
                result.Add(hwnd);

            return true;
        }, 0);
        return result;
    }
}
