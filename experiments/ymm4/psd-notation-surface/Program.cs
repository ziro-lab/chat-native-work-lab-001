using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
        var hasPsdType = false;

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
            if (il.IsDefaultOrEmpty)
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

        if (psdRelated || literalHits.Count > 0)
        {
            assemblyResults.Add(new AssemblyResult(
                Path.GetRelativePath(root, file),
                assemblyName,
                references.Where(x => x.Contains("psd", StringComparison.OrdinalIgnoreCase)).Distinct().Order().ToArray(),
                interestingTypes.Distinct().Order().Take(500).ToArray(),
                literalHits.Distinct().OrderBy(x => x.Type).ThenBy(x => x.Method).ThenBy(x => x.Value).ToArray(),
                charConstantHits.Distinct().OrderBy(x => x.Type).ThenBy(x => x.Method).ThenBy(x => x.Value).Take(1000).ToArray(),
                rawHits.Distinct().OrderBy(x => x.Token).ThenBy(x => x.Encoding).ToArray()));
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
}

var summaryPath = Path.Combine(output, "summary.txt");
File.WriteAllText(summaryPath, summary.ToString(), new UTF8Encoding(false));
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

sealed record ProbeResult(string Root, DateTimeOffset ScannedAtUtc, string[] Tokens, AssemblyResult[] Assemblies);
sealed record AssemblyResult(
    string Path,
    string AssemblyName,
    string[] PsdReferences,
    string[] InterestingTypes,
    LiteralHit[] LiteralHits,
    CharConstantHit[] CharConstantHits,
    RawHit[] RawHits);
sealed record LiteralHit(string Type, string Method, string Value);
sealed record CharConstantHit(string Type, string Method, int Value, string Opcode);
sealed record RawHit(string Token, string Encoding);
