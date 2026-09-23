using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4P5ItemInventoryProbe;

public sealed class P5ItemInventoryEntry : ILocalizePlugin
{
    public string Name => "CNWL P5 Item Inventory";

    public void SetCulture(CultureInfo cultureInfo) =>
        ItemInventoryProbe.Schedule();
}

internal static class ItemInventoryProbe
{
    private sealed record PropertySurface(
        string Name,
        string Type,
        bool PublicGet,
        bool PublicSet,
        string DeclaringType);

    private sealed record ConstructorSurface(
        string Signature,
        string[] Parameters);

    private sealed record ItemTypeSurface(
        string FullName,
        string Assembly,
        string AssemblyVersion,
        string? BaseType,
        bool Abstract,
        bool Sealed,
        bool PublicType,
        bool PublicParameterlessConstructor,
        ConstructorSurface[] PublicConstructors,
        PropertySurface? Frame,
        PropertySurface? Length,
        PropertySurface? Layer,
        string[] Interfaces);

    private static bool scheduled;
    private static string output = "";

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P5_ITEM_INVENTORY_DIR");

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
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
        catch
        {
            return [];
        }
    }

    private static PropertySurface? Surface(
        Type type,
        string propertyName)
    {
        var property = type.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        if (property is null)
            return null;

        return new PropertySurface(
            property.Name,
            property.PropertyType.FullName
                ?? property.PropertyType.Name,
            property.GetMethod?.IsPublic == true,
            property.SetMethod?.IsPublic == true,
            property.DeclaringType?.FullName
                ?? "<unknown>");
    }

    private static string ConstructorSignature(
        ConstructorInfo constructor) =>
        $"{constructor.DeclaringType?.Name}(" +
        string.Join(
            ", ",
            constructor.GetParameters()
                .Select(parameter =>
                    (parameter.ParameterType.FullName
                        ?? parameter.ParameterType.Name)
                    + " "
                    + parameter.Name))
        + ")";

    private static ItemTypeSurface Describe(Type type)
    {
        var constructors = type
            .GetConstructors(
                BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(constructor =>
                constructor.GetParameters().Length)
            .ThenBy(ConstructorSignature, StringComparer.Ordinal)
            .Select(constructor =>
                new ConstructorSurface(
                    ConstructorSignature(constructor),
                    constructor.GetParameters()
                        .Select(parameter =>
                            (parameter.ParameterType.FullName
                                ?? parameter.ParameterType.Name)
                            + " "
                            + parameter.Name)
                        .ToArray()))
            .ToArray();

        return new ItemTypeSurface(
            type.FullName ?? type.Name,
            type.Assembly.GetName().Name
                ?? "<unknown>",
            type.Assembly.GetName().Version?.ToString()
                ?? "<null>",
            type.BaseType?.FullName,
            type.IsAbstract,
            type.IsSealed,
            type.IsPublic || type.IsNestedPublic,
            constructors.Any(constructor =>
                constructor.Parameters.Length == 0),
            constructors,
            Surface(type, "Frame"),
            Surface(type, "Length"),
            Surface(type, "Layer"),
            type.GetInterfaces()
                .Where(@interface =>
                    typeof(IItem).IsAssignableFrom(@interface)
                    || @interface == typeof(IItem))
                .Select(@interface =>
                    @interface.FullName
                    ?? @interface.Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    private static void Run()
    {
        try
        {
            var itemInterface = typeof(IItem);

            var assemblies = AppDomain.CurrentDomain
                .GetAssemblies()
                .OrderBy(assembly =>
                    assembly.GetName().Name,
                    StringComparer.Ordinal)
                .ToArray();

            var all = assemblies
                .SelectMany(SafeTypes)
                .Where(type =>
                    type != itemInterface
                    && itemInterface.IsAssignableFrom(type))
                .Distinct()
                .OrderBy(type =>
                    type.FullName,
                    StringComparer.Ordinal)
                .Select(Describe)
                .ToArray();

            var concrete = all
                .Where(type => !type.Abstract)
                .ToArray();

            File.WriteAllText(
                Path.Combine(output, "inventory.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        status = "PASS_P5_ITEM_INVENTORY",
                        hostAssembly = typeof(IItem)
                            .Assembly
                            .GetName()
                            .ToString(),
                        loadedAssemblyCount = assemblies.Length,
                        itemTypeCount = all.Length,
                        concreteItemTypeCount = concrete.Length,
                        types = all
                    },
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

            var lines = new List<string>
            {
                "status=PASS_P5_ITEM_INVENTORY",
                "host_item_assembly="
                    + typeof(IItem).Assembly.GetName(),
                "loaded_assembly_count="
                    + assemblies.Length,
                "item_type_count="
                    + all.Length,
                "concrete_item_type_count="
                    + concrete.Length,
                "no_harmony_loaded="
                    + !assemblies.Any(assembly =>
                        assembly.GetName().Name
                            is "0Harmony" or "HarmonyLib")
            };

            foreach (var type in concrete)
            {
                lines.Add(
                    "type="
                    + type.FullName
                    + "|assembly="
                    + type.Assembly
                    + "|public="
                    + type.PublicType
                    + "|sealed="
                    + type.Sealed
                    + "|ctor0="
                    + type.PublicParameterlessConstructor
                    + "|ctors="
                    + string.Join(
                        ";",
                        type.PublicConstructors
                            .Select(ctor =>
                                ctor.Signature))
                    + "|frame="
                    + Format(type.Frame)
                    + "|length="
                    + Format(type.Length)
                    + "|layer="
                    + Format(type.Layer));
            }

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
                    "status=FAIL_P5_ITEM_INVENTORY",
                    "error=" + ex.GetBaseException().Message
                ]);
        }
    }

    private static string Format(
        PropertySurface? property) =>
        property is null
            ? "<missing>"
            : property.Type
                + ":get="
                + property.PublicGet
                + ":set="
                + property.PublicSet
                + ":decl="
                + property.DeclaringType;
}
