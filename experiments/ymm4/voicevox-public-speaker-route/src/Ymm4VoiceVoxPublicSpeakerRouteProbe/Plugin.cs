using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceVoxPublicSpeakerRouteProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VOICEVOX Public Speaker Route Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => unchecked((ushort)op.Value));

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEVOX_PUBLIC_ROUTE_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    static void Run()
    {
        try
        {
            var asm = typeof(VoiceItem).Assembly;
            var speakerType = asm.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker");
            Check("speaker_type_found", speakerType is not null);
            if (speakerType is null) throw new InvalidOperationException("VOICEVOXVoiceSpeaker not found.");

            var create = speakerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "CreateVoiceAsync" && m.GetParameters().Length == 4);
            Check("create_voice_async_found", create is not null);
            if (create is null) throw new MissingMethodException(speakerType.FullName, "CreateVoiceAsync");

            var asyncAttr = create.GetCustomAttribute<AsyncStateMachineAttribute>();
            Check("async_state_machine_found", asyncAttr?.StateMachineType is not null);
            if (asyncAttr?.StateMachineType is null)
                throw new InvalidOperationException("AsyncStateMachineAttribute not found.");

            var moveNext = asyncAttr.StateMachineType.GetMethod("MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Check("move_next_found", moveNext?.GetMethodBody() is not null);
            if (moveNext?.GetMethodBody() is null)
                throw new InvalidOperationException("MoveNext body not found.");

            var createIl = Decode(create);
            var moveNextIl = Decode(moveNext);

            var speakerIdGetter = speakerType.GetProperty("ID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod;
            var settingsType = asm.GetType("YukkuriMovieMaker.Settings.VOICEVOXSettings");
            var findEngine = settingsType?.GetMethod("FindEngine",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string)],
                modifiers: null);

            var speakerIdIl = speakerIdGetter is null ? [] : Decode(speakerIdGetter);
            var findEngineIl = findEngine is null ? [] : Decode(findEngine);

            var engineType = asm.GetType("YukkuriMovieMaker.Voice.VOICEVOXEngine");
            var speakerInfosProperty = engineType?.GetProperty("SpeakerInfos",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var speakersCacheProperty = engineType?.GetProperty("SpeakersJsonCache",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var engineCreateRefs = moveNextIl
                .Where(x => x.Resolved?.Contains("VOICEVOXEngine.CreateVoiceFileAsync", StringComparison.Ordinal) == true)
                .ToArray();

            var fileRefs = moveNextIl
                .Where(x => x.Resolved?.Contains("System.IO.File", StringComparison.Ordinal) == true
                         || x.Resolved?.Contains("System.IO.Path", StringComparison.Ordinal) == true)
                .ToArray();

            var pronounceRefs = moveNextIl
                .Where(x => x.Resolved?.Contains("IVoicePronounce", StringComparison.Ordinal) == true
                         || x.Resolved?.Contains("VOICEVOXVoicePronounce", StringComparison.Ordinal) == true)
                .ToArray();

            var instanceFields = speakerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => new {
                    f.Name,
                    type = f.FieldType.FullName,
                    f.IsPublic,
                    f.IsInitOnly
                }).ToArray();

            var stateFields = asyncAttr.StateMachineType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => new {
                    f.Name,
                    type = f.FieldType.FullName,
                    f.IsPublic,
                    f.IsInitOnly
                }).ToArray();

            File.WriteAllText(Path.Combine(output, "surface.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    speakerType = speakerType.FullName,
                    createVoiceAsync = create.ToString(),
                    stateMachineType = asyncAttr.StateMachineType.FullName,
                    speakerFields = instanceFields,
                    stateMachineFields = stateFields,
                    speakerIdGetter = speakerIdGetter?.ToString(),
                    speakerIdIL = speakerIdIl,
                    settingsType = settingsType?.FullName,
                    findEngine = findEngine?.ToString(),
                    findEngineIL = findEngineIl,
                    engineSpeakerInfoSurface = new
                    {
                        engineType = engineType?.FullName,
                        speakerInfos = speakerInfosProperty is null ? null : new
                        {
                            type = speakerInfosProperty.PropertyType.FullName,
                            publicGet = speakerInfosProperty.GetMethod?.IsPublic == true,
                            publicSet = speakerInfosProperty.SetMethod?.IsPublic == true
                        },
                        speakersJsonCache = speakersCacheProperty is null ? null : new
                        {
                            type = speakersCacheProperty.PropertyType.FullName,
                            publicGet = speakersCacheProperty.GetMethod?.IsPublic == true,
                            publicSet = speakersCacheProperty.SetMethod?.IsPublic == true
                        }
                    },
                    engineCreateRefs,
                    fileRefs,
                    pronounceRefs,
                    createIL = createIl,
                    moveNextIL = moveNextIl
                }, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_VOICEVOX_PUBLIC_SPEAKER_ROUTE_INVENTORY", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_VOICEVOX_PUBLIC_SPEAKER_ROUTE_INVENTORY", ex.ToString());
        }
    }

    sealed record ILInstruction(int Offset, string OpCode, string? Resolved);

    static List<ILInstruction> Decode(MethodBase method)
    {
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray() ?? [];
        var result = new List<ILInstruction>();
        int i = 0;

        while (i < bytes.Length)
        {
            int offset = i;
            ushort raw = bytes[i++];
            if (raw == 0xFE)
                raw = (ushort)(0xFE00 | bytes[i++]);

            if (!OpCodesByValue.TryGetValue(raw, out var op))
                throw new InvalidOperationException($"Unknown opcode 0x{raw:X4} at {offset}.");

            string? resolved = null;
            int token;

            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineR:
                    i += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    i += 8;
                    break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(bytes, i);
                    i += 4 + (count * 4);
                    break;
                case OperandType.InlineString:
                    token = BitConverter.ToInt32(bytes, i);
                    i += 4;
                    try { resolved = "string: " + method.Module.ResolveString(token); }
                    catch { resolved = $"string-token: 0x{token:X8}"; }
                    break;
                case OperandType.InlineMethod:
                    token = BitConverter.ToInt32(bytes, i);
                    i += 4;
                    try
                    {
                        var m = method.Module.ResolveMethod(token,
                            method.DeclaringType?.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        resolved = "method: " + Describe(m);
                    }
                    catch { resolved = $"method-token: 0x{token:X8}"; }
                    break;
                case OperandType.InlineField:
                    token = BitConverter.ToInt32(bytes, i);
                    i += 4;
                    try
                    {
                        var f = method.Module.ResolveField(token,
                            method.DeclaringType?.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        resolved = "field: " + f?.DeclaringType?.FullName + "." + f?.Name;
                    }
                    catch { resolved = $"field-token: 0x{token:X8}"; }
                    break;
                case OperandType.InlineType:
                    token = BitConverter.ToInt32(bytes, i);
                    i += 4;
                    try { resolved = "type: " + method.Module.ResolveType(token)?.FullName; }
                    catch { resolved = $"type-token: 0x{token:X8}"; }
                    break;
                case OperandType.InlineTok:
                    token = BitConverter.ToInt32(bytes, i);
                    i += 4;
                    try
                    {
                        var member = method.Module.ResolveMember(token,
                            method.DeclaringType?.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        resolved = "token: " + member?.DeclaringType?.FullName + "." + member?.Name;
                    }
                    catch { resolved = $"token: 0x{token:X8}"; }
                    break;
                case OperandType.InlineSig:
                    token = BitConverter.ToInt32(bytes, i);
                    i += 4;
                    resolved = $"sig-token: 0x{token:X8}";
                    break;
                default:
                    throw new NotSupportedException(op.OperandType.ToString());
            }

            result.Add(new ILInstruction(offset, op.Name ?? $"0x{raw:X4}", resolved));
        }

        return result;
    }

    static string Describe(MethodBase? m)
    {
        if (m is null) return "<null>";
        return $"{m.DeclaringType?.FullName}.{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName))})";
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voicevox-public-speaker-route.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
