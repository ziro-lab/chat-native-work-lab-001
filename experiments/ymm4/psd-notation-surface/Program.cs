using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Ymm4PsdNotationProbe <YMM4_DIR> <OUTPUT_DIR>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);

var notationTokens = new[] { "*", "!", ":flip", ":flipx", ":flipy", ":flipxy", "PSDTool", "%2f", "%25" };
var assemblyResults = new List<AssemblyResult>();
var targetIlDump = new StringBuilder();
var opcodeMap = BuildOpcodeMap();

foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
{
    try
    {
        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            continue;

        var md = pe.GetMetadataReader();
        var assemblyName = md.IsAssembly
            ? md.GetString(md.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(file);

        var references = md.AssemblyReferences
            .Select(h => md.GetString(md.GetAssemblyReference(h).Name))
            .ToArray();

        var methodOwners = new Dictionary<MethodDefinitionHandle, string>();
        var interestingTypes = new List<string>();
        var typeSurfaces = new List<TypeSurface>();
        var hasPsdType = false;
        var capturePsdSurface =
            assemblyName.Equals("YukkuriMovieMaker.Plugin.FileSource.Psd", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals("YukkuriMovieMaker.Plugin.Tachie.Psd", StringComparison.OrdinalIgnoreCase);

        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            var ns = md.GetString(type.Namespace);
            var name = md.GetString(type.Name);
            var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            if (ContainsAny(fullName, "psd", "tachie", "layer", "part"))
                interestingTypes.Add(fullName);
            if (fullName.Contains("psd", StringComparison.OrdinalIgnoreCase))
                hasPsdType = true;

            if (capturePsdSurface)
            {
                var methods = type.GetMethods().Select(h => md.GetString(md.GetMethodDefinition(h).Name)).Distinct().Order().ToArray();
                var properties = type.GetProperties().Select(h => md.GetString(md.GetPropertyDefinition(h).Name)).Distinct().Order().ToArray();
                var fields = type.GetFields().Select(h => md.GetString(md.GetFieldDefinition(h).Name)).Distinct().Order().ToArray();
                typeSurfaces.Add(new TypeSurface(fullName, methods, properties, fields));
            }

            foreach (var methodHandle in type.GetMethods())
                methodOwners[methodHandle] = fullName;
        }

        var psdRelated =
            Path.GetFileName(file).Contains("psd", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Contains("psd", StringComparison.OrdinalIgnoreCase) ||
            references.Any(x => x.Contains("psd", StringComparison.OrdinalIgnoreCase)) ||
            hasPsdType;

        var literalHits = new List<LiteralHit>();
        var charConstantHits = new List<CharConstantHit>();

        foreach (var methodHandle in md.MethodDefinitions)
        {
            var method = md.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var methodName = md.GetString(method.Name);
            var owner = methodOwners.TryGetValue(methodHandle, out var ownerName) ? ownerName : "<unknown>";
            var contextLooksRelevant = psdRelated || ContainsAny(owner, "psd", "tachie", "layer") || ContainsAny(methodName, "psd", "layer", "part");

            MethodBodyBlock body;
            try
            {
                body = pe.GetMethodBody(method.RelativeVirtualAddress);
            }
            catch
            {
                continue;
            }

            var il = body.GetILBytes();
            if (il is null || il.Length == 0)
                continue;

            for (var i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x72) // ldstr
                    continue;

                var token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                if ((token & unchecked((int)0xFF000000)) != 0x70000000)
                    continue;

                try
                {
                    var value = md.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF));
                    if (notationTokens.Any(t => string.Equals(value, t, StringComparison.OrdinalIgnoreCase) ||
                                                value.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        literalHits.Add(new LiteralHit(owner, methodName, value));
                    }
                }
                catch
                {
                    // Discovery probe: malformed candidate bytes are ignored.
                }
            }

            if (contextLooksRelevant)
            {
                for (var i = 0; i + 1 < il.Length; i++)
                {
                    if (il[i] == 0x1F) // ldc.i4.s
                    {
                        var value = unchecked((sbyte)il[i + 1]);
                        if (value is 33 or 42)
                            charConstantHits.Add(new CharConstantHit(owner, methodName, value, "ldc.i4.s"));
                    }
                    else if (i + 4 < il.Length && il[i] == 0x20) // ldc.i4
                    {
                        var value = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                        if (value is 33 or 42)
                            charConstantHits.Add(new CharConstantHit(owner, methodName, value, "ldc.i4"));
                    }
                }
            }
        }

        var rawHits = new List<RawHit>();
        if (psdRelated)
        {
            var bytes = File.ReadAllBytes(file);
            foreach (var token in notationTokens.Where(x => x.Length >= 2))
            {
                if (IndexOf(bytes, Encoding.UTF8.GetBytes(token)) >= 0)
                    rawHits.Add(new RawHit(token, "utf8/ascii"));
                if (IndexOf(bytes, Encoding.Unicode.GetBytes(token)) >= 0)
                    rawHits.Add(new RawHit(token, "utf16le"));
            }
        }

        if (capturePsdSurface)
        {
            DumpTargetIl(pe, md, methodOwners, assemblyName, opcodeMap, targetIlDump);
        }

        if (psdRelated || literalHits.Count > 0)
        {
            assemblyResults.Add(new AssemblyResult(
                Path.GetRelativePath(root, file),
                assemblyName,
                references.Where(x => x.Contains("psd", StringComparison.OrdinalIgnoreCase)).Distinct().Order().ToArray(),
                interestingTypes.Distinct().Order().Take(500).ToArray(),
                literalHits.Distinct().OrderBy(x => x.Type).ThenBy(x => x.Method).ThenBy(x => x.Value).ToArray(),
                charConstantHits.Distinct().OrderBy(x => x.Type).ThenBy(x => x.Method).ThenBy(x => x.Value).Take(1000).ToArray(),
                rawHits.Distinct().OrderBy(x => x.Token).ThenBy(x => x.Encoding).ToArray(),
                typeSurfaces.OrderBy(x => x.Type).ToArray()));
        }
    }
    catch (BadImageFormatException)
    {
    }
    catch (IOException)
    {
    }
}

