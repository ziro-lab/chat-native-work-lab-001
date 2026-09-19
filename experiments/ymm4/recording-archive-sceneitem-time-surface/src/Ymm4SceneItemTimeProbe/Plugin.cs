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
            foreach (var name in new[] { "SceneId", "ContentLength", "OriginalContentLength", "ContentOffset", "IsLooped", "PlaybackRate2", "ContentSeparations" })
            {
                var property = sceneItemType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (property != null) Append($"PROP public {property.PropertyType.FullName} {property.Name}");
            }

            var sceneSourceType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes)
                .FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Player.Video.Items.SceneSource")
                ?? throw new TypeLoadException("SceneSource missing");
            var sceneUpdate = sceneSourceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Update")
                ?? throw new MissingMethodException("SceneSource.Update missing");
            var updateRefs = DecodeRefs(sceneUpdate) ?? throw new InvalidOperationException("SceneSource.Update has no IL body");

            Append("HIT " + Sig(sceneUpdate));
            foreach (var reference in updateRefs.Distinct()) Append("REF " + reference);

            bool Has(string text) => updateRefs.Any(r => r.Contains(text, StringComparison.Ordinal));
            var usesPlaybackMap = Has("PlaybackRateMap::GetSourceTime");
            var usesContentOffset = Has("BaseItem::get_ContentOffset");
            var usesContentLength = Has("BaseItem::get_ContentLength");
            var usesLoop = Has("SceneItem::get_IsLooped");
            var usesChildDuration = Has("ISceneInfo::get_Duration");
            var updatesChildTimeline = Has("ITimelineSource::Update");

            var contentLengthGetter = sceneItemType.GetProperty("ContentLength", BindingFlags.Instance | BindingFlags.Public)?.GetMethod
                ?? throw new MissingMethodException("SceneItem.ContentLength getter missing");
            var contentLengthRefs = DecodeRefs(contentLengthGetter) ?? [];
            foreach (var reference in contentLengthRefs.Distinct()) Append("CONTENT_LENGTH_REF " + reference);
            var contentLengthUsesGlobalScene = contentLengthRefs.Any(r => r.Contains("GlobalSceneInfo::GetValue", StringComparison.Ordinal));

            Assert(usesPlaybackMap, "SceneSource.Update maps parent item time through PlaybackRateMap.GetSourceTime");
            Assert(usesContentOffset && usesContentLength, "SceneSource.Update supplies SceneItem ContentOffset and ContentLength");
            Assert(usesChildDuration, "SceneSource.Update reads child scene duration");
            Assert(updatesChildTimeline, "SceneSource.Update forwards mapped source time into child ITimelineSource.Update");
            Assert(contentLengthUsesGlobalScene, "SceneItem.ContentLength is resolved through GlobalSceneInfo");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_SCENEITEM_TIME_SURFACE",
                "scene_source_uses_playbackratemap=" + usesPlaybackMap,
                "scene_source_uses_content_offset=" + usesContentOffset,
                "scene_source_uses_content_length=" + usesContentLength,
                "scene_source_uses_islooped=" + usesLoop,
                "scene_source_uses_child_duration=" + usesChildDuration,
                "scene_source_updates_child_timeline=" + updatesChildTimeline,
                "sceneitem_content_length_uses_global_scene=" + contentLengthUsesGlobalScene,
                "scene_source_update=" + Sig(sceneUpdate)
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
                {
                    var count = BitConverter.ToInt32(bytes, i);
                    i += 4 + 4 * count;
                    break;
                }
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.InlineString:
                case OperandType.InlineSig:
                {
                    var token = BitConverter.ToInt32(bytes, i);
                    i += 4;
                    if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
                        refs.Add(Resolve(method, token));
                    break;
                }
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
