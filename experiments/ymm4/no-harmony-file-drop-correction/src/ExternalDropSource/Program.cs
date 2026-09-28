using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    internal const uint LeftDown = 2;
    internal const uint LeftUp = 4;
}

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 3)
            return 64;

        var path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
            return 66;

        if (!double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var targetX) ||
            !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var targetY))
            return 65;

        var exitCode = 1;
        var app = new Application();
        app.Startup += async (_, _) =>
        {
            try
            {
                exitCode = await Run(path, new Point(targetX, targetY));
            }
            catch
            {
                exitCode = 1;
            }
            finally
            {
                app.Shutdown();
            }
        };
        app.Run();
        return exitCode;
    }

    private static async Task<int> Run(string path, Point targetScreen)
    {
        var source = new Border
        {
            Width = 72,
            Height = 72,
            Background = Brushes.Gray
        };

        var window = new Window
        {
            Width = 96,
            Height = 96,
            Left = 24,
            Top = 24,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Content = source
        };

        window.Show();
        window.Activate();
        await Task.Delay(350);

        var start = source.PointToScreen(new Point(source.ActualWidth / 2, source.ActualHeight / 2));
        Native.SetCursorPos((int)Math.Round(start.X), (int)Math.Round(start.Y));
        await Task.Delay(120);
        Native.mouse_event(Native.LeftDown, 0, 0, 0, 0);
        await Task.Delay(100);

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { path });

        using var cancel = new CancellationTokenSource();
        var mover = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cancel.Token);
                for (var i = 1; i <= 16; i++)
                {
                    Native.SetCursorPos(
                        (int)Math.Round(start.X + (targetScreen.X - start.X) * i / 16.0),
                        (int)Math.Round(start.Y + (targetScreen.Y - start.Y) * i / 16.0));
                    await Task.Delay(80, cancel.Token);
                }

                await Task.Delay(250, cancel.Token);
                Native.mouse_event(Native.LeftUp, 0, 0, 0, 0);
            }
            catch (OperationCanceledException)
            {
            }
        });

        DragDropEffects effect;
        try
        {
            effect = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        finally
        {
            cancel.Cancel();
            Native.mouse_event(Native.LeftUp, 0, 0, 0, 0);
            window.Close();
        }

        try { await mover; }
        catch (OperationCanceledException) { }

        return effect == DragDropEffects.None ? 2 : 0;
    }
}