var result = new ProbeResult(
    root,
    DateTimeOffset.UtcNow,
    notationTokens,
    assemblyResults.OrderBy(x => x.AssemblyName).ThenBy(x => x.Path).ToArray());

var jsonPath = Path.Combine(output, "static-scan.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

var summary = new StringBuilder();
summary.AppendLine($"Managed PSD-related assemblies or notation-hit assemblies: {result.Assemblies.Length}");
foreach (var a in result.Assemblies)
{
    summary.AppendLine();
    summary.AppendLine($"[{a.AssemblyName}] {a.Path}");
    if (a.PsdReferences.Length > 0)
        summary.AppendLine($"  PSD refs: {string.Join(", ", a.PsdReferences)}");
    foreach (var hit in a.LiteralHits)
        summary.AppendLine($"  ldstr: {hit.Type}::{hit.Method} => {Escape(hit.Value)}");
    foreach (var hit in a.RawHits)
        summary.AppendLine($"  raw: {hit.Token} ({hit.Encoding})");
    var bang = a.CharConstantHits.Count(x => x.Value == 33);
    var star = a.CharConstantHits.Count(x => x.Value == 42);
    if (bang > 0 || star > 0)
        summary.AppendLine($"  relevant-method integer constants: !={bang}, *={star} (discovery only)");

    foreach (var t in a.TypeSurfaces)
    {
        var members = t.Methods.Concat(t.Properties).Concat(t.Fields)
            .Where(x => ContainsAny(x, "radio", "force", "flip", "select", "check", "visible", "visibility", "active", "switch", "layer"))
            .Distinct().Order().ToArray();
        if (members.Length > 0)
            summary.AppendLine($"  surface: {t.Type} => {string.Join(", ", members)}");
    }
}

var summaryPath = Path.Combine(output, "summary.txt");
File.WriteAllText(summaryPath, summary.ToString(), new UTF8Encoding(false));
File.WriteAllText(Path.Combine(output, "target-il.txt"), targetIlDump.ToString(), new UTF8Encoding(false));
Console.Write(summary.ToString());

if (result.Assemblies.Length == 0)
{
    Console.Error.WriteLine("No PSD-related managed assembly or notation hit was found.");
    return 3;
}

return 0;

static bool ContainsAny(string value, params string[] needles)
    => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

static string Escape(string s)
    => s.Replace("\r", "\\r").Replace("\n", "\\n");

static int IndexOf(byte[] haystack, byte[] needle)
{
    if (needle.Length == 0)
        return 0;
    for (var i = 0; i <= haystack.Length - needle.Length; i++)
    {
        var match = true;
        for (var j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j])
            {
                match = false;
                break;
            }
        }
        if (match)
            return i;
    }
    return -1;
}


