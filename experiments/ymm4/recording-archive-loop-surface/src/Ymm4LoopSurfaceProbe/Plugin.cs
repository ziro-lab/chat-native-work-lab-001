using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4LoopSurfaceProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Loop Surface";
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
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_LOOP_SURFACE_DIR");
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
            const int fps = 60;
            var video = new VideoItem { Length = 600, ContentOffset = TimeSpan.Zero };
            video.PlaybackRate2.SetFirstValue(100);
            video.PlaybackRate2.SetAnimationParameters(video.Length, fps);

            var mapProp = typeof(VideoItem).GetProperty("PlaybackRateMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException("PlaybackRateMap");
            var map = mapProp.GetValue(video) ?? throw new InvalidOperationException("map null");
            var getSource = map.GetType().GetMethod("GetSourceTime",
                [typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan)])
                ?? throw new MissingMethodException("GetSourceTime");

            double[] MapSample(bool loop)
            {
                video.IsLooped = loop;
                var current = mapProp.GetValue(video) ?? throw new InvalidOperationException("map null");
                var method = current.GetType().GetMethod("GetSourceTime",
                    [typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan)])
                    ?? throw new MissingMethodException("GetSourceTime");
                return new[] { 0d, 1d, 2d, 3d, 4d, 5d, 8d }
                    .Select(t => ((TimeSpan)method.Invoke(current,
                        [TimeSpan.FromSeconds(t), video.Length, fps, TimeSpan.Zero, TimeSpan.FromSeconds(3)])!).TotalSeconds)
                    .ToArray();
            }

            var mapOff = MapSample(false);
            var mapOn = MapSample(true);
            var mapSame = mapOff.Zip(mapOn).All(x => Math.Abs(x.First - x.Second) < 1e-9);
            Append("MAP_LOOP_FALSE=" + Join(mapOff));
            Append("MAP_LOOP_TRUE=" + Join(mapOn));
            Append("PLAYBACK_RATE_MAP_IDENTICAL_WITH_LOOP_TOGGLE=" + mapSame);

            var videoSourceType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes)
                .FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Player.Video.Items.VideoSource")
                ?? throw new TypeLoadException("VideoSource missing");
            var calculate = videoSourceType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .SingleOrDefault(m => m.Name == "CalculateSourceTime" && m.GetParameters().Length == 8)
                ?? throw new MissingMethodException("VideoSource.CalculateSourceTime");
            Assert(calculate.IsStatic, "VideoSource.CalculateSourceTime is static and directly observable");

            double Native(double itemSeconds, bool loop, double contentLength, double sourceDuration)
            {
                object?[] args =
                [
                    map, TimeSpan.FromSeconds(itemSeconds), video.Length, fps, TimeSpan.Zero,
                    TimeSpan.FromSeconds(contentLength), loop, TimeSpan.FromSeconds(sourceDuration)
                ];
                var raw = calculate.Invoke(null, args);
                return raw is TimeSpan value ? value.TotalSeconds : double.NaN;
            }

            var times = new[] { 0d, 1d, 2d, 2.9d, 3d, 3.1d, 4d, 5.9d, 6d, 8.9d };
            var noLoopNative = times.Select(t => Native(t, false, 3, 3)).ToArray();
            var loopOriginal = times.Select(t => Native(t, true, 3, 3)).ToArray();
            var loopShortClip = times.Select(t => Native(t, true, 1, 1)).ToArray();
            var loopLongSource = times.Select(t => Native(t, true, 3, 7)).ToArray();
            var shorteningChanges = loopOriginal.Zip(loopShortClip).Any(x => Math.Abs(x.First - x.Second) > 1e-9);
            var sourceDurationChanges = loopOriginal.Zip(loopLongSource).Any(x => Math.Abs(x.First - x.Second) > 1e-9);

            Append("NATIVE_TIMES=" + Join(times));
            Append("NATIVE_NO_LOOP_CONTENT3_SOURCE3=" + Join(noLoopNative));
            Append("NATIVE_LOOP_CONTENT3_SOURCE3=" + Join(loopOriginal));
            Append("NATIVE_LOOP_CONTENT1_SOURCE1=" + Join(loopShortClip));
            Append("NATIVE_LOOP_CONTENT3_SOURCE7=" + Join(loopLongSource));
            Append("SHORTENING_CHANGES_LOOP_MAPPING=" + shorteningChanges);
            Append("SOURCE_DURATION_CHANGES_LOOP_MAPPING=" + sourceDurationChanges);

            var refs = new List<(MethodInfo Method, List<string> Refs)>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
                         .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true))
            foreach (var type in SafeTypes(asm))
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                var decoded = Decode(method);
                if (decoded == null) continue;
                if (decoded.Any(r => r.Contains("get_IsLooped", StringComparison.Ordinal) || r.Contains("isLooped", StringComparison.OrdinalIgnoreCase)))
                    refs.Add((method, decoded));
            }

            var videoHits = refs.Where(x => x.Method.DeclaringType?.FullName == "YukkuriMovieMaker.Player.Video.Items.VideoSource").ToArray();
            foreach (var hit in videoHits)
            {
                Append("VIDEO_LOOP_HIT=" + Sig(hit.Method));
                foreach (var r in hit.Refs.Distinct()) Append("VIDEO_REF " + r);
            }

            Assert(mapSame, "PlaybackRateMap mapping itself is unchanged by IsLooped toggle");
            Assert(refs.Count > 0, "native YMM4 contains IsLooped references");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_LOOP_SURFACE_OBSERVATION",
                "map_identical_with_loop_toggle=" + mapSame,
                "islooped_il_hit_count=" + refs.Count,
                "video_source_islooped_hit_count=" + videoHits.Length,
                "shortening_changes_loop_mapping=" + shorteningChanges,
                "source_duration_changes_loop_mapping=" + sourceDurationChanges,
                "native_loop_original=" + Join(loopOriginal),
                "native_loop_short_clip=" + Join(loopShortClip)
            ], new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            File.WriteAllLines(Path.Combine(output, "result.txt"),
                ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message], new UTF8Encoding(false));
        }
    }

    private static string Join(IEnumerable<double> values) =>
        string.Join(",", values.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));

    private static List<string>? Decode(MethodInfo method)
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
        string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName)) + ")";

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("ASSERT FAIL: " + name);
        Append("ASSERT PASS: " + name);
    }

    private static void Append(string line) =>
        File.AppendAllText(Path.Combine(output, "loop-surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
