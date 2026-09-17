using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4TemplatePlacerUxHostSurfaceProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL — Template Placer UX host surface";
    private static int done;
    public void SetCulture(CultureInfo cultureInfo)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_TP_UX_SURFACE_DIR");
        if (Interlocked.Exchange(ref done, 1) != 0 || string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine("status=PASS_TEMPLATE_PLACER_UX_HOST_SURFACE");
            DumpType(sb, "ITOOLPLUGIN", typeof(IToolPlugin));
            DumpAttributes(sb, "ITOOLPLUGIN", typeof(IToolPlugin));
            DumpTimeline(sb);
            DumpUndoSurface(sb);
            DumpToolSurface(sb);
            DumpItemNameSurface(sb);
            File.WriteAllText(Path.Combine(dir, "surface.txt"), sb.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "result.txt"), "status=PASS_TEMPLATE_PLACER_UX_HOST_SURFACE\n", new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "result.txt"), "status=FAIL_TEMPLATE_PLACER_UX_HOST_SURFACE\nerror=" + ex, new UTF8Encoding(false));
        }
    }

    private static void DumpTimeline(StringBuilder sb)
    {
        var t = typeof(Timeline);
        sb.AppendLine("SECTION TIMELINE");
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(p => ContainsAny(p.Name, "Frame", "Current", "Selected", "Selection"))
                     .OrderBy(p => p.Name))
            sb.AppendLine($"TIMELINE_PROPERTY {p.Name} type={p.PropertyType.FullName} get={Access(p.GetMethod)} set={Access(p.SetMethod)}");
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                     .Where(m => ContainsAny(m.Name, "Frame", "Current", "Select", "Selection"))
                     .OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length))
            sb.AppendLine("TIMELINE_METHOD " + Method(m));
    }

    private static void DumpUndoSurface(StringBuilder sb)
    {
        sb.AppendLine("SECTION UNDO");
        foreach (var t in YmmTypes().Where(t => ContainsAny(t.FullName ?? t.Name, "Undo", "Redo", "History", "Transaction"))
                     .OrderBy(t => t.FullName))
        {
            sb.AppendLine("UNDO_TYPE " + t.AssemblyQualifiedName);
            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                sb.AppendLine("UNDO_CTOR " + Access(c) + " " + c);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(p => ContainsAny(p.Name, "Undo", "Redo", "History", "Transaction", "Command", "Group", "Merge", "Begin", "End")))
                sb.AppendLine($"UNDO_PROPERTY {t.FullName}::{p.Name} type={p.PropertyType.FullName} get={Access(p.GetMethod)} set={Access(p.SetMethod)}");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(m => ContainsAny(m.Name, "Undo", "Redo", "History", "Transaction", "Command", "Group", "Merge", "Begin", "End", "Execute", "Add"))
                         .OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length))
                sb.AppendLine("UNDO_METHOD " + Method(m));
        }
    }

    private static void DumpToolSurface(StringBuilder sb)
    {
        sb.AppendLine("SECTION TOOL_MENU");
        foreach (var t in YmmTypes().Where(t => ContainsAny(t.FullName ?? t.Name, "ToolPlugin", "ToolArea", "ToolMenu", "Utility", "Utilities"))
                     .OrderBy(t => t.FullName))
        {
            sb.AppendLine("TOOL_TYPE " + t.AssemblyQualifiedName);
            DumpAttributes(sb, "TOOL", t);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(p => ContainsAny(p.Name, "Category", "Group", "Order", "Menu", "Name", "Parent")))
                sb.AppendLine($"TOOL_PROPERTY {t.FullName}::{p.Name} type={p.PropertyType.FullName} get={Access(p.GetMethod)} set={Access(p.SetMethod)}");
        }
    }

    private static void DumpItemNameSurface(StringBuilder sb)
    {
        sb.AppendLine("SECTION ITEM_DISPLAY_NAME");
        var itemType = typeof(IItem);
        foreach (var t in YmmTypes().Where(t => ContainsAny(t.FullName ?? t.Name, "Item", "Localiz", "DisplayName", "TypeName", "NameProvider"))
                     .OrderBy(t => t.FullName))
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(m => m.ReturnType == typeof(string) && ContainsAny(m.Name, "Name", "Display", "Localiz", "Label", "Text")))
            {
                var ps = m.GetParameters();
                if (ps.Any(p => p.ParameterType == typeof(Type) || itemType.IsAssignableFrom(p.ParameterType) || p.ParameterType.IsAssignableFrom(itemType)))
                    sb.AppendLine("ITEM_NAME_METHOD " + Method(m));
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(p => p.PropertyType == typeof(string) && ContainsAny(p.Name, "Name", "Display", "Localiz", "Label", "Text")))
                sb.AppendLine($"ITEM_NAME_PROPERTY {t.FullName}::{p.Name} get={Access(p.GetMethod)} set={Access(p.SetMethod)}");
        }
        foreach (var known in new[] { typeof(VoiceItem), typeof(VideoItem), typeof(ImageItem), typeof(AudioItem), typeof(TextItem), typeof(TachieItem), typeof(ShapeItem), typeof(TransitionItem), typeof(FrameBufferItem) })
        {
            sb.AppendLine("ITEM_TYPE " + known.AssemblyQualifiedName);
            DumpAttributes(sb, "ITEM", known);
        }
    }

    private static IEnumerable<Type> YmmTypes() => AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
        .SelectMany(SafeTypes);

    private static Type[] SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static void DumpType(StringBuilder sb, string prefix, Type t)
    {
        sb.AppendLine($"{prefix}_TYPE {t.AssemblyQualifiedName}");
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).OrderBy(p => p.Name))
            sb.AppendLine($"{prefix}_PROPERTY {p.Name} type={p.PropertyType.FullName} get={Access(p.GetMethod)} set={Access(p.SetMethod)}");
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length))
            sb.AppendLine(prefix + "_METHOD " + Method(m));
    }

    private static void DumpAttributes(StringBuilder sb, string prefix, MemberInfo member)
    {
        foreach (var a in member.GetCustomAttributesData())
            sb.AppendLine($"{prefix}_ATTRIBUTE {member.Name} {a.AttributeType.AssemblyQualifiedName} args={string.Join(";", a.ConstructorArguments.Select(x => x.Value?.ToString() ?? "<null>"))}");
    }

    private static string Method(MethodInfo m) => $"{Access(m)} {(m.IsStatic ? "static" : "instance")} {m.DeclaringType?.FullName}::{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))}) -> {m.ReturnType.FullName}";
    private static string Access(MethodBase? m) => m == null ? "none" : m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "nonpublic";
    private static bool ContainsAny(string text, params string[] needles) => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
}