static Dictionary<ushort, OpCode> BuildOpcodeMap()
{
    var map = new Dictionary<ushort, OpCode>();
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (field.GetValue(null) is OpCode op)
            map[unchecked((ushort)op.Value)] = op;
    }
    return map;
}

static void DumpTargetIl(
    PEReader pe,
    MetadataReader md,
    Dictionary<MethodDefinitionHandle, string> methodOwners,
    string assemblyName,
    Dictionary<ushort, OpCode> opcodeMap,
    StringBuilder output)
{
    foreach (var methodHandle in md.MethodDefinitions)
    {
        var method = md.GetMethodDefinition(methodHandle);
        if (method.RelativeVirtualAddress == 0)
            continue;

        var methodName = md.GetString(method.Name);
        var owner = methodOwners.TryGetValue(methodHandle, out var ownerName) ? ownerName : "<unknown>";

        var target =
            (owner.EndsWith(".PsdFolder", StringComparison.Ordinal) &&
                methodName is "Parse" or "SetEnableItems" or "GetEnableItems" or "ResolveItem" or "SetActiveLayers") ||
            (owner.EndsWith(".SwitchLayerCommand", StringComparison.Ordinal) && methodName == "Execute") ||
            (owner.EndsWith(".ShiftLayerCommand", StringComparison.Ordinal) && methodName == "Execute") ||
            (owner.Contains("PsdItemViewModel", StringComparison.Ordinal) &&
                methodName is ".ctor" or "set_IsEnabled" or "UpdateEnable") ||
            (owner.EndsWith(".PsdFolderViewModel", StringComparison.Ordinal) && methodName == "UpdateEnable") ||
            (owner.EndsWith(".PsdLayerEditorViewModel", StringComparison.Ordinal) &&
                methodName is "UpdateViewModels" or "Resolve") ||
            (owner.EndsWith(".PsdTachieSource", StringComparison.Ordinal) && methodName == "Update");

        if (!target)
            continue;

        MethodBodyBlock body;
        try
        {
            body = pe.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch
        {
            continue;
        }

        var il = body.GetILBytes();
        if (il is null || il.Length == 0)
            continue;

        output.AppendLine();
        output.AppendLine($"## {assemblyName} :: {owner}::{methodName}");
        foreach (var line in DecodeIl(il, md, methodOwners, opcodeMap))
            output.AppendLine(line);
    }
}

static IEnumerable<string> DecodeIl(
    byte[] il,
    MetadataReader md,
    Dictionary<MethodDefinitionHandle, string> methodOwners,
    Dictionary<ushort, OpCode> opcodeMap)
{
    var p = 0;
    while (p < il.Length)
    {
        var start = p;
        ushort key = il[p++];
        if (key == 0xFE)
        {
            if (p >= il.Length)
                yield break;
            key = (ushort)(0xFE00 | il[p++]);
        }

        if (!opcodeMap.TryGetValue(key, out var op))
        {
            yield return $"{start:X4}: <unknown 0x{key:X4}>";
            yield break;
        }

        string operand = "";
        switch (op.OperandType)
        {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineI:
                    operand = unchecked((sbyte)il[p]).ToString();
                    p += 1;
                    break;
                case OperandType.InlineI:
                    operand = ReadI4(il, p).ToString();
                    p += 4;
                    break;
                case OperandType.InlineI8:
                    operand = BitConverter.ToInt64(il, p).ToString();
                    p += 8;
                    break;
                case OperandType.ShortInlineR:
                    operand = BitConverter.ToSingle(il, p).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    p += 4;
                    break;
                case OperandType.InlineR:
                    operand = BitConverter.ToDouble(il, p).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    p += 8;
                    break;
                case OperandType.ShortInlineVar:
                    operand = il[p].ToString();
                    p += 1;
                    break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(il, p).ToString();
                    p += 2;
                    break;
                case OperandType.ShortInlineBrTarget:
                    {
                        var delta = unchecked((sbyte)il[p]);
                        p += 1;
                        operand = $"IL_{p + delta:X4}";
                        break;
                    }
                case OperandType.InlineBrTarget:
                    {
                        var delta = ReadI4(il, p);
                        p += 4;
                        operand = $"IL_{p + delta:X4}";
                        break;
                    }
                case OperandType.InlineSwitch:
                    {
                        var count = ReadI4(il, p);
                        p += 4;
                        var baseOffset = p + count * 4;
                        var targets = new List<string>(Math.Max(count, 0));
                        for (var i = 0; i < count; i++)
                        {
                            var delta = ReadI4(il, p);
                            p += 4;
                            targets.Add($"IL_{baseOffset + delta:X4}");
                        }
                        operand = string.Join(", ", targets);
                        break;
                    }
                case OperandType.InlineString:
                    {
                        var token = ReadI4(il, p);
                        p += 4;
                        try
                        {
                            operand = Quote(md.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF)));
                        }
                        catch
                        {
                            operand = $"0x{token:X8}";
                        }
                        break;
                    }
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                    {
                        var token = ReadI4(il, p);
                        p += 4;
                        operand = ResolveToken(md, methodOwners, token);
                        break;
                    }
            default:
                operand = $"<operand {op.OperandType}>";
                yield return $"{start:X4}: {op.Name} {operand}";
                yield break;
        }

        yield return string.IsNullOrEmpty(operand)
            ? $"{start:X4}: {op.Name}"
            : $"{start:X4}: {op.Name} {operand}";
    }
}

