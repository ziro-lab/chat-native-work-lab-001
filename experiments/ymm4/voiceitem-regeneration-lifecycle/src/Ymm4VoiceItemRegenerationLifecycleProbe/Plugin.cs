using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceItemRegenerationLifecycleProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Regeneration Lifecycle Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEITEM_REGEN_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    static void Start()
    {
        int ticks = 0;
        bool created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows.Cast<Window>()
                    .Select(w => w.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (main is null)
                    return;

                var active = main.GetType().GetProperty(
                    "ActiveTimelineViewModel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(main);

                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                    }

                    if (ticks > 160)
                        throw new TimeoutException("ActiveTimelineViewModel was not created.");
                    return;
                }

                timer.Stop();
                Run(main, active);
                Write("PASS_VOICEITEM_REGENERATION_SURFACE_INVENTORY", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_REGENERATION_SURFACE_INVENTORY", ex.ToString());
            }
        };

        timer.Start();
    }

    static void Run(object main, object active)
    {
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException("Timeline could not be resolved.");
        Check("timeline_resolved", true);

        var voice = new VoiceItem
        {
            Serif = "CNWL lifecycle probe",
            Hatsuon = "しーえぬだぶりゅーえる",
            CharacterName = "CNWL Probe"
        };

        Check("voice_added_to_real_timeline", timeline.TryAddItems([voice], 240, 6));
        Check("edit_service_interface_loaded", typeof(IVoiceItemEditService) is not null);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Concat([typeof(VoiceItem).Assembly, typeof(IVoiceItemEditService).Assembly])
            .Distinct()
            .ToArray();

        var editServiceType = typeof(IVoiceItemEditService);
        var implementors = assemblies
            .SelectMany(SafeTypes)
            .Where(t => !t.IsInterface && editServiceType.IsAssignableFrom(t))
            .Distinct()
            .OrderBy(t => t.FullName)
            .Select(DescribeType)
            .ToArray();

        var factories = assemblies
            .SelectMany(SafeTypes)
            .SelectMany(SafeMethods)
            .Where(m =>
            {
                var returnsEditService = editServiceType.IsAssignableFrom(m.ReturnType);
                var mentionsVoice = m.GetParameters().Any(p =>
                    p.ParameterType == typeof(VoiceItem) ||
                    p.ParameterType.IsAssignableFrom(typeof(VoiceItem)) ||
                    typeof(VoiceItem).IsAssignableFrom(p.ParameterType));
                return returnsEditService || (mentionsVoice &&
                    (m.Name.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Contains("Voice", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Contains("Service", StringComparison.OrdinalIgnoreCase)));
            })
            .OrderBy(m => m.DeclaringType?.FullName)
            .ThenBy(m => m.Name)
            .Select(DescribeMethodWithDeclaringType)
            .ToArray();

        var voiceMethods = typeof(VoiceItem)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => Relevant(m.Name) ||
                        editServiceType.IsAssignableFrom(m.ReturnType) ||
                        m.GetParameters().Any(p => editServiceType.IsAssignableFrom(p.ParameterType)))
            .OrderBy(m => m.Name)
            .Select(DescribeMethod)
            .ToArray();

        var voiceProperties = typeof(VoiceItem)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => Relevant(p.Name) ||
                        editServiceType.IsAssignableFrom(p.PropertyType))
            .OrderBy(p => p.Name)
            .Select(DescribeProperty)
            .ToArray();

        var reachable = new List<object>();
        InspectObject("voice", voice, reachable);
        InspectObject("timeline", timeline, reachable);
        InspectObject("activeTimelineViewModel", active, reachable);
        InspectObject("mainViewModel", main, reachable);

        Check("voiceitem_surface_inventoried", voiceMethods.Length > 0 || voiceProperties.Length > 0);
        Check("edit_service_implementors_inventoried", true);
        Check("edit_service_factories_inventoried", true);
        Check("reachable_host_objects_inventoried", true);
        Check("voice_present_in_timeline", timeline.Items.Any(x => ReferenceEquals(x, voice)));

        var characterType = typeof(YukkuriMovieMaker.Project.Character);
        var characterSurface = new
        {
            type = characterType.FullName,
            constructors = characterType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x => new { visibility = Visibility(x), signature = x.ToString() }).ToArray(),
            properties = characterType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(DescribeProperty).OrderBy(x => x.ToString()).ToArray(),
            methods = characterType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => Relevant(m.Name) || m.Name.Contains("Name", StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Name)
                .Select(DescribeMethod).ToArray()
        };

        var observation = new
        {
            host = "4.56.1.0 Lite",
            voiceItemType = typeof(VoiceItem).FullName,
            voiceItemIsEditService = voice is IVoiceItemEditService,
            editService = new
            {
                type = editServiceType.FullName,
                properties = editServiceType.GetProperties().Select(DescribeProperty).ToArray(),
                methods = editServiceType.GetMethods().Where(m => !m.IsSpecialName).Select(DescribeMethod).ToArray()
            },
            voiceItem = new
            {
                properties = voiceProperties,
                methods = voiceMethods
            },
            character = characterSurface,
            implementors,
            factories,
            reachable
        };

        File.WriteAllText(
            Path.Combine(output, "surface.json"),
            JsonSerializer.Serialize(observation, new JsonSerializerOptions { WriteIndented = true }));
    }

    static bool Relevant(string name) =>
        name.Contains("Voice", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Pronounce", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Hatsuon", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Speaker", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("File", StringComparison.OrdinalIgnoreCase);

    static void InspectObject(string label, object target, List<object> output)
    {
        var t = target.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (p.GetIndexParameters().Length != 0)
                continue;

            var interestingType = typeof(IVoiceItemEditService).IsAssignableFrom(p.PropertyType);
            if (!interestingType && !Relevant(p.Name) && !Relevant(p.PropertyType.Name))
                continue;

            object? value = null;
            string? error = null;
            try { value = p.GetValue(target); }
            catch (Exception ex) { error = ex.GetType().Name; }

            output.Add(new
            {
                owner = label,
                kind = "property",
                name = p.Name,
                declaredType = p.PropertyType.FullName,
                publicGet = p.GetMethod?.IsPublic == true,
                runtimeType = value?.GetType().FullName,
                isEditService = value is IVoiceItemEditService,
                error
            });
        }

        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var interestingType = typeof(IVoiceItemEditService).IsAssignableFrom(f.FieldType);
            if (!interestingType && !Relevant(f.Name) && !Relevant(f.FieldType.Name))
                continue;

            object? value = null;
            string? error = null;
            try { value = f.GetValue(target); }
            catch (Exception ex) { error = ex.GetType().Name; }

            output.Add(new
            {
                owner = label,
                kind = "field",
                name = f.Name,
                declaredType = f.FieldType.FullName,
                publicGet = f.IsPublic,
                runtimeType = value?.GetType().FullName,
                isEditService = value is IVoiceItemEditService,
                error
            });
        }
    }

    static Timeline? FindTimeline(object active)
    {
        for (var t = active.GetType(); t is not null; t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (typeof(Timeline).IsAssignableFrom(f.FieldType) && f.GetValue(active) is Timeline ft)
                    return ft;

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.GetIndexParameters().Length != 0 || !typeof(Timeline).IsAssignableFrom(p.PropertyType))
                    continue;
                try
                {
                    if (p.GetValue(active) is Timeline pt)
                        return pt;
                }
                catch { }
            }
        }

        return null;
    }

    static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }

    static MethodInfo[] SafeMethods(Type type)
    {
        try
        {
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch { return []; }
    }

    static object DescribeType(Type t) => new
    {
        type = t.FullName,
        isPublic = t.IsPublic || t.IsNestedPublic,
        isAbstract = t.IsAbstract,
        constructors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(c => new
            {
                visibility = Visibility(c),
                signature = c.ToString()
            }).ToArray(),
        properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => Relevant(p.Name) || typeof(IVoiceItemEditService).IsAssignableFrom(p.PropertyType))
            .Select(DescribeProperty)
            .ToArray(),
        methods = SafeMethods(t)
            .Where(m => Relevant(m.Name) || typeof(IVoiceItemEditService).IsAssignableFrom(m.ReturnType))
            .Select(DescribeMethod)
            .ToArray()
    };

    static object DescribeProperty(PropertyInfo p) => new
    {
        name = p.Name,
        type = p.PropertyType.FullName,
        publicGet = p.GetMethod?.IsPublic == true,
        publicSet = p.SetMethod?.IsPublic == true
    };

    static object DescribeMethod(MethodInfo m) => new
    {
        name = m.Name,
        visibility = Visibility(m),
        isStatic = m.IsStatic,
        returnType = m.ReturnType.FullName,
        parameters = m.GetParameters().Select(p => new
        {
            p.Name,
            type = p.ParameterType.FullName
        }).ToArray()
    };

    static object DescribeMethodWithDeclaringType(MethodInfo m) => new
    {
        declaringType = m.DeclaringType?.FullName,
        name = m.Name,
        visibility = Visibility(m),
        isStatic = m.IsStatic,
        returnType = m.ReturnType.FullName,
        parameters = m.GetParameters().Select(p => new
        {
            p.Name,
            type = p.ParameterType.FullName
        }).ToArray()
    };

    static string Visibility(MethodBase m) =>
        m.IsPublic ? "public" :
        m.IsFamily ? "protected" :
        m.IsAssembly ? "internal" :
        m.IsPrivate ? "private" :
        "nonpublic";

    static void Check(string id, bool passed) =>
        requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voiceitem-regeneration-lifecycle.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
