using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4P5FixtureViabilityProbe;

public sealed class P5FixtureViabilityEntry : ILocalizePlugin
{
    public string Name => "CNWL P5 Fixture Viability";
    public void SetCulture(CultureInfo cultureInfo) =>
        FixtureViabilityProbe.Schedule();
}

internal static class FixtureViabilityProbe
{
    private sealed record Outcome(
        string Type,
        bool Constructed,
        bool Added,
        bool Live,
        bool VmFound,
        bool GeometryFound,
        double? Left,
        double? Width,
        string Error);

    private static bool scheduled;
    private static string output = "";

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P5_FIXTURE_VIABILITY_DIR");

        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);

        var timer = new DispatcherTimer(
            DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        var ticks = 0;
        var created = false;

        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100)
                    throw new TimeoutException(
                        "MainViewModel bootstrap");

                foreach (Window window
                    in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName
                        != "YukkuriMovieMaker.ViewModels.MainViewModel")
                    {
                        continue;
                    }

                    var active = Get(
                        main,
                        "ActiveTimelineViewModel");

                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType()
                            .GetMethod(
                                "CreateProject",
                                Type.EmptyTypes)
                            ?.Invoke(main, null);
                        return;
                    }

                    if (active is null)
                        continue;

                    timer.Stop();
                    _ = Run(active);
                    return;
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Fail(ex);
            }
        };

        timer.Start();
    }

    private static object? Get(
        object? target,
        string name) =>
        target?.GetType()
            .GetProperty(
                name,
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic)
            ?.GetValue(target);

    private static void SetInt(
        object target,
        string name,
        int value)
    {
        var property = target.GetType()
            .GetProperty(
                name,
                BindingFlags.Instance
                | BindingFlags.Public)
            ?? throw new MissingMemberException(
                target.GetType().FullName,
                name);

        if (property.SetMethod?.IsPublic != true)
            throw new MissingMemberException(
                target.GetType().FullName,
                "public " + name + " setter");

        property.SetValue(target, value);
    }

    private static void SetStringIfPossible(
        object target,
        string name,
        string value)
    {
        var property = target.GetType()
            .GetProperty(
                name,
                BindingFlags.Instance
                | BindingFlags.Public);

        if (property?.SetMethod?.IsPublic == true
            && property.PropertyType == typeof(string))
        {
            property.SetValue(target, value);
        }
    }

    private static IEnumerable<object> VmItems(
        object active) =>
        Get(active, "Items") is IEnumerable items
            ? items.Cast<object>()
            : [];

    private static IItem? ItemOf(object vm) =>
        vm as IItem
        ?? Get(vm, "Item") as IItem;

    private static async Task Run(object active)
    {
        try
        {
            var timeline = Get(
                    active,
                    "Timeline") as Timeline
                ?? active.GetType()
                    .GetField(
                        "timeline",
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic)
                    ?.GetValue(active) as Timeline
                ?? throw new InvalidOperationException(
                    "Timeline missing.");

            var itemTypes = typeof(IItem).Assembly
                .GetTypes()
                .Where(type =>
                    type != typeof(IItem)
                    && typeof(IItem)
                        .IsAssignableFrom(type)
                    && !type.IsAbstract
                    && type.GetConstructor(
                        Type.EmptyTypes) is not null)
                .OrderBy(type =>
                    type.FullName,
                    StringComparer.Ordinal)
                .ToArray();

            var outcomes = new List<Outcome>();
            var itemsByType = new Dictionary<string, IItem>(
                StringComparer.Ordinal);

            for (var index = 0;
                index < itemTypes.Length;
                index++)
            {
                var type = itemTypes[index];
                var constructed = false;
                var added = false;
                var live = false;
                var vmFound = false;
                var geometryFound = false;
                double? left = null;
                double? width = null;
                var error = "";

                try
                {
                    var raw = Activator.CreateInstance(type)
                        ?? throw new InvalidOperationException(
                            "Activator returned null.");
                    constructed = true;

                    if (raw is not IItem item)
                        throw new InvalidCastException(
                            "Constructed object is not IItem.");

                    itemsByType[
                        type.FullName ?? type.Name] = item;

                    // Keep all item classes inside the same vertically visible
                    // row. The previous one-type-per-layer fixture only proved
                    // TimelineViewModel vertical virtualization, not a type
                    // compatibility difference.
                    var frame = 20 + index * 34;
                    const int layer = 1;

                    SetInt(raw, "Frame", frame);
                    SetInt(raw, "Layer", layer);
                    SetInt(raw, "Length", 20);
                    SetStringIfPossible(
                        raw,
                        "Remark",
                        "CNWL_P5_" + type.Name);

                    try
                    {
                        added = timeline.TryAddItems(
                            [item],
                            frame,
                            layer,
                            isItemSelectionEnabled: false);
                    }
                    catch (Exception ex)
                    {
                        error =
                            "add:"
                            + ex.GetBaseException().Message;
                    }

                    await Task.Delay(90);

                    live = timeline.Items.Any(
                        candidate =>
                            ReferenceEquals(candidate, item));

                    if (!added
                        && string.IsNullOrEmpty(error))
                    {
                        error = "TryAddItems=false";
                    }
                }
                catch (Exception ex)
                {
                    error = ex.GetBaseException().Message;
                }

                outcomes.Add(new Outcome(
                    type.FullName ?? type.Name,
                    constructed,
                    added,
                    live,
                    vmFound,
                    geometryFound,
                    left,
                    width,
                    error.ReplaceLineEndings(" ")));
            }

            // TimelineViewModel item VMs are refreshed asynchronously.
            // Classify geometry only after all fixtures have been inserted and
            // one UI-idle settle has had time to materialize the common VM list.
            await Task.Delay(1200);

            var settledVms = VmItems(active).ToArray();

            for (var i = 0; i < outcomes.Count; i++)
            {
                var outcome = outcomes[i];

                if (!itemsByType.TryGetValue(
                        outcome.Type,
                        out var item))
                {
                    continue;
                }

                var vm = settledVms.FirstOrDefault(
                    candidate =>
                        ReferenceEquals(
                            ItemOf(candidate),
                            item));

                if (vm is null)
                    continue;

                double? settledLeft = null;
                double? settledWidth = null;
                var settledGeometry = false;

                var leftProperty = vm.GetType()
                    .GetProperty(
                        "Left",
                        BindingFlags.Instance
                        | BindingFlags.Public);
                var widthProperty = vm.GetType()
                    .GetProperty(
                        "Width",
                        BindingFlags.Instance
                        | BindingFlags.Public);

                if (leftProperty?.GetMethod?.IsPublic == true
                    && widthProperty?.GetMethod?.IsPublic == true)
                {
                    settledLeft = Convert.ToDouble(
                        leftProperty.GetValue(vm));
                    settledWidth = Convert.ToDouble(
                        widthProperty.GetValue(vm));
                    settledGeometry =
                        double.IsFinite(settledLeft.Value)
                        && double.IsFinite(settledWidth.Value)
                        && settledWidth.Value >= 0;
                }

                outcomes[i] = outcome with
                {
                    VmFound = true,
                    GeometryFound = settledGeometry,
                    Left = settledLeft,
                    Width = settledWidth
                };
            }

            var lines = new List<string>
            {
                "status=PASS_P5_FIXTURE_VIABILITY",
                "host_version="
                    + typeof(IItem).Assembly
                        .GetName().Version,
                "tested_type_count="
                    + outcomes.Count,
                "constructed_count="
                    + outcomes.Count(x =>
                        x.Constructed),
                "added_count="
                    + outcomes.Count(x =>
                        x.Added),
                "live_count="
                    + outcomes.Count(x =>
                        x.Live),
                "vm_count="
                    + outcomes.Count(x =>
                        x.VmFound),
                "geometry_count="
                    + outcomes.Count(x =>
                        x.GeometryFound),
                "no_harmony_loaded="
                    + !AppDomain.CurrentDomain
                        .GetAssemblies()
                        .Any(assembly =>
                            assembly.GetName().Name
                                is "0Harmony"
                                or "HarmonyLib")
            };

            foreach (var outcome in outcomes)
            {
                lines.Add(
                    "fixture="
                    + outcome.Type
                    + "|constructed="
                    + outcome.Constructed
                    + "|added="
                    + outcome.Added
                    + "|live="
                    + outcome.Live
                    + "|vm="
                    + outcome.VmFound
                    + "|geometry="
                    + outcome.GeometryFound
                    + "|left="
                    + (outcome.Left?.ToString("R")
                        ?? "<null>")
                    + "|width="
                    + (outcome.Width?.ToString("R")
                        ?? "<null>")
                    + "|error="
                    + outcome.Error);
            }

            File.WriteAllLines(
                Path.Combine(output, "result.txt"),
                lines);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private static void Fail(Exception ex)
    {
        File.WriteAllText(
            Path.Combine(output, "error.txt"),
            ex.ToString());

        File.WriteAllLines(
            Path.Combine(output, "result.txt"),
            [
                "status=FAIL_P5_FIXTURE_VIABILITY",
                "error="
                    + ex.GetBaseException().Message
            ]);
    }
}