static int ReadI4(byte[] data, int offset)
    => data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

static string ResolveToken(MetadataReader md, Dictionary<MethodDefinitionHandle, string> owners, int token)
{
    try
    {
        var handle = MetadataTokens.EntityHandle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => ResolveMethod(md, owners, (MethodDefinitionHandle)handle),
            HandleKind.MemberReference => ResolveMemberRef(md, (MemberReferenceHandle)handle),
            HandleKind.TypeReference => ResolveTypeRef(md, (TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => ResolveTypeDef(md, (TypeDefinitionHandle)handle),
            HandleKind.FieldDefinition => md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
            HandleKind.MethodSpecification => "MethodSpec:" + ResolveToken(md, owners, MetadataTokens.GetToken(md.GetMethodSpecification((MethodSpecificationHandle)handle).Method)),
            _ => $"0x{token:X8}:{handle.Kind}"
        };
    }
    catch
    {
        return $"0x{token:X8}";
    }
}

static string ResolveMethod(MetadataReader md, Dictionary<MethodDefinitionHandle, string> owners, MethodDefinitionHandle h)
{
    var m = md.GetMethodDefinition(h);
    var owner = owners.TryGetValue(h, out var o) ? o : "<unknown>";
    return owner + "::" + md.GetString(m.Name);
}

static string ResolveMemberRef(MetadataReader md, MemberReferenceHandle h)
{
    var mr = md.GetMemberReference(h);
    return ResolveParent(md, mr.Parent) + "::" + md.GetString(mr.Name);
}

static string ResolveParent(MetadataReader md, EntityHandle h)
    => h.Kind switch
    {
        HandleKind.TypeReference => ResolveTypeRef(md, (TypeReferenceHandle)h),
        HandleKind.TypeDefinition => ResolveTypeDef(md, (TypeDefinitionHandle)h),
        _ => h.Kind.ToString()
    };

static string ResolveTypeRef(MetadataReader md, TypeReferenceHandle h)
{
    var t = md.GetTypeReference(h);
    var ns = md.GetString(t.Namespace);
    var name = md.GetString(t.Name);
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

static string ResolveTypeDef(MetadataReader md, TypeDefinitionHandle h)
{
    var t = md.GetTypeDefinition(h);
    var ns = md.GetString(t.Namespace);
    var name = md.GetString(t.Name);
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

sealed record ProbeResult(string Root, DateTimeOffset ScannedAtUtc, string[] Tokens, AssemblyResult[] Assemblies);
sealed record AssemblyResult(
    string Path,
    string AssemblyName,
    string[] PsdReferences,
    string[] InterestingTypes,
    LiteralHit[] LiteralHits,
    CharConstantHit[] CharConstantHits,
    RawHit[] RawHits,
    TypeSurface[] TypeSurfaces);
sealed record TypeSurface(string Type, string[] Methods, string[] Properties, string[] Fields);
sealed record LiteralHit(string Type, string Method, string Value);
sealed record CharConstantHit(string Type, string Method, int Value, string Opcode);
sealed record RawHit(string Token, string Encoding);
