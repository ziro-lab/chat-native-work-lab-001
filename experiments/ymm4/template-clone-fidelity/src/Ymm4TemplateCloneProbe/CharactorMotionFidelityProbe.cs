using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4TemplateCloneProbe;

public sealed class CharactorMotionFidelityPluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — CharactorMotion Fidelity";
    public void SetCulture(CultureInfo cultureInfo) => CharactorMotionFidelityProbe.Schedule();
}

internal static class CharactorMotionFidelityProbe
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
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    private static void Run()
    {
        var evidence = new List<string>();
        try
        {
            var effectType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeTypes)
                .SingleOrDefault(t => t.FullName == "CharactorMotion.CharactorMotionEffect")
                ?? throw new InvalidOperationException("CharactorMotion.CharactorMotionEffect was not loaded.");
            var effect = Activator.CreateInstance(effectType) as IVideoEffect
                ?? throw new InvalidOperationException("CharactorMotionEffect could not be instantiated as IVideoEffect.");
            var zoom = effectType.GetProperty("ZoomCorrection", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ZoomCorrection property was not found.");
            var enabled = effectType.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("IsEnabled property was not found.");
            if (zoom.PropertyType != typeof(double) || zoom.SetMethod?.IsPublic != true)
                throw new InvalidOperationException("ZoomCorrection is not a public double setter.");
            if (enabled.PropertyType != typeof(bool) || enabled.SetMethod?.IsPublic != true)
                throw new InvalidOperationException("IsEnabled is not a public bool setter.");

            zoom.SetValue(effect, 5.0d);
            enabled.SetValue(effect, false);
            var sourceCharacter = new Character { Name = "CNWL CharactorMotion" };
            var canonicalCharacter = new Character { Name = sourceCharacter.Name };
            var source = new TachieFaceItem(sourceCharacter)
            {
                Frame = 12,
                Layer = 4,
                Length = 90,
                TachieFaceEffects = ImmutableList.Create(effect)
            };
            var clone = (TachieFaceItem)source.GetClone();
            var clonedEffect = clone.TachieFaceEffects.Single();
            evidence.Add("effect_type=" + clonedEffect.GetType().AssemblyQualifiedName);
            evidence.Add("clone_list_independent=" + !ReferenceEquals(source.TachieFaceEffects, clone.TachieFaceEffects));
            evidence.Add("clone_effect_independent=" + !ReferenceEquals(effect, clonedEffect));
            evidence.Add("clone_zoom=" + Convert.ToDouble(zoom.GetValue(clonedEffect), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            evidence.Add("clone_enabled=" + enabled.GetValue(clonedEffect));
            Assert(!ReferenceEquals(effect, clonedEffect), "CharactorMotion effect object is independently cloned", evidence);
            Assert(Convert.ToDouble(zoom.GetValue(clonedEffect), CultureInfo.InvariantCulture) == 5.0d,
                "CharactorMotion ZoomCorrection=5 survives TachieFaceItem.GetClone", evidence);
            Assert(Equals(enabled.GetValue(clonedEffect), false), "CharactorMotion IsEnabled=false survives TachieFaceItem.GetClone", evidence);

            var parameter = clone.TachieFaceParameter;
            var effects = clone.TachieFaceEffects;
            clone.Character = canonicalCharacter;
            clone.TachieFaceParameter = parameter;
            clone.TachieFaceEffects = effects;
            var reboundEffect = clone.TachieFaceEffects.Single();
            evidence.Add("rebind_character_is_canonical=" + ReferenceEquals(clone.Character, canonicalCharacter));
            evidence.Add("rebind_effect_same_saved_object=" + ReferenceEquals(clonedEffect, reboundEffect));
            evidence.Add("rebind_zoom=" + Convert.ToDouble(zoom.GetValue(reboundEffect), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            evidence.Add("rebind_enabled=" + enabled.GetValue(reboundEffect));
            Assert(ReferenceEquals(clone.Character, canonicalCharacter), "clone binds to canonical Character", evidence);
            Assert(ReferenceEquals(clonedEffect, reboundEffect), "saved CharactorMotion effect object survives Character setter refresh", evidence);
            Assert(Convert.ToDouble(zoom.GetValue(reboundEffect), CultureInfo.InvariantCulture) == 5.0d,
                "CharactorMotion ZoomCorrection=5 survives canonical Character rebind", evidence);
            Assert(Equals(enabled.GetValue(reboundEffect), false), "CharactorMotion IsEnabled=false survives canonical Character rebind", evidence);
            Assert(ReferenceEquals(source.Character, sourceCharacter), "source Template Character remains unchanged", evidence);
            Assert(Convert.ToDouble(zoom.GetValue(effect), CultureInfo.InvariantCulture) == 5.0d,
                "source CharactorMotion effect value remains unchanged", evidence);

            Write("PASS_CHARACTOR_MOTION_FIDELITY", evidence);
        }
        catch (Exception ex)
        {
            evidence.Add("error=" + ex);
            Write("FAIL_CHARACTOR_MOTION_FIDELITY", evidence);
        }
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static void Assert(bool value, string message, ICollection<string> evidence)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        evidence.Add("assert=PASS " + message);
    }

    private static void Write(string status, IEnumerable<string> evidence)
    {
        File.WriteAllLines(Path.Combine(output, "charactor-motion-result.txt"), new[] { "status=" + status }.Concat(evidence), new UTF8Encoding(false));
    }
}
