using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4TemplateCloneProbe;

public sealed class TemplateCreateFidelityPluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Template Create Fidelity";
    public void SetCulture(CultureInfo cultureInfo) => TemplateCreateFidelityProbe.Schedule();
}

internal static class TemplateCreateFidelityProbe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_TEMPLATE_CLONE_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(async () => await RunAsync()), DispatcherPriority.ApplicationIdle);
    }

    private static async Task RunAsync()
    {
        var evidence = new List<string>();
        try
        {
            var sourceCharacter = new Character { Name = "CNWL Template Create Character" };
            var canonicalCharacter = new Character { Name = sourceCharacter.Name };
            var (effect, effectProperty) = CreateEffectFixture();
            var expected = effectProperty.GetValue(effect);
            var source = new TachieFaceItem(sourceCharacter)
            {
                Frame = 12,
                Layer = 4,
                Length = 90,
                Remark = "CNWL_TEMPLATE_CREATE_SOURCE",
                TachieFaceEffects = ImmutableList.Create(effect)
            };

            var registry = TryGetCharacterRegistry();
            if (registry != null) registry.Add(canonicalCharacter);
            try
            {
                var template = new ItemTemplate();
                template.Items.Add(source);
                var created = (await template.CreateItemsAsync(30)).ToArray();
                var createdFace = created.OfType<TachieFaceItem>().Single();
                var createdEffect = createdFace.TachieFaceEffects.Single();
                var createdValue = effectProperty.GetValue(createdEffect);
                evidence.Add($"template_created_count={created.Length}");
                evidence.Add($"template_created_character_is_source={ReferenceEquals(createdFace.Character, sourceCharacter)}");
                evidence.Add($"template_created_character_is_canonical={ReferenceEquals(createdFace.Character, canonicalCharacter)}");
                evidence.Add($"template_created_character_name={createdFace.CharacterName}");
                evidence.Add($"template_created_effect_same_reference={ReferenceEquals(effect, createdEffect)}");
                evidence.Add($"template_created_effect_value={Format(createdValue)}");
                Assert(!ReferenceEquals(createdFace, source), "ItemTemplate.CreateItemsAsync returns an independent Face item", evidence);
                Assert(Equals(expected, createdValue), "ItemTemplate.CreateItemsAsync preserves a non-default effect value", evidence);

                var manual = (TachieFaceItem)source.GetClone();
                var manualEffectBefore = manual.TachieFaceEffects.Single();
                var beforeValue = effectProperty.GetValue(manualEffectBefore);
                manual.Character = canonicalCharacter;
                var manualEffectAfter = manual.TachieFaceEffects.Single();
                var afterValue = effectProperty.GetValue(manualEffectAfter);
                evidence.Add($"manual_rebind_character_is_canonical={ReferenceEquals(manual.Character, canonicalCharacter)}");
                evidence.Add($"manual_rebind_character_name={manual.CharacterName}");
                evidence.Add($"manual_rebind_effect_same_reference_before_after={ReferenceEquals(manualEffectBefore, manualEffectAfter)}");
                evidence.Add($"manual_rebind_effect_value_before={Format(beforeValue)}");
                evidence.Add($"manual_rebind_effect_value_after={Format(afterValue)}");
                Assert(ReferenceEquals(manual.Character, canonicalCharacter), "TachieFaceItem.Character can be rebound to the canonical Character", evidence);
                Assert(Equals(beforeValue, afterValue), "Character rebind preserves the cloned effect value", evidence);

                Write("PASS_TEMPLATE_CREATE_FIDELITY", evidence);
            }
            finally
            {
                if (registry != null) registry.Remove(canonicalCharacter);
            }
        }
        catch (Exception ex)
        {
            evidence.Add("error=" + ex);
            Write("FAIL_TEMPLATE_CREATE_FIDELITY", evidence);
        }
    }

    private static (IVideoEffect Effect, PropertyInfo Property) CreateEffectFixture()
    {
        var implementations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
            .SelectMany(SafeTypes)
            .Where(t => typeof(IVideoEffect).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName);
        foreach (var type in implementations)
        {
            IVideoEffect? effect = null;
            try { effect = Activator.CreateInstance(type) as IVideoEffect; } catch { }
            if (effect == null) continue;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.SetMethod?.IsPublic != true) continue;
                if (!TryDifferentValue(property.PropertyType, property.GetValue(effect), out var changed)) continue;
                property.SetValue(effect, changed);
                return (effect, property);
            }
        }
        throw new InvalidOperationException("No mutable built-in video effect fixture was found.");
    }

    private static IList? TryGetCharacterRegistry()
    {
        var type = typeof(Character).Assembly.GetType("YukkuriMovieMaker.Settings.CharacterSettings");
        var defaultProperty = type?.GetProperty("Default", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var charactersProperty = type?.GetProperty("Characters", BindingFlags.Public | BindingFlags.Instance);
        var registry = defaultProperty?.GetValue(null);
        return registry == null ? null : charactersProperty?.GetValue(registry) as IList;
    }

    private static bool TryDifferentValue(Type type, object? current, out object? changed)
    {
        if (type == typeof(bool)) { changed = !(current as bool? ?? false); return true; }
        if (type == typeof(int)) { changed = (current is int i ? i : 0) + 7; return true; }
        if (type == typeof(double)) { changed = (current is double d ? d : 0d) + 7.25d; return true; }
        if (type == typeof(float)) { changed = (current is float f ? f : 0f) + 7.25f; return true; }
        if (type.IsEnum)
        {
            changed = Enum.GetValues(type).Cast<object>().FirstOrDefault(x => !Equals(x, current));
            return changed != null;
        }
        changed = null;
        return false;
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static string Format(object? value) => value?.ToString() ?? "<null>";

    private static void Assert(bool value, string message, ICollection<string> evidence)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        evidence.Add("assert=PASS " + message);
    }

    private static void Write(string status, IEnumerable<string> evidence)
    {
        File.WriteAllLines(Path.Combine(output, "template-create-result.txt"), new[] { "status=" + status }.Concat(evidence), new UTF8Encoding(false));
    }
}
