using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

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
            Append("ASSEMBLIES " + string.Join(" | ", assemblies.Select(a => a.FullName)));

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
                        if (decoded == null) continue;
                        var refs = decoded.Value.Refs;
                        if (!refs.Any(IsSourceTimeReference)) continue;
                        hits.Add(new Hit(method, refs, decoded.Value.Lines));
                    }
                }
            }

            Append($"SUMMARY methodsScanned={methodsScanned} hits={hits.Count}");
            foreach (var hit in hits.OrderBy(h => h.Method.DeclaringType?.FullName).ThenBy(h => h.Method.Name).Take(250))
            {
                Append("=== HIT " + Signature(hit.Method) + " ===");
                foreach (var reference in hit.Refs.Distinct(StringComparer.Ordinal)) Append("REF " + reference);
                foreach (var line in hit.Lines) Append("IL " + line);
            }

            var rateGetterHits = hits.Count(h => h.Refs.Any(r => r.Contains("VideoItem::get_PlaybackRate2", StringComparison.Ordinal)));
            var offsetGetterHits = hits.Count(h => h.Refs.Any(r => r.Contains("VideoItem::get_ContentOffset", StringComparison.Ordinal)));
            var animationGetValueHits = hits.Count(h => h.Refs.Any(r => r.Contains("Animation::GetValue", StringComparison.Ordinal)));
            var mapHits = hits.Count(h => h.Refs.Any(r => r.Contains("PlaybackRateMap", StringComparison.Ordinal)));

            Assert(rateGetterHits > 0, "at least one method references VideoItem.PlaybackRate2");
            Assert(offsetGetterHits > 0, "at least one method references VideoItem.ContentOffset");
            Assert(animationGetValueHits > 0 || mapHits > 0, "at least one method references Animation.GetValue or PlaybackRateMap");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_SOURCE_TIME_IL_SURFACE",
                "methods_scanned=" + methodsScanned,
                "hit_count=" + hits.Count,
                "playbackrate2_getter_hit_count=" + rateGetterHits,
                "contentoffset_getter_hit_count=" + offsetGetterHits,
                "animation_getvalue_hit_count=" + animationGetValueHits,
                "playbackratemap_hit_count=" + mapHits
            ], new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message], new UTF8Encoding(false));
        }
    }

    private static bool IsSourceTimeReference(string value) =>
        value.Contains("VideoItem::get_PlaybackRate2", StringComparison.Ordinal) ||
        value.Contains("VideoItem::get_ContentOffset", StringComparison.Ordinal) ||
        value.Contains("Animation::GetValue", StringComparison.Ordinal) ||
        value.Contains("PlaybackRateMap", StringComparison.Ordinal);

    private static (List<string> Lines, List<string> Refs)? Decode(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
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
            if (!OpCodesByValue.TryGetValue(code, out var op))
            {
                lines.Add($"{offset:X4}: <unknown 0x{code:X4}>");
                break;
            }

            string operandText = "";
            try
            {
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineI:
                        operandText = unchecked((sbyte)bytes[i]).ToString(CultureInfo.InvariantCulture); i += 1; break;
                    case OperandType.ShortInlineVar:
                        operandText = bytes[i].ToString(CultureInfo.InvariantCulture); i += 1; break;
                    case OperandType.ShortInlineBrTarget:
                        operandText = unchecked((sbyte)bytes[i]).ToString(CultureInfo.InvariantCulture); i += 1; break;
                    case OperandType.InlineVar:
                        operandText = BitConverter.ToUInt16(bytes, i).ToString(CultureInfo.InvariantCulture); i += 2; break;
                    case OperandType.InlineI:
                    case OperandType.InlineBrTarget:
                        operandText = BitConverter.ToInt32(bytes, i).ToString(CultureInfo.InvariantCulture); i += 4; break;
                    case OperandType.ShortInlineR:
                        operandText = BitConverter.ToSingle(bytes, i).ToString("R", CultureInfo.InvariantCulture); i += 4; break;
                    case OperandType.InlineI8:
                        operandText = BitConverter.ToInt64(bytes, i).ToString(CultureInfo.InvariantCulture); i += 8; break;
                    case OperandType.InlineR:
                        operandText = BitConverter.ToDouble(bytes, i).ToString("R", CultureInfo.InvariantCulture); i += 8; break;
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
                        if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
                            refs.Add(operandText);
                        break;
                    }
                    default:
                        operandText = "<operand:" + op.OperandType + ">";
                        break;
                }
            }
            catch (Exception ex)
            {
                operandText = "<decode-error:" + ex.GetBaseException().GetType().Name + ">";
                break;
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
            if (member == null) return "token:0x" + token.ToString("X8");
            return (member.DeclaringType?.FullName ?? member.Module.Name) + "::" + member.Name + " " + member;
        }
        catch
        {
            return "token:0x" + token.ToString("X8");
        }
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
