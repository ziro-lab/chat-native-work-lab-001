using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using HarmonyLib;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;

namespace LayerPatanPatchSurfaceProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL LayerPatan Patch Surface Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Ymm = typeof(Timeline).Assembly;
    static readonly MethodInfo GetLayerHeight = typeof(YMMSettings).GetProperty(nameof(YMMSettings.LayerHeight))!.GetGetMethod()!;

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_LAYERPATAN_PATCH_SURFACE_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        Application.Current.Dispatcher.BeginInvoke(new Action(() => Run(Path.GetFullPath(dir))), DispatcherPriority.ApplicationIdle);
    }

    static void Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var lines = new List<string>
            {
                "status=PASS_LAYERPATAN_PATCH_SURFACE",
                $"ymm_version={typeof(Timeline).Assembly.GetName().Version}",
                $"harmony_version={typeof(Harmony).Assembly.GetName().Version}"
            };

            var total = 0;
            foreach (var name in new[] { "MainViewModel", "TimelineViewModel" })
            {
                var t = TypeOf(name);
                foreach (var method in AllMethods(t))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                        continue;
                    IList<CodeInstruction> code;
                    try { code = PatchProcessor.GetOriginalInstructions(method); }
                    catch (Exception ex)
                    {
                        lines.Add($"scan_error|{method.DeclaringType?.FullName}|{method.Name}|{ex.GetBaseException().GetType().Name}");
                        continue;
                    }

                    var div = 0;
                    var mul = 0;
                    for (var i = 1; i < code.Count; i++)
                    {
                        if (IsDivSite(code, i)) div++;
                        if (IsMulSite(code, i)) mul++;
                    }
                    if (div + mul > 0)
                    {
                        total += div + mul;
                        lines.Add($"site|root={name}|declaring={method.DeclaringType?.FullName}|method={method.Name}|visibility={Visibility(method)}|y_to_layer={div}|layer_to_y={mul}");
                    }
                }
            }
            lines.Add($"site_total={total}");

            AddTarget(lines, "item_top", "TimelineItemViewModel", "Top", true);
            AddTarget(lines, "item_height", "TimelineItemViewModel", "Height", true);
            AddTarget(lines, "item_update_height", "TimelineItemViewModel", "UpdateItemHeight", false);
            AddTarget(lines, "label_top", "TimelineLayerLabelItemViewModel", "Top", true);
            AddTarget(lines, "label_height", "TimelineLayerLabelItemViewModel", "Height", true);
            AddTarget(lines, "line_top", "TimelineLayerLineViewModel", "Top", true);
            AddTarget(lines, "line_height", "TimelineLayerLineViewModel", "Height", true);
            AddTarget(lines, "compute_max_layer", "TimelineViewModel", "ComputeMaxLayerCount", false, contains:true);
            AddTarget(lines, "delta", "TimelineItemViewModel", "GetDeltaFrameAndLayer", false);
            AddTargetFull(lines, "item_mousemove", "YukkuriMovieMaker.Views.TimelineItemView", "OnMouseMove");
            AddTargetFull(lines, "add_position", "YukkuriMovieMaker.Views.Converters.AddItemCommandParameterConverterBase", "GetTimelinePosition");
            AddTarget(lines, "space_top", "TimelineItemSpaceViewModel", "Top", true);
            AddTarget(lines, "space_height", "TimelineItemSpaceViewModel", "Height", true);
            AddTarget(lines, "scroll_to_item", "TimelineViewModel", "ScrollToItem", false);
            AddTarget(lines, "background_top", "TimelineItemBackgroundViewModel", "Top", true);
            AddTarget(lines, "background_height", "TimelineItemBackgroundViewModel", "Height", true);

            File.WriteAllLines(Path.Combine(output, "result.txt"), lines, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "result.txt"), "status=FAIL_EXCEPTION\n", new UTF8Encoding(false));
        }
    }

    static Type TypeOf(string name) => Ymm.GetType("YukkuriMovieMaker.ViewModels." + name) ?? throw new MissingMemberException(name);

    static IEnumerable<MethodInfo> AllMethods(Type type)
    {
        foreach (var method in type.GetMethods(All))
            yield return method;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (nested.ContainsGenericParameters) continue;
            foreach (var method in AllMethods(nested))
                yield return method;
        }
    }

    static bool IsLayerHeight(CodeInstruction ci) =>
        (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) &&
        ci.operand is MethodInfo m && m == GetLayerHeight;

    static bool IsSettingsDefault(CodeInstruction ci) =>
        ci.opcode == OpCodes.Call &&
        ci.operand is MethodInfo { Name: "get_Default" } m &&
        m.DeclaringType == typeof(SettingsBase<YMMSettings>);

    static bool IsDivSite(IList<CodeInstruction> code, int i) =>
        i + 3 < code.Count && IsSettingsDefault(code[i - 1]) && IsLayerHeight(code[i])
        && code[i + 1].opcode == OpCodes.Conv_R8 && code[i + 2].opcode == OpCodes.Div && code[i + 3].opcode == OpCodes.Conv_I4
        && NoLabels(code, i, i + 3);

    static bool IsMulSite(IList<CodeInstruction> code, int i) =>
        i + 1 < code.Count && IsSettingsDefault(code[i - 1]) && IsLayerHeight(code[i]) && code[i + 1].opcode == OpCodes.Mul
        && NoLabels(code, i, i + 1);

    static bool NoLabels(IList<CodeInstruction> code, int from, int to)
    {
        for (var k = from; k <= to; k++)
            if (code[k].labels.Count > 0 || code[k].blocks.Count > 0) return false;
        return true;
    }

    static string Visibility(MethodBase m) => m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private";

    static void AddTarget(List<string> lines, string label, string typeName, string member, bool property, bool contains=false)
    {
        var t = TypeOf(typeName);
        if (property)
        {
            var p = t.GetProperty(member, Inst);
            var g = p?.GetGetMethod(true);
            var s = p?.GetSetMethod(true);
            lines.Add($"target|{label}|type={t.FullName}|property={member}|getter={(g is null ? "none" : Visibility(g))}|setter={(s is null ? "none" : Visibility(s))}");
        }
        else
        {
            var ms = t.GetMethods(Inst|BindingFlags.Static).Where(m => contains ? m.Name.Contains(member) : m.Name == member).ToArray();
            lines.Add($"target|{label}|type={t.FullName}|method={member}|count={ms.Length}|vis={string.Join(",", ms.Select(Visibility))}|names={string.Join(",", ms.Select(m=>m.Name))}");
        }
    }

    static void AddTargetFull(List<string> lines, string label, string fullType, string member)
    {
        var t = Ymm.GetType(fullType);
        if (t is null) { lines.Add($"target|{label}|missing_type={fullType}"); return; }
        var ms = t.GetMethods(Inst|BindingFlags.Static).Where(m=>m.Name==member).ToArray();
        lines.Add($"target|{label}|type={fullType}|method={member}|count={ms.Length}|vis={string.Join(",",ms.Select(Visibility))}");
    }
}
