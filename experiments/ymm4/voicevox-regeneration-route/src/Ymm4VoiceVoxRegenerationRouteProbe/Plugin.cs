using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceVoxRegenerationRouteProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VOICEVOX Regeneration Route Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEVOX_ROUTE_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    static void Run()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Concat([typeof(VoiceItem).Assembly, typeof(IVoiceSpeaker).Assembly])
                .Distinct()
                .ToArray();

            var allTypes = assemblies.SelectMany(SafeTypes).Distinct().ToArray();
            var vv = allTypes
                .Where(t => (t.FullName ?? "").Contains("VOICEVOX", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.FullName)
                .ToArray();

            var parameter = vv.FirstOrDefault(t => t.Name == "VOICEVOXVoiceParameter");
            var pronounce = vv.FirstOrDefault(t => t.Name == "VOICEVOXVoicePronounce");
            var speakers = vv.Where(t => typeof(IVoiceSpeaker).IsAssignableFrom(t)).ToArray();

            Check("voicevox_types_found", vv.Length > 0);
            Check("voicevox_parameter_type_found", parameter is not null);
            Check("voicevox_pronounce_type_found", pronounce is not null);

            static bool RelevantName(string n) =>
                n.Contains("voice", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("pronounce", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("hatsuon", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("query", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("synth", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("accent", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("mora", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("speaker", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("url", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("port", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("engine", StringComparison.OrdinalIgnoreCase);

            object DescribeType(Type t) => new
            {
                type = t.FullName,
                assembly = t.Assembly.GetName().Name,
                isPublic = t.IsPublic || t.IsNestedPublic,
                isAbstract = t.IsAbstract,
                isInterface = t.IsInterface,
                baseType = t.BaseType?.FullName,
                interfaces = t.GetInterfaces().Select(i => i.FullName).OrderBy(x => x).ToArray(),
                constructors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(c => new
                    {
                        visibility = c.IsPublic ? "public" : c.IsAssembly ? "internal" : c.IsPrivate ? "private" : "nonpublic",
                        signature = c.ToString()
                    }).ToArray(),
                properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => RelevantName(p.Name) || RelevantName(p.PropertyType.Name))
                    .Select(p => new
                    {
                        p.Name,
                        type = p.PropertyType.FullName,
                        isStatic = (p.GetMethod ?? p.SetMethod)?.IsStatic == true,
                        publicGet = p.GetMethod?.IsPublic == true,
                        publicSet = p.SetMethod?.IsPublic == true
                    }).ToArray(),
                methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => !m.IsSpecialName &&
                        (RelevantName(m.Name) ||
                         RelevantName(m.ReturnType.Name) ||
                         m.GetParameters().Any(p => RelevantName(p.ParameterType.Name))))
                    .Select(m => new
                    {
                        m.Name,
                        visibility = m.IsPublic ? "public" : m.IsAssembly ? "internal" : m.IsPrivate ? "private" : "nonpublic",
                        m.IsStatic,
                        returnType = m.ReturnType.FullName,
                        parameters = m.GetParameters().Select(p => new { p.Name, type = p.ParameterType.FullName }).ToArray()
                    }).ToArray(),
                literalFields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => f.IsLiteral && !f.IsInitOnly)
                    .Select(f => new
                    {
                        f.Name,
                        type = f.FieldType.FullName,
                        value = SafeConstant(f)
                    }).ToArray()
            };

            bool ReferencesVoiceVox(Type type)
            {
                var n = type.FullName ?? type.Name;
                return n.Contains("VOICEVOX", StringComparison.OrdinalIgnoreCase);
            }

            var crossReferences = allTypes
                .SelectMany(t =>
                {
                    try
                    {
                        return t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                            .Where(m => !m.IsSpecialName &&
                                (ReferencesVoiceVox(m.ReturnType) || m.GetParameters().Any(p => ReferencesVoiceVox(p.ParameterType))))
                            .Select(m => new
                            {
                                declaringType = t.FullName,
                                method = m.Name,
                                visibility = m.IsPublic ? "public" : m.IsAssembly ? "internal" : m.IsPrivate ? "private" : "nonpublic",
                                m.IsStatic,
                                returnType = m.ReturnType.FullName,
                                parameters = m.GetParameters().Select(p => new { p.Name, type = p.ParameterType.FullName }).ToArray()
                            }).ToArray();
                    }
                    catch { return []; }
                })
                .OrderBy(x => x.declaringType)
                .ThenBy(x => x.method)
                .ToArray();

            object Focus(Type? t) => t is null ? new { missing = true } : new
            {
                type = t.FullName,
                constructors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(x => x.ToString()).ToArray(),
                properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(p => new
                    {
                        p.Name,
                        type = p.PropertyType.FullName,
                        isStatic = (p.GetMethod ?? p.SetMethod)?.IsStatic == true,
                        publicGet = p.GetMethod?.IsPublic == true,
                        publicSet = p.SetMethod?.IsPublic == true
                    }).ToArray(),
                fields = t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => new
                    {
                        field.Name,
                        type = field.FieldType.FullName,
                        field.IsStatic,
                        field.IsPublic,
                        field.IsInitOnly
                    }).ToArray()
            };

            var result = new
            {
                host = "4.56.1.0 Lite",
                voiceVoxTypeCount = vv.Length,
                parameterType = parameter?.FullName,
                pronounceType = pronounce?.FullName,
                speakerTypes = speakers.Select(t => t.FullName).ToArray(),
                focused = new
                {
                    engine = Focus(vv.FirstOrDefault(t => t.Name == "VOICEVOXEngine")),
                    character = Focus(vv.FirstOrDefault(t => t.Name == "VOICEVOXCharacter")),
                    style = Focus(vv.FirstOrDefault(t => t.Name == "VOICEVOXStyle")),
                    parameter = Focus(parameter),
                    pronounce = Focus(pronounce),
                    speaker = Focus(speakers.FirstOrDefault()),
                    speakerInfo = Focus(vv.FirstOrDefault(t => t.Name == "VOICEVOXSpeakerInfo")),
                    api = Focus(vv.FirstOrDefault(t => t.Name == "VOICEVOXAPI"))
                },
                voiceVoxTypes = vv.Select(DescribeType).ToArray(),
                crossReferences
            };

            File.WriteAllText(Path.Combine(output, "surface.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Write("PASS_VOICEVOX_REGENERATION_ROUTE_INVENTORY", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_VOICEVOX_REGENERATION_ROUTE_INVENTORY", ex.ToString());
        }
    }

    static string? SafeConstant(FieldInfo f)
    {
        try { return f.GetRawConstantValue()?.ToString(); }
        catch { return null; }
    }

    static Type[] SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voicevox-regeneration-route.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
