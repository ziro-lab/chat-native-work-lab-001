using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceVoxPronounceMutationProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VOICEVOX Pronounce Mutation Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static readonly List<object> requirements = [];

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEVOX_PRONOUNCE_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    private static void Run()
    {
        try
        {
            var voiceItemType = typeof(VoiceItem);
            var pronounceProperty = voiceItemType.GetProperty("Pronounce", BindingFlags.Instance | BindingFlags.Public);
            Check("voiceitem_pronounce_public_readwrite",
                pronounceProperty?.GetMethod?.IsPublic == true && pronounceProperty.SetMethod?.IsPublic == true);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Concat([voiceItemType.Assembly, typeof(IVoicePronounce).Assembly])
                .Distinct()
                .ToArray();

            var voiceVoxTypes = assemblies
                .SelectMany(SafeTypes)
                .Where(t => typeof(IVoicePronounce).IsAssignableFrom(t)
                    && !t.IsAbstract
                    && t.FullName?.Contains("voicevox", StringComparison.OrdinalIgnoreCase) == true)
                .Distinct()
                .OrderBy(t => t.FullName)
                .ToArray();

            Check("voicevox_pronounce_type_found", voiceVoxTypes.Length > 0);

            var voiceVoxPronounceType = voiceVoxTypes
                .FirstOrDefault(t => FindMember(t, "AudioQuery") is not null)
                ?? voiceVoxTypes.FirstOrDefault()
                ?? throw new InvalidOperationException("VOICEVOX IVoicePronounce implementation was not found.");

            var audioQueryMember = FindMember(voiceVoxPronounceType, "AudioQuery")
                ?? throw new MissingMemberException(voiceVoxPronounceType.FullName, "AudioQuery");
            Check("voicevox_audioquery_public", IsPublicReadable(audioQueryMember) && IsPublicWritable(audioQueryMember));

            var audioQueryType = MemberType(audioQueryMember);
            var accentPhrasesMember = FindMember(audioQueryType, "AccentPhrases")
                ?? FindMember(audioQueryType, "accent_phrases")
                ?? throw new MissingMemberException(audioQueryType.FullName, "AccentPhrases");
            Check("audioquery_accentphrases_public", IsPublicReadable(accentPhrasesMember));

            var phraseType = CollectionElementType(MemberType(accentPhrasesMember))
                ?? throw new InvalidOperationException("Could not resolve AccentPhrase element type.");
            var pauseMoraMember = FindMember(phraseType, "PauseMora")
                ?? FindMember(phraseType, "pause_mora")
                ?? throw new MissingMemberException(phraseType.FullName, "PauseMora");
            Check("accentphrase_pausemora_public", IsPublicReadable(pauseMoraMember) && IsPublicWritable(pauseMoraMember));

            var moraType = MemberType(pauseMoraMember);
            var vowelLengthMember = FindMember(moraType, "VowelLength")
                ?? FindMember(moraType, "vowel_length")
                ?? throw new MissingMemberException(moraType.FullName, "VowelLength");
            Check("mora_vowellength_public_writable", IsPublicReadable(vowelLengthMember) && IsPublicWritable(vowelLengthMember));

            var mora = CreateBare(moraType);
            SetValue(vowelLengthMember, mora, ConvertNumber(0.25, MemberType(vowelLengthMember)));
            var before = Convert.ToDouble(GetValue(vowelLengthMember, mora), CultureInfo.InvariantCulture);
            SetValue(vowelLengthMember, mora, ConvertNumber(0.0, MemberType(vowelLengthMember)));
            var after = Convert.ToDouble(GetValue(vowelLengthMember, mora), CultureInfo.InvariantCulture);
            Check("pause_duration_accepts_zero", before > 0 && after == 0d);

            bool nestedRoundTrip = TryBuildNested(
                voiceVoxPronounceType,
                audioQueryMember,
                audioQueryType,
                accentPhrasesMember,
                phraseType,
                pauseMoraMember,
                mora,
                vowelLengthMember,
                out var nestedObservation);
            Check("nested_pronounce_zero_observable", nestedRoundTrip);

            var editService = assemblies
                .SelectMany(SafeTypes)
                .FirstOrDefault(t => t.Name == "IVoiceItemEditService");

            var editSurface = editService is null
                ? null
                : new
                {
                    type = editService.FullName,
                    properties = editService.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(DescribeProperty).ToArray(),
                    methods = editService.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .Where(m => !m.IsSpecialName)
                        .Select(DescribeMethod).ToArray()
                };
            Check("voiceitem_edit_service_inventoried", editService is not null);

            var surface = new
            {
                host = "4.56.1.0 Lite",
                voiceItem = new
                {
                    type = voiceItemType.FullName,
                    pronounce = pronounceProperty is null ? null : DescribeProperty(pronounceProperty)
                },
                voiceVoxPronounceTypes = voiceVoxTypes.Select(t => new
                {
                    type = t.FullName,
                    constructors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(c => c.ToString()).ToArray(),
                    properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(DescribeProperty).ToArray()
                }).ToArray(),
                selectedVoiceVoxPronounceType = voiceVoxPronounceType.FullName,
                audioQuery = new
                {
                    type = audioQueryType.FullName,
                    properties = audioQueryType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(DescribeProperty).ToArray()
                },
                accentPhrase = new
                {
                    type = phraseType.FullName,
                    properties = phraseType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(DescribeProperty).ToArray()
                },
                mora = new
                {
                    type = moraType.FullName,
                    properties = moraType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(DescribeProperty).ToArray()
                },
                pauseMutation = new { before, after },
                nestedObservation,
                voiceItemEditService = editSurface
            };
            File.WriteAllText(Path.Combine(output, "surface.json"),
                JsonSerializer.Serialize(surface, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_VOICEVOX_PRONOUNCE_MUTATION_SURFACE", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_VOICEVOX_PRONOUNCE_MUTATION_SURFACE", ex.ToString());
        }
    }

    private static bool TryBuildNested(
        Type pronounceType,
        MemberInfo audioQueryMember,
        Type audioQueryType,
        MemberInfo accentPhrasesMember,
        Type phraseType,
        MemberInfo pauseMoraMember,
        object mora,
        MemberInfo vowelLengthMember,
        out object observation)
    {
        try
        {
            var phrase = CreateBare(phraseType);
            SetValue(pauseMoraMember, phrase, mora);

            var collection = CreateCollection(MemberType(accentPhrasesMember), phraseType, phrase);
            var query = CreateBare(audioQueryType);
            if (!TrySetValue(accentPhrasesMember, query, collection))
            {
                var existing = GetValue(accentPhrasesMember, query);
                if (existing is IList list)
                    list.Add(phrase);
                else
                    throw new InvalidOperationException("AccentPhrases is not publicly settable and no mutable IList instance exists.");
            }

            var pronounce = CreateBare(pronounceType);
            SetValue(audioQueryMember, pronounce, query);

            var query2 = GetValue(audioQueryMember, pronounce)
                ?? throw new InvalidOperationException("AudioQuery readback returned null.");
            var phrases2 = GetValue(accentPhrasesMember, query2) as IEnumerable
                ?? throw new InvalidOperationException("AccentPhrases readback is not enumerable.");
            var phrase2 = phrases2.Cast<object>().FirstOrDefault()
                ?? throw new InvalidOperationException("AccentPhrases readback is empty.");
            var mora2 = GetValue(pauseMoraMember, phrase2)
                ?? throw new InvalidOperationException("PauseMora readback returned null.");
            var zero = Convert.ToDouble(GetValue(vowelLengthMember, mora2), CultureInfo.InvariantCulture);

            observation = new
            {
                built = true,
                pronounceType = pronounce.GetType().FullName,
                queryType = query2.GetType().FullName,
                phraseType = phrase2.GetType().FullName,
                moraType = mora2.GetType().FullName,
                vowelLength = zero
            };
            return zero == 0d;
        }
        catch (Exception ex)
        {
            observation = new { built = false, error = ex.ToString() };
            return false;
        }
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }

    private static MemberInfo? FindMember(Type type, string requested)
    {
        static string N(string s) => s.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        var key = N(requested);
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .FirstOrDefault(m => N(m.Name) == key);
    }

    private static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        _ => throw new NotSupportedException(member.MemberType.ToString())
    };

    private static bool IsPublicReadable(MemberInfo member) => member switch
    {
        PropertyInfo p => p.GetMethod?.IsPublic == true,
        FieldInfo f => f.IsPublic,
        _ => false
    };

    private static bool IsPublicWritable(MemberInfo member) => member switch
    {
        PropertyInfo p => p.SetMethod?.IsPublic == true,
        FieldInfo f => f.IsPublic && !f.IsInitOnly,
        _ => false
    };

    private static object? GetValue(MemberInfo member, object instance) => member switch
    {
        PropertyInfo p => p.GetValue(instance),
        FieldInfo f => f.GetValue(instance),
        _ => throw new NotSupportedException(member.MemberType.ToString())
    };

    private static void SetValue(MemberInfo member, object instance, object? value)
    {
        if (!TrySetValue(member, instance, value))
            throw new InvalidOperationException($"{member.DeclaringType?.FullName}.{member.Name} is not publicly writable.");
    }

    private static bool TrySetValue(MemberInfo member, object instance, object? value)
    {
        switch (member)
        {
            case PropertyInfo p when p.SetMethod?.IsPublic == true:
                p.SetValue(instance, value);
                return true;
            case FieldInfo f when f.IsPublic && !f.IsInitOnly:
                f.SetValue(instance, value);
                return true;
            default:
                return false;
        }
    }

    private static object CreateBare(Type type)
    {
        if (type.IsValueType)
            return Activator.CreateInstance(type)!;

        var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, Type.EmptyTypes, modifiers: null);
        if (ctor is not null)
            return ctor.Invoke(null);

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static Type? CollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            return type.GetGenericArguments()[0];

        return type.GetInterfaces()
            .Where(i => i.IsGenericType)
            .FirstOrDefault(i => i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static object CreateCollection(Type declaredType, Type elementType, object item)
    {
        if (declaredType.IsArray)
        {
            var array = Array.CreateInstance(elementType, 1);
            array.SetValue(item, 0);
            return array;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(item);
        if (declaredType.IsAssignableFrom(listType))
            return list;

        var direct = declaredType.GetConstructor(Type.EmptyTypes)?.Invoke(null);
        if (direct is IList directList)
        {
            directList.Add(item);
            return directList;
        }

        var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
        var ctor = declaredType.GetConstructor([enumerableType]);
        if (ctor is not null)
            return ctor.Invoke([list]);

        throw new InvalidOperationException($"Cannot create collection for {declaredType.FullName}.");
    }

    private static object ConvertNumber(double value, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
    }

    private static object DescribeProperty(PropertyInfo p) => new
    {
        name = p.Name,
        type = p.PropertyType.FullName,
        publicGet = p.GetMethod?.IsPublic == true,
        publicSet = p.SetMethod?.IsPublic == true
    };

    private static object DescribeMethod(MethodInfo m) => new
    {
        name = m.Name,
        returnType = m.ReturnType.FullName,
        parameters = m.GetParameters().Select(p => new { p.Name, type = p.ParameterType.FullName }).ToArray()
    };

    private static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    private static void Write(string status, string? error)
    {
        var result = new
        {
            schema = "cnwl.voicevox-pronounce-mutation.v1",
            status,
            host = "4.56.1.0 Lite",
            sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            requirements,
            error
        };
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
