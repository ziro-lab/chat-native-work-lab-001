using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceItemEditServiceRouteProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Edit Service Route Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];
    static readonly Dictionary<ushort, OpCode> opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(x => unchecked((ushort)x.Value));

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEITEM_EDIT_ROUTE_OUTPUT");
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
            var iface = typeof(IVoiceItemEditService);
            Check("interface_public", iface.IsPublic || iface.IsNestedPublic);

            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
                .SelectMany(SafeTypes)
                .Distinct()
                .ToArray();

            var impls = allTypes
                .Where(t => !t.IsAbstract && !t.IsInterface && iface.IsAssignableFrom(t))
                .OrderBy(t => t.FullName)
                .ToArray();

            Check("implementation_found", impls.Length > 0);

            object Describe(Type t)
            {
                var map = t.GetInterfaceMap(iface);
                var createIndex = Array.FindIndex(map.InterfaceMethods,
                    m => m.Name == "CreateVoiceFileAsync");
                MethodInfo? impl = createIndex >= 0 ? map.TargetMethods[createIndex] : null;
                var asyncAttr = impl?.GetCustomAttribute<AsyncStateMachineAttribute>();
                var moveNext = asyncAttr?.StateMachineType.GetMethod("MoveNext",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                return new
                {
                    type = t.FullName,
                    isPublic = t.IsPublic || t.IsNestedPublic,
                    constructors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(c => new {
                            visibility = c.IsPublic ? "public" : c.IsFamily ? "protected" : c.IsAssembly ? "internal" : "nonpublic",
                            signature = c.ToString()
                        }).ToArray(),
                    fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(f => Relevant(f.Name) || Relevant(f.FieldType.Name))
                        .Select(f => new {
                            f.Name,
                            type = f.FieldType.FullName,
                            visibility = f.IsPublic ? "public" : f.IsFamily ? "protected" : f.IsAssembly ? "internal" : "nonpublic"
                        }).ToArray(),
                    properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(p => Relevant(p.Name) || Relevant(p.PropertyType.Name))
                        .Select(p => new {
                            p.Name,
                            type = p.PropertyType.FullName,
                            publicGet = p.GetMethod?.IsPublic == true,
                            publicSet = p.SetMethod?.IsPublic == true
                        }).ToArray(),
                    createVoiceFile = impl?.ToString(),
                    createVoiceFileVisibility = impl is null ? null : impl.IsPublic ? "public" : impl.IsAssembly ? "internal" : impl.IsPrivate ? "private" : "nonpublic",
                    stateMachine = asyncAttr?.StateMachineType.FullName,
                    createIL = impl is null ? [] : Decode(impl),
                    moveNextIL = moveNext is null ? [] : Decode(moveNext)
                };
            }

            var editorInfoImpls = allTypes
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IEditorInfo).IsAssignableFrom(t))
                .Select(t => new {
                    type = t.FullName,
                    isPublic = t.IsPublic || t.IsNestedPublic,
                    constructors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(c => new {
                            visibility = c.IsPublic ? "public" : c.IsAssembly ? "internal" : c.IsPrivate ? "private" : "nonpublic",
                            signature = c.ToString()
                        }).ToArray(),
                    voiceItemEditProperty = t.GetProperty("VoiceItemEdit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.ToString()
                }).ToArray();

            File.WriteAllText(Path.Combine(output, "surface.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    interfaceType = iface.FullName,
                    interfaceMethods = iface.GetMethods().Select(m => m.ToString()).ToArray(),
                    interfaceProperties = iface.GetProperties().Select(p => new {
                        p.Name, type = p.PropertyType.FullName,
                        publicGet = p.GetMethod?.IsPublic == true,
                        publicSet = p.SetMethod?.IsPublic == true
                    }).ToArray(),
                    implementations = impls.Select(Describe).ToArray(),
                    editorInfoImplementations = editorInfoImpls
                }, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_VOICEITEM_EDIT_SERVICE_ROUTE_INVENTORY", null);
        }
        catch(Exception ex)
        {
            Write("FAIL_VOICEITEM_EDIT_SERVICE_ROUTE_INVENTORY", ex.ToString());
        }
    }

    static bool Relevant(string s) =>
        s.Contains("voice", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("item", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("cache", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("speaker", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("pronounce", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("hatsuon", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("file", StringComparison.OrdinalIgnoreCase);

    sealed record Ins(int Offset,string OpCode,string? Resolved);

    static List<Ins> Decode(MethodBase method)
    {
        var bytes=method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var result=new List<Ins>();
        int i=0;
        while(i<bytes.Length)
        {
            int off=i;
            ushort raw=bytes[i++];
            if(raw==0xFE) raw=(ushort)(0xFE00|bytes[i++]);
            if(!opcodes.TryGetValue(raw,out var op)) throw new InvalidOperationException($"Unknown opcode {raw:X4}");
            string? resolved=null;
            int token;
            switch(op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: i+=1; break;
                case OperandType.InlineVar: i+=2; break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineR: i+=4; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: i+=8; break;
                case OperandType.InlineSwitch:
                    int count=BitConverter.ToInt32(bytes,i); i+=4+count*4; break;
                case OperandType.InlineString:
                    token=BitConverter.ToInt32(bytes,i); i+=4;
                    try { resolved="string: "+method.Module.ResolveString(token); } catch { }
                    break;
                case OperandType.InlineMethod:
                    token=BitConverter.ToInt32(bytes,i); i+=4;
                    try {
                        var m=method.Module.ResolveMethod(token,method.DeclaringType?.GetGenericArguments(),method.IsGenericMethod?method.GetGenericArguments():null);
                        resolved="method: "+m?.DeclaringType?.FullName+"."+m?.Name+"("+string.Join(",",m?.GetParameters().Select(p=>p.ParameterType.FullName)??[])+")";
                    } catch { resolved=$"method-token:{token:X8}"; }
                    break;
                case OperandType.InlineField:
                    token=BitConverter.ToInt32(bytes,i); i+=4;
                    try {
                        var f=method.Module.ResolveField(token,method.DeclaringType?.GetGenericArguments(),method.IsGenericMethod?method.GetGenericArguments():null);
                        resolved="field: "+f?.DeclaringType?.FullName+"."+f?.Name;
                    } catch { }
                    break;
                case OperandType.InlineType:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                    token=BitConverter.ToInt32(bytes,i); i+=4;
                    try {
                        var m=method.Module.ResolveMember(token,method.DeclaringType?.GetGenericArguments(),method.IsGenericMethod?method.GetGenericArguments():null);
                        resolved="member: "+m?.DeclaringType?.FullName+"."+m?.Name;
                    } catch { }
                    break;
                default: throw new NotSupportedException(op.OperandType.ToString());
            }
            result.Add(new(off,op.Name??raw.ToString("X4"),resolved));
        }
        return result;
    }

    static Type[] SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch(ReflectionTypeLoadException ex) { return ex.Types.Where(t=>t is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }

    static void Check(string id,bool passed)=>requirements.Add(new{id,passed});

    static void Write(string status,string? error)
    {
        File.WriteAllText(Path.Combine(output,"result.json"),
            JsonSerializer.Serialize(new{
                schema="cnwl.voiceitem-edit-service-route.v1",
                status,host="4.56.1.0 Lite",
                sourceHead=Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,error
            },new JsonSerializerOptions{WriteIndented=true}));
    }
}
