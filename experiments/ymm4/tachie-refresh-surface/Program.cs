using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Ymm4TachieRefreshSurface <YMM4_DIR> <OUTPUT_DIR>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var opcodeMap = BuildOpcodeMap();
var records = new List<MethodRecord>();
var publicCandidates = new List<PublicCandidate>();

foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
{
    try
    {
        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) continue;
        var md = pe.GetMetadataReader();
        var assemblyName = md.IsAssembly ? md.GetString(md.GetAssemblyDefinition().Name) : Path.GetFileNameWithoutExtension(file);
        var owners = BuildOwners(md);

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            var ns = md.GetString(td.Namespace);
            var name = md.GetString(td.Name);
            var owner = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                var methodName = md.GetString(m.Name);
                if ((m.Attributes & MethodAttributes.Public) != 0 &&
                    ContainsAny(owner + "::" + methodName, "invalidate", "refresh", "redraw", "preview", "request", "render", "frame"))
                    publicCandidates.Add(new(assemblyName, owner, methodName));
            }
        }

        foreach (var mh in md.MethodDefinitions)
        {
            var m = md.GetMethodDefinition(mh);
            if (m.RelativeVirtualAddress == 0) continue;
            var owner = owners.TryGetValue(mh, out var o) ? o : "<unknown>";
            var methodName = md.GetString(m.Name);
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(m.RelativeVirtualAddress); } catch { continue; }
            var il = body.GetILBytes();
            if (il is null || il.Length == 0) continue;
            var lines = DecodeIl(il, md, owners, opcodeMap).ToArray();
            var joined = string.Join("\n", lines);
            var context = owner + "::" + methodName + "\n" + joined;
            var hasPropertyChanged = ContainsAny(context, "propertychanged", "add_propertychanged", "propertychangedeventmanager");
            var hasRefreshSignal = ContainsAny(context, "invalidate", "refresh", "redraw", "preview", "request", "render", "updateframe", "framechanged", "raise");
            var tachieContext = ContainsAny(context, "tachie", "timeline", "player.video", "timelineitem", "videosource");
            var handlerName = methodName.Contains("PropertyChanged", StringComparison.OrdinalIgnoreCase);
            if ((hasPropertyChanged && tachieContext) || (handlerName && tachieContext) || (hasRefreshSignal && tachieContext && hasPropertyChanged))
                records.Add(new(Path.GetRelativePath(root, file), assemblyName, owner, methodName, lines));
        }
    }
    catch (BadImageFormatException) { }
    catch (IOException) { }
}

records = records.DistinctBy(r => (r.Assembly, r.Owner, r.Method))
    .OrderBy(r => r.Assembly).ThenBy(r => r.Owner).ThenBy(r => r.Method).ToList();
publicCandidates = publicCandidates.Distinct().OrderBy(x => x.Assembly).ThenBy(x => x.Owner).ThenBy(x => x.Method).ToList();

var trace = new StringBuilder();
foreach (var r in records)
{
    trace.AppendLine();
    trace.AppendLine($"## {r.Assembly} :: {r.Owner}::{r.Method}");
    foreach (var line in r.Il) trace.AppendLine(line);
}
File.WriteAllText(Path.Combine(output, "refresh-trace.txt"), trace.ToString(), new UTF8Encoding(false));

var summary = new StringBuilder();
summary.AppendLine($"Candidate tachie/timeline PropertyChanged methods: {records.Count}");
foreach (var g in records.GroupBy(x => x.Assembly))
    summary.AppendLine($"  {g.Key}: {g.Count()}");
summary.AppendLine();
summary.AppendLine($"Public refresh/render-looking methods: {publicCandidates.Count}");
foreach (var c in publicCandidates.Take(300))
    summary.AppendLine($"  {c.Assembly} :: {c.Owner}::{c.Method}");
