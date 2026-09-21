using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed record Host(Window Window, Timeline Timeline, object Vm, FrameworkElement View, FrameworkElement Source, ScrollViewer Scroll)
{
    internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static object? Get(object? instance, string name) => instance?.GetType().GetProperty(name, Flags)?.GetValue(instance);
    internal static IItem? Item(object? instance) => instance as IItem ?? Get(instance, "Item") as IItem;
    internal static object? Reactive(object? instance, string name) => Get(Get(instance, name), "Value");
    internal static void SetReactive(object instance, string name, object value)
    {
        var holder = Get(instance, name) ?? throw new MissingMemberException(name);
        var property = holder.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (property?.SetMethod?.IsPublic != true) throw new MissingMemberException(name + ".Value public setter");
        property.SetValue(holder, value);
    }
    internal static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>(); stack.Push(root);
        var count = 0;
        while (stack.Count > 0)
        {
            if (++count > 20000) throw new InvalidOperationException("Visual tree budget exceeded");
            var current = stack.Pop();
            if (current is FrameworkElement fe) yield return fe;
            for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
                stack.Push(VisualTreeHelper.GetChild(current, i));
        }
    }
    internal IEnumerable<FrameworkElement> ItemViews() => Elements(View).Where(x => x.GetType().Name == "TimelineItemView");
    internal FrameworkElement ItemView(IItem item)
    {
        var views = ItemViews().ToArray();
        var found = views.FirstOrDefault(x => ReferenceEquals(Item(x.DataContext), item));
        if (found is not null) return found;
        var live = Timeline.Items.ToArray();
        var detail = $"Item not realized: {item.Remark}; live_count={live.Length}; same_reference={live.Any(x => ReferenceEquals(x, item))}; view_count={views.Length}; "
            + $"vm_type={Vm.GetType().FullName}; view_vm_type={View.DataContext?.GetType().FullName}; same_vm={ReferenceEquals(Vm, View.DataContext)}; viewport={Reactive(Vm, "Viewport")}; "
            + "live=" + string.Join("|", live.Select(x => $"{x.Remark}:L{x.Layer}:F{x.Frame}"))
            + "; views=" + string.Join("|", views.Select(x => $"{Item(x.DataContext)?.Remark}:{x.DataContext?.GetType().Name}:visible={x.IsVisible}"));
        foreach (var owner in new[] { Window.DataContext, Vm, (object)Timeline }.Where(x => x is not null))
        {
            var type = owner!.GetType();
            detail += "; undo_surface_" + type.Name + "=" + string.Join("|", type.GetMembers(Flags)
                .Where(x => x.Name.Contains("Undo", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Commit", StringComparison.OrdinalIgnoreCase))
                .Take(40).Select(x => x.ToString()));
        }
        throw new InvalidOperationException(detail);
    }
    internal static FrameworkElement? AncestorItem(DependencyObject? source)
    {
        for (var depth = 0; source is not null && depth < 100; depth++)
        {
            if (source is FrameworkElement fe && fe.GetType().Name == "TimelineItemView") return fe;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
    internal static Rect ScreenRect(FrameworkElement view) => new(view.PointToScreen(new Point()), view.PointToScreen(new Point(view.ActualWidth, view.ActualHeight)));
    internal Point Center(IItem item)
    {
        var rect = ScreenRect(ItemView(item));
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        if (rect.Width < 4 || rect.Height < 4 || !ScreenRect(Scroll).Contains(center))
            throw new InvalidOperationException("Fixture not on screen: " + item.Remark + " " + rect);
        return center;
    }
    internal double LocalTop(IItem item) => ItemView(item).TranslatePoint(new Point(), Source).Y;
    internal double ModelTop(IItem item)
    {
        var context = ItemView(item).DataContext;
        if (!ReferenceEquals(Item(context), item)) throw new InvalidOperationException("View/model identity mismatch");
        var top = Get(context, "Top") ?? throw new MissingMemberException(context.GetType().FullName, "Top");
        return Convert.ToDouble(top);
    }
    internal Point OffsetScreen(Point start, double dx, double dy)
    {
        var local = Source.PointFromScreen(start);
        return Source.PointToScreen(new Point(local.X + dx, local.Y + dy));
    }
    internal static int ConverterLayer(Point point, int height)
    {
        var type = typeof(Timeline).Assembly.GetType("YukkuriMovieMaker.Views.Converters.AddItemCommandParameterConverterBase")
            ?? throw new TypeLoadException("AddItemCommandParameterConverterBase");
        var method = type.GetMethod("GetTimelinePosition", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("GetTimelinePosition");
        return method.Invoke(null, [new object[] { point, 1.0, height }]) is ValueTuple<int, int> pair
            ? pair.Item2 : throw new InvalidOperationException("Unexpected converter result");
    }
    internal void Activate() { Window.Activate(); Native.SetForegroundWindow(new WindowInteropHelper(Window).Handle); }
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
    internal static void Release() { mouse_event(4 | 16, 0, 0, 0, 0); keybd_event(0x10, 0, 2, 0); keybd_event(0x11, 0, 2, 0); }
    private static void Move(Point p)
    {
        if (!SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y))) throw new InvalidOperationException("SetCursorPos failed");
    }
    internal static async Task Click(Point p, bool right = false)
    {
        Move(p); await Task.Delay(140); mouse_event(right ? 8u : 2u, 0, 0, 0, 0);
        await Task.Delay(90); mouse_event(right ? 16u : 4u, 0, 0, 0, 0); await Task.Delay(250);
    }
    internal static async Task Drag(Point a, Point b, bool shift = false)
    {
        if (shift) keybd_event(0x10, 0, 0, 0);
        try
        {
            Move(a); await Task.Delay(140); mouse_event(2, 0, 0, 0, 0); await Task.Delay(140);
            for (var i = 1; i <= 10; i++)
            {
                Move(new Point(a.X + (b.X - a.X) * i / 10, a.Y + (b.Y - a.Y) * i / 10));
                await Task.Delay(70);
            }
        }
        finally { mouse_event(4, 0, 0, 0, 0); if (shift) keybd_event(0x10, 0, 2, 0); }
        await Task.Delay(400);
    }
    internal static async Task Key(byte key, bool ctrl = false)
    {
        if (ctrl) keybd_event(0x11, 0, 0, 0);
        try { await Task.Delay(60); keybd_event(key, 0, 0, 0); await Task.Delay(60); }
        finally { keybd_event(key, 0, 2, 0); if (ctrl) keybd_event(0x11, 0, 2, 0); }
        await Task.Delay(450);
    }
}
