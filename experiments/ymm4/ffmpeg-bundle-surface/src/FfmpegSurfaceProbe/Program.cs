using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: FfmpegSurfaceProbe <ymm4Dir> <output>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var candidate = Path.Combine(root, name.Name + ".dll");
    return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
};

var lines = new List<string>();
foreach (var path in Directory.EnumerateFiles(root, "YukkuriMovieMaker*.dll", SearchOption.TopDirectoryOnly).OrderBy(x => x))
{
    try
    {
        var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        foreach (var type in SafeTypes(asm))
        {
            var typeHit = Contains(type.FullName);
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => Contains(m.Name) || (m is MethodInfo mi && (Contains(mi.ReturnType.FullName) || mi.GetParameters().Any(p => Contains(p.ParameterType.FullName)))) || (m is PropertyInfo pi && Contains(pi.PropertyType.FullName)))
                .ToArray();
            if (!typeHit && members.Length == 0) continue;
            lines.Add($"TYPE {type.FullName} public={type.IsPublic}");
            foreach (var m in members.OrderBy(x => x.MemberType).ThenBy(x => x.Name))
                lines.Add("  " + Describe(m));
        }
    }
    catch (Exception ex)
    {
        lines.Add($"ASSEMBLY_ERROR {Path.GetFileName(path)} {ex.GetBaseException().Message}");
    }
}
File.WriteAllLines(output, lines);
Console.WriteLine($"hits={lines.Count}");
return 0;

static bool Contains(string? s) => s?.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) == true || s?.Contains("ffprobe", StringComparison.OrdinalIgnoreCase) == true;
static Type[] SafeTypes(Assembly a)
{
    try { return a.GetTypes(); }
    catch (ReflectionTypeLoadException e) { return e.Types.Where(x => x != null).Cast<Type>().ToArray(); }
}
static string Describe(MemberInfo m) => m switch
{
    MethodInfo x => $"METHOD access={(x.IsPublic ? "public" : "nonpublic")} static={x.IsStatic} {x.ReturnType.FullName} {x.Name}({string.Join(",", x.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))})",
    PropertyInfo x => $"PROPERTY access={(x.GetMethod?.IsPublic == true ? "public" : "nonpublic")} static={x.GetMethod?.IsStatic == true} {x.PropertyType.FullName} {x.Name}",
    FieldInfo x => $"FIELD access={(x.IsPublic ? "public" : "nonpublic")} static={x.IsStatic} {x.FieldType.FullName} {x.Name}",
    _ => $"{m.MemberType} {m.Name}"
};