File.WriteAllText(Path.Combine(output, "summary.txt"), summary.ToString(), new UTF8Encoding(false));
File.WriteAllText(Path.Combine(output, "surface.json"), JsonSerializer.Serialize(new
{
    scannedAtUtc = DateTimeOffset.UtcNow,
    root,
    propertyChangedCandidates = records.Select(r => new { r.Path, r.Assembly, r.Owner, r.Method }).ToArray(),
    publicCandidates
}, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
Console.Write(summary.ToString());
return records.Count == 0 ? 3 : 0;

static Dictionary<MethodDefinitionHandle, string> BuildOwners(MetadataReader md)
{
    var result = new Dictionary<MethodDefinitionHandle, string>();
    foreach (var th in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(th);
        var ns = md.GetString(td.Namespace); var name = md.GetString(td.Name);
        var owner = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        foreach (var mh in td.GetMethods()) result[mh] = owner;
    }
    return result;
}
static bool ContainsAny(string text, params string[] needles)
    => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
static Dictionary<ushort, OpCode> BuildOpcodeMap()
{
    var map = new Dictionary<ushort, OpCode>();
    foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        if (f.GetValue(null) is OpCode op) map[unchecked((ushort)op.Value)] = op;
    return map;
}
static IEnumerable<string> DecodeIl(byte[] il, MetadataReader md, Dictionary<MethodDefinitionHandle,string> owners, Dictionary<ushort,OpCode> map)
{
    var p = 0;
    while (p < il.Length)
    {
        var start = p; ushort key = il[p++];
        if (key == 0xFE) { if (p >= il.Length) yield break; key = (ushort)(0xFE00 | il[p++]); }
        if (!map.TryGetValue(key, out var op)) { yield return $"{start:X4}: <unknown 0x{key:X4}>"; yield break; }
        string operand = "";
        switch (op.OperandType)
        {
            case OperandType.InlineNone: break;
            case OperandType.ShortInlineI: operand = unchecked((sbyte)il[p]).ToString(); p++; break;
            case OperandType.InlineI: operand = ReadI4(il,p).ToString(); p+=4; break;
            case OperandType.InlineI8: operand = BitConverter.ToInt64(il,p).ToString(); p+=8; break;
            case OperandType.ShortInlineR: operand = BitConverter.ToSingle(il,p).ToString(System.Globalization.CultureInfo.InvariantCulture); p+=4; break;
            case OperandType.InlineR: operand = BitConverter.ToDouble(il,p).ToString(System.Globalization.CultureInfo.InvariantCulture); p+=8; break;
            case OperandType.ShortInlineVar: operand = il[p].ToString(); p++; break;
            case OperandType.InlineVar: operand = BitConverter.ToUInt16(il,p).ToString(); p+=2; break;
            case OperandType.ShortInlineBrTarget: { var d=unchecked((sbyte)il[p]); p++; operand=$"IL_{p+d:X4}"; break; }
            case OperandType.InlineBrTarget: { var d=ReadI4(il,p); p+=4; operand=$"IL_{p+d:X4}"; break; }
            case OperandType.InlineSwitch:
                { var count=ReadI4(il,p); p+=4; var b=p+count*4; var ts=new List<string>(); for(int i=0;i<count;i++){var d=ReadI4(il,p);p+=4;ts.Add($"IL_{b+d:X4}");} operand=string.Join(", ",ts); break; }
            case OperandType.InlineString:
                { var token=ReadI4(il,p); p+=4; try{operand="\""+md.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF)).Replace("\"","\\\"")+"\"";}catch{operand=$"0x{token:X8}";} break; }
            case OperandType.InlineMethod:
            case OperandType.InlineField:
            case OperandType.InlineType:
            case OperandType.InlineTok:
            case OperandType.InlineSig:
                { var token=ReadI4(il,p); p+=4; operand=ResolveToken(md,owners,token); break; }
            default: yield return $"{start:X4}: {op.Name} <{op.OperandType}>"; yield break;
        }
        yield return string.IsNullOrEmpty(operand) ? $"{start:X4}: {op.Name}" : $"{start:X4}: {op.Name} {operand}";
    }
}
static int ReadI4(byte[] d,int o)=>d[o]|(d[o+1]<<8)|(d[o+2]<<16)|(d[o+3]<<24);
static string ResolveToken(MetadataReader md, Dictionary<MethodDefinitionHandle,string> owners, int token)
{
    try
    {
        var h=MetadataTokens.EntityHandle(token);
        return h.Kind switch
        {
            HandleKind.MethodDefinition => ResolveMethod(md,owners,(MethodDefinitionHandle)h),
            HandleKind.MemberReference => ResolveMember(md,(MemberReferenceHandle)h),
            HandleKind.TypeReference => ResolveTypeRef(md,(TypeReferenceHandle)h),
            HandleKind.TypeDefinition => ResolveTypeDef(md,(TypeDefinitionHandle)h),
            HandleKind.FieldDefinition => md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)h).Name),
            HandleKind.MethodSpecification => "MethodSpec:"+ResolveToken(md,owners,MetadataTokens.GetToken(md.GetMethodSpecification((MethodSpecificationHandle)h).Method)),
            _ => $"0x{token:X8}:{h.Kind}"
        };
    } catch { return $"0x{token:X8}"; }
}
static string ResolveMethod(MetadataReader md, Dictionary<MethodDefinitionHandle,string> owners, MethodDefinitionHandle h)
{ var m=md.GetMethodDefinition(h); return (owners.TryGetValue(h,out var o)?o:"<unknown>")+"::"+md.GetString(m.Name); }
static string ResolveMember(MetadataReader md, MemberReferenceHandle h)
{ var m=md.GetMemberReference(h); return ResolveParent(md,m.Parent)+"::"+md.GetString(m.Name); }
static string ResolveParent(MetadataReader md, EntityHandle h)=>h.Kind switch
{ HandleKind.TypeReference=>ResolveTypeRef(md,(TypeReferenceHandle)h), HandleKind.TypeDefinition=>ResolveTypeDef(md,(TypeDefinitionHandle)h), _=>h.Kind.ToString() };
static string ResolveTypeRef(MetadataReader md, TypeReferenceHandle h)
{ var t=md.GetTypeReference(h);var ns=md.GetString(t.Namespace);var n=md.GetString(t.Name);return string.IsNullOrEmpty(ns)?n:ns+"."+n; }
static string ResolveTypeDef(MetadataReader md, TypeDefinitionHandle h)
{ var t=md.GetTypeDefinition(h);var ns=md.GetString(t.Namespace);var n=md.GetString(t.Name);return string.IsNullOrEmpty(ns)?n:ns+"."+n; }
sealed record MethodRecord(string Path,string Assembly,string Owner,string Method,string[] Il);
sealed record PublicCandidate(string Assembly,string Owner,string Method);