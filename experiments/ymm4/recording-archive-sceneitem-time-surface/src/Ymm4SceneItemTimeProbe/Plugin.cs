using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4SceneItemTimeProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive SceneItem Time Surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static readonly Dictionary<ushort, OpCode> Ops = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => unchecked((ushort)o.Value));

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SCENEITEM_TIME_DIR");
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
            var sceneItemType = typeof(SceneItem);
            Append("TYPE=" + sceneItemType.FullName);
            foreach (var property in sceneItemType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(p => p.Name is "SceneId" or "ContentLength" or "OriginalContentLength"
                             or "ContentOffset" or "IsLooped" or "PlaybackRate2" or "ContentSeparations"))
                Append($"PROP public {property.PropertyType.FullName} {property.Name}");

            var hits = new List<(MethodInfo Method, List<string> Refs)>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
                         .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true))
            foreach (var type in SafeTypes(asm))
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                var refs = DecodeRefs(method);
                if (refs == null) continue;
                var hasScene = refs.Any(r => r.Contains("SceneItem::get_SceneId", StringComparison.Ordinal)
                    || r.Contains("SceneItem::SceneId", StringComparison.Ordinal));
                var hasTiming = refs.Any(r => r.Contains("PlaybackRateMap", StringComparison.Ordinal)
                    || r.Contains("ContentOffset", StringComparison.Ordinal)
                    || r.Contains("ContentLength", StringComparison.Ordinal)
                    || r.Contains("get_Frame", StringComparison.Ordinal)
                    || r.Contains("get_Length", StringComparison.Ordinal));
                if (hasScene && hasTiming) hits.Add((method, refs));
            }

            foreach (var hit in hits.OrderBy(x => x.Method.DeclaringType?.FullName).ThenBy(x => x.Method.Name))
            {
                Append("HIT " + Sig(hit.Method));
                foreach (var reference in hit.Refs.Distinct()) Append("REF " + reference);
            }

            var update = hits.SingleOrDefault(x =>
                x.Method.DeclaringType?.FullName == "YukkuriMovieMaker.Player.Video.Items.SceneSource"
                && x.Method.Name == "Update");
            if (update.Method == null) throw new MissingMethodException("SceneSource.Update timing path missing");

            bool Has(string text) => update.Refs.Any(r => r.Contains(text, StringComparison.Ordinal));
            var usesPlaybackMap = Has("PlaybackRateMap::GetSourceTime");
            var usesContentOffset = Has("BaseItem::get_ContentOffset");
            var usesContentLength = Has("BaseItem::get_ContentLength");
            var usesLoop = Has("SceneItem::get_IsLooped");
            var usesChildDuration = Has("ISceneInfo::get_Duration");
            var updatesChildTimeline = Has("ITimelineSource::Update");

            var contentLengthGetter = hits.SingleOrDefault(x =>
                x.Method.DeclaringType == typeof(SceneItem) && x.Method.Name == "get_ContentLength");
            var contentLengthUsesGlobalScene = contentLengthGetter.Method != null
                && contentLengthGetter.Refs.Any(r => r.Contains("GlobalSceneInfo::GetValue", StringComparison.Ordinal));

            Assert(usesPlaybackMap, "SceneSource.Update maps parent item time through PlaybackRateMap.GetSourceTime");
            Assert(usesContentOffset && usesContentLength, "SceneSource.Update supplies SceneItem ContentOffset and ContentLength");
            Assert(usesChildDuration, "SceneSource.Update reads child scene duration");
            Assert(updatesChildTimeline, "SceneSource.Update forwards mapped source time into child ITimelineSource.Update");
            Assert(contentLengthUsesGlobalScene, "SceneItem.ContentLength is resolved through GlobalSceneInfo");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_SCENEITEM_TIME_SURFACE",
                "timing_reference_hit_count=" + hits.Count,
                "scene_source_uses_playbackratemap=" + usesPlaybackMap,
                "scene_source_uses_content_offset=" + usesContentOffset,
                "scene_source_uses_content_length=" + usesContentLength,
                "scene_source_uses_islooped=" + usesLoop,
                "scene_source_uses_child_duration=" + usesChildDuration,
                "scene_source_updates_child_timeline=" + updatesChildTimeline,
                "sceneitem_content_length_uses_global_scene=" + contentLengthUsesGlobalScene,
                "candidate_methods=" + string.Join(";", hits.Select(x => Sig(x.Method)))
            ], new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            File.WriteAllLines(Path.Combine(output, "result.txt"),
                ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message], new UTF8Encoding(false));
        }
    }

    private static List<string>? DecodeRefs(MethodInfo method)
    {
        byte[]? bytes;
        try { bytes = method.GetMethodBody()?.GetILAsByteArray(); } catch { return null; }
        if (bytes == null) return null;
        var refs = new List<string>();
        var i = 0;
        while (i < bytes.Length)
        {
            ushort code = bytes[i++];
            if (code == 0xFE)
            {
                if (i >= bytes.Length) break;
                code = (ushort)(0xFE00 | bytes[i++]);
            }
            if (!Ops.TryGetValue(code, out var op)) break;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                case OperandType.ShortInlineBrTarget: i += 1; break;
                case OperandType.InlineVar: i += 2; break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineR: i += 4; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: i += 8; break;
                case OperandType.InlineSwitch:
                    var n = BitConverter.ToInt32(bytes, i); i += 4 + 4 * n; break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.InlineString:
                case OperandType.InlineSig:
                    var token = BitConverter.ToInt32(bytes, i); i += 4;
                    if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
                        refs.Add(Resolve(method, token));
                    break;
            }
        }
        return refs;
    }

    private static string Resolve(MethodInfo method, int token)
    {
        try
        {
            var member = method.Module.ResolveMember(token,
                method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : Type.EmptyTypes,
                method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes);
            return member == null ? $"token:{token:X8}" : (member.DeclaringType?.FullName ?? member.Module.Name) + "::" + member.Name + " " + member;
        }
        catch { return $"token:{token:X8}"; }
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static string Sig(MethodInfo method) =>
        (method.DeclaringType?.FullName ?? "<global>") + "::" + method.Name + "(" +
        string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name)) + ")";

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("ASSERT FAIL: " + name);
        Append("ASSERT PASS: " + name);
    }

    private static void Append(string line) =>
        File.AppendAllText(Path.Combine(output, "sceneitem-time.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
