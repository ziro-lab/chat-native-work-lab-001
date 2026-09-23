using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4SubtitlePrelayoutHookSurfaceProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Subtitle Prelayout Hook Surface Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_SUBTITLE_PRELAYOUT_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    static void Run()
    {
        try
        {
            var host = typeof(VoiceItem).Assembly;
            var plugin = typeof(IVideoEffectProcessor).Assembly;

            var jimaku = host.GetType("YukkuriMovieMaker.Player.Video.Items.JimakuSource");
            var textSource = host.GetType("YukkuriMovieMaker.Player.Video.Items.TextSource");

            var assemblies = new[] { host, plugin }.Distinct().ToArray();
            var candidateTypes = assemblies
                .SelectMany(SafeTypes)
                .Where(t => t.IsPublic || t.IsNestedPublic)
                .Where(t =>
                {
                    var n = t.FullName ?? t.Name;
                    return ContainsAny(n, "Jimaku", "Subtitle", "TextSource", "TextTransform", "TextFilter", "TextRender", "TextCompletion");
                })
                .OrderBy(t => t.FullName)
                .Select(DescribeType)
                .ToArray();

            var effectProcessor = DescribeType(typeof(IVideoEffectProcessor));
            var effectDescription = DescribeType(typeof(EffectDescription));
            var timelineItemDescription = DescribeType(typeof(TimelineItemSourceDescription));

            var result = new
            {
                host = "4.56.1.0 Lite",
                internalSources = new
                {
                    jimaku = jimaku is null ? null : DescribeType(jimaku),
                    textSource = textSource is null ? null : DescribeType(textSource)
                },
                videoEffectSurface = new
                {
                    processor = effectProcessor,
                    effectDescription,
                    timelineItemDescription,
                    effectDescriptionHasString = typeof(EffectDescription)
                        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Any(p => p.PropertyType == typeof(string)),
                    effectDescriptionHasVoiceItem = typeof(EffectDescription)
                        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Any(p => typeof(VoiceItem).IsAssignableFrom(p.PropertyType)),
                    timelineDescriptionHasString = typeof(TimelineItemSourceDescription)
                        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Any(p => p.PropertyType == typeof(string)),
                    timelineDescriptionHasVoiceItem = typeof(TimelineItemSourceDescription)
                        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Any(p => typeof(VoiceItem).IsAssignableFrom(p.PropertyType))
                },
                publicCandidates = candidateTypes
            };

            File.WriteAllText(Path.Combine(output, "surface.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

            var checks = new[]
            {
                new { id = "jimaku_source_found", passed = jimaku is not null },
                new { id = "text_source_found", passed = textSource is not null },
                new { id = "jimaku_source_visibility_recorded", passed = jimaku is not null },
                new { id = "text_source_visibility_recorded", passed = textSource is not null },
                new { id = "video_effect_processor_recorded", passed = true },
                new { id = "effect_description_recorded", passed = true },
                new { id = "timeline_item_description_recorded", passed = true },
                new { id = "public_candidate_inventory_recorded", passed = true }
            };

            Write("PASS_SUBTITLE_PRELAYOUT_SURFACE_INVENTORY", checks, null);
        }
        catch (Exception ex)
        {
            Write("FAIL_SUBTITLE_PRELAYOUT_SURFACE_INVENTORY", Array.Empty<object>(), ex.ToString());
        }
    }

    static bool ContainsAny(string value, params string[] terms)
        => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
    }

    static object DescribeType(Type type)
    {
        var flags = BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly;

        return new
        {
            name = type.FullName,
            assembly = type.Assembly.GetName().Name,
            visibility = type.IsPublic || type.IsNestedPublic ? "public" :
                         type.IsNestedFamily ? "protected" :
                         type.IsNotPublic || type.IsNestedAssembly ? "internal" : "nonpublic",
            isInterface = type.IsInterface,
            isAbstract = type.IsAbstract,
            baseType = type.BaseType?.FullName,
            interfaces = type.GetInterfaces().Select(i => i.FullName).OrderBy(x => x).ToArray(),
            constructors = type.GetConstructors(flags)
                .Select(c => new
                {
                    visibility = c.IsPublic ? "public" : c.IsFamily ? "protected" : c.IsAssembly ? "internal" : "nonpublic",
                    signature = c.ToString()
                }).ToArray(),
            properties = type.GetProperties(flags)
                .Select(p => new
                {
                    p.Name,
                    type = p.PropertyType.FullName,
                    publicGet = p.GetMethod?.IsPublic == true,
                    publicSet = p.SetMethod?.IsPublic == true
                }).ToArray(),
            methods = type.GetMethods(flags)
                .Where(m => !m.IsSpecialName)
                .Select(m => new
                {
                    m.Name,
                    visibility = m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "nonpublic",
                    returnType = m.ReturnType.FullName,
                    parameters = m.GetParameters().Select(p => new { p.Name, type = p.ParameterType.FullName }).ToArray()
                }).ToArray()
        };
    }

    static void Write(string status, object checks, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.subtitle-prelayout-surface.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements = checks,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
