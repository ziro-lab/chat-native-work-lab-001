using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4TemplateCloneProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Template Clone Fidelity";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_TEMPLATE_CLONE_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    private static void Run()
    {
        try
        {
            File.WriteAllText(Path.Combine(output, "surface.txt"), "", new UTF8Encoding(false));
            DumpType(typeof(TachieFaceItem));
            DumpType(typeof(ItemTemplate));
            DumpCloneBehavior();
            DumpTemplatePlacementCandidates();
            DumpJsonCandidates();
            WriteResult("PASS_TEMPLATE_CLONE_FIDELITY_DISCOVERY");
        }
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            WriteResult("FAIL_EXCEPTION", ex.GetBaseException().Message);
        }
    }

    private static void DumpType(Type type)
    {
        Append($"TYPE {type.AssemblyQualifiedName}");
        foreach (var ctor in type.GetConstructors(All).OrderBy(x => x.GetParameters().Length))
            Append("CTOR " + Describe(ctor));
        foreach (var property in type.GetProperties(All)
            .Where(p => p.Name.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Tachie", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Effect", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Template", StringComparison.OrdinalIgnoreCase) ||
                        p.Name is "Items" or "Frame" or "Layer" or "Length" or "Group" or "Remark")
            .OrderBy(p => p.Name))
        {
            var getter = property.GetMethod;
            var setter = property.SetMethod;
            Append($"PROPERTY {type.FullName}::{property.Name} type={property.PropertyType.FullName} get={(getter == null ? "none" : getter.IsPublic ? "public" : "nonpublic")} set={(setter == null ? "none" : setter.IsPublic ? "public" : "nonpublic")}");
        }
    }

    private static void DumpCloneBehavior()
    {
        var character = new Character { Name = "CNWL Template Character" };
        var source = new TachieFaceItem(character) { Frame = 123, Layer = 7, Length = 321, Group = 42, Remark = "CNWL_SOURCE" };
        var clone = source.GetClone() as TachieFaceItem;
        Assert(clone != null, "TachieFaceItem.GetClone returns TachieFaceItem");
        Assert(!ReferenceEquals(source, clone), "TachieFaceItem.GetClone returns an independent item object");
        Append($"CLONE character_reference_equal={ReferenceEquals(source.Character, clone!.Character)}");
        Append($"CLONE character_name_source={source.CharacterName}");
        Append($"CLONE character_name_clone={clone.CharacterName}");
        Append($"CLONE geometry_source={source.Frame},{source.Layer},{source.Length},{source.Group}");
        Append($"CLONE geometry_clone={clone.Frame},{clone.Layer},{clone.Length},{clone.Group}");

        foreach (var name in new[] { "Character", "CharacterName", "TachieFaceParameter", "TachieFaceEffects" })
        {
            var property = typeof(TachieFaceItem).GetProperty(name, All);
            if (property == null) { Append($"CLONE_PROPERTY {name} missing"); continue; }
            object? a = null, b = null;
            try { a = property.GetValue(source); b = property.GetValue(clone); } catch (Exception ex) { Append($"CLONE_PROPERTY {name} read_error={ex.GetBaseException().Message}"); continue; }
            Append($"CLONE_PROPERTY {name} type={property.PropertyType.FullName} same_reference={ReferenceEquals(a,b)} source_null={a == null} clone_null={b == null}");
            if (a is ICollection ac && b is ICollection bc) Append($"CLONE_COLLECTION {name} source_count={ac.Count} clone_count={bc.Count}");
        }

        var characterProperty = typeof(TachieFaceItem).GetProperty("Character", All);
        var characterNameProperty = typeof(TachieFaceItem).GetProperty("CharacterName", All);
        Append($"REBIND Character setter={(characterProperty?.SetMethod == null ? "none" : characterProperty.SetMethod.IsPublic ? "public" : "nonpublic")}");
        Append($"REBIND CharacterName setter={(characterNameProperty?.SetMethod == null ? "none" : characterNameProperty.SetMethod.IsPublic ? "public" : "nonpublic")}");
    }

    private static void DumpTemplatePlacementCandidates()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
            .ToArray();
        var candidates = new List<MethodInfo>();
        foreach (var assembly in assemblies)
        foreach (var type in SafeTypes(assembly))
        {
            if (type.Namespace?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) != true) continue;
            foreach (var method in type.GetMethods(All | BindingFlags.DeclaredOnly))
            {
                var parameters = method.GetParameters();
                var touchesTemplate = method.ReturnType == typeof(ItemTemplate) || parameters.Any(p => p.ParameterType == typeof(ItemTemplate));
                var touchesItem = typeof(IItem).IsAssignableFrom(method.ReturnType) || parameters.Any(p => typeof(IItem).IsAssignableFrom(p.ParameterType));
                var nameInteresting = method.Name.Contains("Template", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Clone", StringComparison.OrdinalIgnoreCase) ||
                    method.Name.Contains("Copy", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                    method.Name.Contains("Add", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Place", StringComparison.OrdinalIgnoreCase) ||
                    method.Name.Contains("Item", StringComparison.OrdinalIgnoreCase);
                if ((touchesTemplate || touchesItem) && nameInteresting) candidates.Add(method);
            }
        }
        foreach (var method in candidates.Distinct().OrderBy(m => m.DeclaringType?.FullName).ThenBy(m => m.Name).Take(500))
            Append("METHOD_CANDIDATE " + Describe(method));
        Append($"METHOD_CANDIDATE_COUNT {candidates.Distinct().Count()}");
        Assert(candidates.Count > 0, "YMM4 exposes at least one Item/Template-related method candidate for inspection");
    }

    private static void DumpJsonCandidates()
    {
        var json = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
            .SelectMany(SafeTypes).FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Json.Json");
        if (json == null) { Append("JSON_HELPER missing"); return; }
        Append("JSON_HELPER " + json.AssemblyQualifiedName);
        foreach (var method in json.GetMethods(All | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("Clone", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Copy", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Serial", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Deserial", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Read", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Write", StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Name))
            Append("JSON_CANDIDATE " + Describe(method));
    }

    private static string Describe(MethodBase member) => member switch
    {
        MethodInfo m => $"access={(m.IsPublic ? "public" : "nonpublic")} static={m.IsStatic} generic={m.IsGenericMethodDefinition} return={m.ReturnType.FullName} {m.DeclaringType?.FullName}::{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))})",
        ConstructorInfo c => $"access={(c.IsPublic ? "public" : "nonpublic")} {c.DeclaringType?.FullName}({string.Join(",", c.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))})",
        _ => member.ToString() ?? member.Name
    };

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, string detail = "") =>
        File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=" + status, "detail=" + detail], new UTF8Encoding(false));

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
