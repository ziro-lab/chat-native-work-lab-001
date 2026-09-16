using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;

namespace Ymm4SourceTimeIlProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — VideoItem Source Time IL Surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => unchecked((ushort)op.Value));

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SOURCE_TIME_IL_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    private static void Run()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
                .ToArray();

            var hits = new List<Hit>();
            var methodsScanned = 0;
            foreach (var assembly in assemblies)
            {
                foreach (var type in SafeTypes(assembly))
                {
                    if (type.Namespace?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) != true) continue;
                    foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (method.IsAbstract || method.ContainsGenericParameters) continue;
                        MethodBody? body;
                        try { body = method.GetMethodBody(); } catch { continue; }
                        if (body == null) continue;
                        methodsScanned++;
                        var decoded = Decode(method);
                        if (decoded == null || !decoded.Value.Refs.Any(IsRelevantReference)) continue;
                        hits.Add(new Hit(method, decoded.Value.Refs, decoded.Value.Lines));
                    }
                }
            }

            Append($"SUMMARY methodsScanned={methodsScanned} hits={hits.Count}");
            foreach (var hit in hits.OrderBy(h => h.Method.DeclaringType?.FullName).ThenBy(h => h.Method.Name))
            {
                Append("=== HIT " + Signature(hit.Method) + " ===");
                foreach (var reference in hit.Refs.Distinct(StringComparer.Ordinal)) Append("REF " + reference);
                foreach (var line in hit.Lines) Append("IL " + line);
            }

            bool HasRef(Hit h, string s) => h.Refs.Any(r => r.Contains(s, StringComparison.Ordinal));
            var rateGetterHits = hits.Count(h => HasRef(h, "BaseItem::get_PlaybackRate2"));
            var offsetGetterHits = hits.Count(h => HasRef(h, "BaseItem::get_ContentOffset"));
            var mapHits = hits.Count(h => HasRef(h, "PlaybackRateMap"));
            var mapSourceTimeHits = hits.Count(h => HasRef(h, "PlaybackRateMap::GetSourceTime"));

            var videoUpdate = hits.SingleOrDefault(h => h.Method.DeclaringType?.FullName == "YukkuriMovieMaker.Player.Video.Items.VideoSource" && h.Method.Name == "Update");
            Assert(videoUpdate != null, "VideoSource.Update is present in the resolved IL hit set");
            Assert(HasRef(videoUpdate!, "BaseItem::get_PlaybackRateMap"), "VideoSource.Update reads BaseItem.PlaybackRateMap");
            Assert(HasRef(videoUpdate!, "BaseItem::get_ContentOffset"), "VideoSource.Update reads BaseItem.ContentOffset");
            Assert(HasRef(videoUpdate!, "VideoSource::CalculateSourceTime"), "VideoSource.Update calls VideoSource.CalculateSourceTime");

            var calculate = hits.SingleOrDefault(h => h.Method.DeclaringType?.FullName == "YukkuriMovieMaker.Player.Video.Items.VideoSource" && h.Method.Name == "CalculateSourceTime");
            Assert(calculate != null && HasRef(calculate, "PlaybackRateMap::GetSourceTime"), "VideoSource.CalculateSourceTime delegates to PlaybackRateMap.GetSourceTime");

            var mouth = hits.SingleOrDefault(h => h.Method.DeclaringType?.FullName == "YukkuriMovieMaker.Player.Video.Items.TachieSource" && h.Method.Name == "GetMouthShape");
            Assert(mouth != null, "TachieSource.GetMouthShape exposes the constant-rate branch");
            Assert(HasRef(mouth!, "PlaybackRateMap::get_IsConstant"), "constant-rate branch checks PlaybackRateMap.IsConstant");
            Assert(HasRef(mouth!, "BaseItem::get_PlaybackRate2"), "constant-rate branch reads PlaybackRate2");
            Assert(HasRef(mouth!, "Animation::GetFirstValue"), "constant-rate branch reads PlaybackRate2.GetFirstValue");
            Assert(HasRef(mouth!, "TimeSpan::op_Addition") && HasRef(mouth!, "TimeSpan::op_Multiply") && HasRef(mouth!, "TimeSpan::op_Division"),
                "constant-rate branch performs offset addition then rate multiplication/division");
            Assert(mouth!.Lines.Any(x => x.Contains("ldc.r8 100", StringComparison.Ordinal)), "constant-rate branch divides by the 100-percent basis");

            Assert(rateGetterHits > 0, "at least one native method references BaseItem.PlaybackRate2");
            Assert(offsetGetterHits > 0, "at least one native method references BaseItem.ContentOffset");
            Assert(mapHits > 0 && mapSourceTimeHits > 0, "PlaybackRateMap and GetSourceTime are used by native host methods");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_SOURCE_TIME_IL_SURFACE",
                "methods_scanned=" + methodsScanned,
                "hit_count=" + hits.Count,
                "playbackrate2_getter_hit_count=" + rateGetterHits,
                "contentoffset_getter_hit_count=" + offsetGetterHits,
                "playbackratemap_hit_count=" + mapHits,
                "playbackratemap_getsourcetime_hit_count=" + mapSourceTimeHits,
                "video_source_update_verified=True",
                "constant_rate_branch_verified=True"
            ], new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message], new UTF8Encoding(false));
        }
    }

    private static bool IsRelevantReference(string value) =>
        value.Contains("BaseItem::get_PlaybackRate2", StringComparison.Ordinal) ||
        value.Contains("BaseItem::get_ContentOffset", StringComparison.Ordinal) ||
        value.Contains("BaseItem::get_PlaybackRateMap", StringComparison.Ordinal) ||
        value.Contains("PlaybackRateMap::", StringComparison.Ordinal) ||
        value.Contains("VideoSource::CalculateSourceTime", StringComparison.Ordinal);

    private static (List<string> Lines, List<string> Refs)? Decode(MethodInfo method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes == null) return null;
        var lines = new List<string>();
        var refs = new List<string>();
        var i = 0;
        while (i < bytes.Length)
        {
            var offset = i;
            ushort code = bytes[i++];
            if (code == 0xFE)
            {
                if (i >= bytes.Length) break;
                code = (ushort)(0xFE00 | bytes[i++]);
            }
            if (!OpCodesByValue.TryGetValue(code, out var op)) break;
            string operandText = "";
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI: operandText = unchecked((sbyte)bytes[i]).ToString(CultureInfo.InvariantCulture); i += 1; break;
                case OperandType.ShortInlineVar: operandText = bytes[i].ToString(CultureInfo.InvariantCulture); i += 1; break;
                case OperandType.ShortInlineBrTarget: operandText = unchecked((sbyte)bytes[i]).ToString(CultureInfo.InvariantCulture); i += 1; break;
                case OperandType.InlineVar: operandText = BitConverter.ToUInt16(bytes, i).ToString(CultureInfo.InvariantCulture); i += 2; break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget: operandText = BitConverter.ToInt32(bytes, i).ToString(CultureInfo.InvariantCulture); i += 4; break;
                case OperandType.ShortInlineR: operandText = BitConverter.ToSingle(bytes, i).ToString("R", CultureInfo.InvariantCulture); i += 4; break;
                case OperandType.InlineI8: operandText = BitConverter.ToInt64(bytes, i).ToString(CultureInfo.InvariantCulture); i += 8; break;
                case OperandType.InlineR: operandText = BitConverter.ToDouble(bytes, i).ToString("R", CultureInfo.InvariantCulture); i += 8; break;
                case OperandType.InlineSwitch:
                {
                    var count = BitConverter.ToInt32(bytes, i); i += 4;
                    var targets = new int[count];
                    for (var n = 0; n < count; n++) { targets[n] = BitConverter.ToInt32(bytes, i); i += 4; }
                    operandText = "[" + string.Join(",", targets) + "]";
                    break;
                }
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.InlineString:
                case OperandType.InlineSig:
                {
                    var token = BitConverter.ToInt32(bytes, i); i += 4;
                    operandText = ResolveToken(method, token, op.OperandType);
                    if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType) refs.Add(operandText);
                    break;
                }
            }
            lines.Add($"{offset:X4}: {op.Name}{(operandText.Length > 0 ? " " + operandText : "")}");
        }
        return (lines, refs);
    }

    private static string ResolveToken(MethodInfo method, int token, OperandType operandType)
    {
        try
        {
            if (operandType == OperandType.InlineString) return "string:" + method.Module.ResolveString(token);
            if (operandType == OperandType.InlineSig) return "sig:0x" + token.ToString("X8");
            var typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : Type.EmptyTypes;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
            var member = method.Module.ResolveMember(token, typeArgs, methodArgs);
            return member == null ? "token:0x" + token.ToString("X8") : (member.DeclaringType?.FullName ?? member.Module.Name) + "::" + member.Name + " " + member;
        }
        catch { return "token:0x" + token.ToString("X8"); }
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static string Signature(MethodInfo method) =>
        (method.DeclaringType?.FullName ?? "<global>") + "::" + method.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName)) + ")";

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "il-surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
    private sealed record Hit(MethodInfo Method, List<string> Refs, List<string> Lines);
}
