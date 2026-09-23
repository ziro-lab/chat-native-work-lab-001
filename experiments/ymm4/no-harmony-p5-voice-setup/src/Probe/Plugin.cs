using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4P5VoiceSetupProbe;

public sealed class P5VoiceSetupEntry : ILocalizePlugin
{
    public string Name => "CNWL P5 Voice Setup";
    public void SetCulture(CultureInfo cultureInfo) =>
        VoiceSetupProbe.Schedule();
}

internal static class VoiceSetupProbe
{
    private static bool scheduled;
    private static string output = "";

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P5_VOICE_SETUP_DIR");

        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);

        Application.Current.Dispatcher.BeginInvoke(
            new Action(Run),
            DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
        catch { return []; }
    }

    private static string DescribeProperty(
        object target,
        PropertyInfo property)
    {
        object? value = null;
        string error = "";
        if (property.GetMethod?.IsPublic == true
            && property.GetIndexParameters().Length == 0)
        {
            try { value = property.GetValue(target); }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
            }
        }

        string valueText =
            value switch
            {
                null => "<null>",
                string text => text,
                Type type => type.FullName ?? type.Name,
                Enum enumValue => enumValue.ToString(),
                ValueType scalar => scalar.ToString() ?? "<null>",
                _ => value.GetType().FullName
                    ?? value.GetType().Name
            };

        return property.Name
            + "|type="
            + (property.PropertyType.FullName
                ?? property.PropertyType.Name)
            + "|get="
            + (property.GetMethod?.IsPublic == true)
            + "|set="
            + (property.SetMethod?.IsPublic == true)
            + "|value="
            + valueText.ReplaceLineEndings(" ")
            + "|error="
            + error.ReplaceLineEndings(" ");
    }

    private static void Run()
    {
        try
        {
            var character = new Character
            {
                Name = "CNWL_P5_VOICE_SETUP"
            };
            var voice = new VoiceItem(character);

            static bool Relevant(PropertyInfo property) =>
                property.Name.Contains(
                    "Voice",
                    StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains(
                    "Speaker",
                    StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains(
                    "Character",
                    StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains(
                    "Plugin",
                    StringComparison.OrdinalIgnoreCase);

            var characterProperties = character.GetType()
                .GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public)
                .Where(Relevant)
                .OrderBy(property => property.Name)
                .Select(property =>
                    DescribeProperty(
                        character,
                        property))
                .ToArray();

            var voiceProperties = voice.GetType()
                .GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public)
                .Where(Relevant)
                .OrderBy(property => property.Name)
                .Select(property =>
                    DescribeProperty(
                        voice,
                        property))
                .ToArray();

            var loadedAssemblies = AppDomain.CurrentDomain
                .GetAssemblies()
                .OrderBy(assembly =>
                    assembly.GetName().Name,
                    StringComparer.Ordinal)
                .ToArray();

            var voiceTypes = loadedAssemblies
                .SelectMany(SafeTypes)
                .Where(type =>
                    (type.FullName ?? type.Name)
                        .Contains(
                            "Voice",
                            StringComparison.OrdinalIgnoreCase)
                    || (type.FullName ?? type.Name)
                        .Contains(
                            "Speaker",
                            StringComparison.OrdinalIgnoreCase))
                .Where(type =>
                    type.IsPublic
                    || type.IsNestedPublic)
                .OrderBy(type =>
                    type.FullName,
                    StringComparer.Ordinal)
                .Select(type =>
                    (type.FullName ?? type.Name)
                    + "|assembly="
                    + (type.Assembly.GetName().Name
                        ?? "<unknown>")
                    + "|interface="
                    + type.IsInterface
                    + "|abstract="
                    + type.IsAbstract)
                .ToArray();

            var pluginLikeTypes = voiceTypes
                .Where(line =>
                    line.Contains(
                        "Plugin",
                        StringComparison.OrdinalIgnoreCase)
                    || line.Contains(
                        "Speaker",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var interestingTypeNames = new[]
            {
                "YukkuriMovieMaker.Plugin.Voice.VoiceDescription",
                "YukkuriMovieMaker.Plugin.Community.Voice.Recording.RecordedVoicePlugin",
                "YukkuriMovieMaker.Plugin.Community.Voice.Recording.RecordedVoiceSpeaker",
                "YukkuriMovieMaker.Plugin.Community.Voice.Recording.RecordedVoiceParameter"
            };

            string DescribeTypeSurface(Type type)
            {
                var constructors = type
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                    .OrderBy(ctor => ctor.GetParameters().Length)
                    .Select(ctor =>
                        type.Name
                        + "("
                        + string.Join(
                            ",",
                            ctor.GetParameters().Select(parameter =>
                                (parameter.ParameterType.FullName
                                    ?? parameter.ParameterType.Name)
                                + " "
                                + parameter.Name))
                        + ")")
                    .ToArray();

                var properties = type
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property => property.GetIndexParameters().Length == 0)
                    .OrderBy(property => property.Name)
                    .Select(property =>
                        property.Name
                        + ":"
                        + (property.PropertyType.FullName
                            ?? property.PropertyType.Name)
                        + ":get="
                        + (property.GetMethod?.IsPublic == true)
                        + ":set="
                        + (property.SetMethod?.IsPublic == true))
                    .ToArray();

                var interfaces = type.GetInterfaces()
                    .Select(@interface =>
                        @interface.FullName
                        ?? @interface.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                return "type="
                    + (type.FullName ?? type.Name)
                    + "|ctors="
                    + string.Join(";", constructors)
                    + "|properties="
                    + string.Join(";", properties)
                    + "|interfaces="
                    + string.Join(";", interfaces);
            }

            var detailedVoiceTypes = loadedAssemblies
                .SelectMany(SafeTypes)
                .Where(type =>
                    type.FullName is not null
                    && interestingTypeNames.Contains(
                        type.FullName,
                        StringComparer.Ordinal))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(DescribeTypeSurface)
                .ToArray();

            var lines = new List<string>
            {
                "status=PASS_P5_VOICE_SETUP_DISCOVERY",
                "host_version="
                    + typeof(VoiceItem).Assembly
                        .GetName().Version,
                "character_type="
                    + character.GetType().FullName,
                "voice_type="
                    + voice.GetType().FullName,
                "character_relevant_property_count="
                    + characterProperties.Length,
                "voice_relevant_property_count="
                    + voiceProperties.Length,
                "public_voice_or_speaker_type_count="
                    + voiceTypes.Length,
                "plugin_or_speaker_type_count="
                    + pluginLikeTypes.Length,
                "no_harmony_loaded="
                    + !loadedAssemblies.Any(assembly =>
                        assembly.GetName().Name
                            is "0Harmony"
                            or "HarmonyLib")
            };

            lines.AddRange(
                characterProperties.Select(value =>
                    "character_property=" + value));
            lines.AddRange(
                voiceProperties.Select(value =>
                    "voice_property=" + value));
            lines.AddRange(
                pluginLikeTypes.Select(value =>
                    "voice_surface_type=" + value));
            lines.AddRange(
                detailedVoiceTypes.Select(value =>
                    "voice_detailed_surface=" + value));

            File.WriteAllLines(
                Path.Combine(output, "result.txt"),
                lines);
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(output, "error.txt"),
                ex.ToString());
            File.WriteAllLines(
                Path.Combine(output, "result.txt"),
                [
                    "status=FAIL_P5_VOICE_SETUP_DISCOVERY",
                    "error="
                        + ex.GetBaseException().Message
                ]);
        }
    }
}
