using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using YukkuriMovieMaker.Plugin;

namespace Ymm4PsdNotationRuntimeProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    static int executed;

    public string Name => "Chat Native Work Lab — YMM4 PSD Notation Runtime Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        var outputDir = Environment.GetEnvironmentVariable("CNWL_YMM4_PSD_RUNTIME_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDir) || Interlocked.Exchange(ref executed, 1) != 0)
            return;

        Directory.CreateDirectory(outputDir);
        var resultPath = Path.Combine(outputDir, "runtime-result.json");
        var markerPath = Path.Combine(outputDir, "runtime-marker.txt");

        RuntimeResult result;
        try
        {
            result = RunProbe(outputDir, cultureInfo);
        }
        catch (Exception ex)
        {
            result = new RuntimeResult(
                "FAIL",
                cultureInfo.Name,
                null,
                [],
                [],
                [],
                ex.ToString());
        }

        File.WriteAllText(
            resultPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        File.WriteAllText(markerPath, result.Status + Environment.NewLine, new UTF8Encoding(false));
    }

    static RuntimeResult RunProbe(string outputDir, CultureInfo cultureInfo)
    {
        var fixture = Path.Combine(outputDir, "notation-fixture.psd");
        PsdFixtureWriter.Write(fixture);

        var parserAssembly = LoadYmmAssembly("PsdParser");
        var fileSourceAssembly = LoadYmmAssembly("YukkuriMovieMaker.Plugin.FileSource.Psd");
        var tachieAssembly = LoadYmmAssembly("YukkuriMovieMaker.Plugin.Tachie.Psd");

        var psdFileType = RequireType(parserAssembly, "PsdParser.PsdFile");
        using var psd = (IDisposable)(Activator.CreateInstance(psdFileType, fixture)
            ?? throw new InvalidOperationException("Failed to construct PsdParser.PsdFile."));

        var folderType = RequireType(fileSourceAssembly, "YukkuriMovieMaker.Plugin.FileSource.Psd.PsdFolder");
        var parse = folderType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(m =>
                m.Name == "Parse" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsAssignableFrom(psdFileType))
            ?? throw new MissingMethodException(folderType.FullName, "Parse(PsdFile)");

        var root = parse.Invoke(null, [psd])
            ?? throw new InvalidOperationException("PsdFolder.Parse returned null.");

        var rawItems = FlattenFileSourceTree(root).ToArray();
        var constructorSurface = CaptureConstructorSurface(tachieAssembly);
        var editorAttempts = TryCreateEditorTree(tachieAssembly, fixture);

        var checks = new List<CheckResult>();
        var byName = rawItems.ToDictionary(x => x.Name, StringComparer.Ordinal);

        checks.Add(Check(
            "fixture-control-hidden",
            byName.TryGetValue("PlainHidden", out var plainHidden) && !plainHidden.IsEnabled,
            plainHidden is null ? "PlainHidden was not found." : $"PlainHidden IsEnabled={plainHidden.IsEnabled}"));

        checks.Add(Check(
            "fixture-control-visible",
            byName.TryGetValue("PlainVisible", out var plainVisible) && plainVisible.IsEnabled,
            plainVisible is null ? "PlainVisible was not found." : $"PlainVisible IsEnabled={plainVisible.IsEnabled}"));

        byName.TryGetValue("*StarA", out var starA);
        byName.TryGetValue("*StarB", out var starB);
        byName.TryGetValue("!BangHidden", out var bang);

        checks.Add(Check(
            "star-pair-not-auto-exclusive-at-file-model",
            starA is not null &&
            starB is not null &&
            starA.IsEnabled &&
            starB.IsEnabled,
            $"*StarA={starA?.IsEnabled.ToString() ?? "missing"}, *StarB={starB?.IsEnabled.ToString() ?? "missing"}"));

        checks.Add(Check(
            "bang-not-auto-forced-at-file-model",
            bang is not null &&
            !bang.IsEnabled,
            $"!BangHidden={bang?.IsEnabled.ToString() ?? "missing"}"));

        checks.Add(Check(
            "flip-suffix-remains-literal-at-file-model",
            byName.ContainsKey("Pose:flipx") &&
            byName.ContainsKey("Pose"),
            "Expected Pose and Pose:flipx layer names to survive parsing literally."));

        var controlsOk = checks.Take(2).All(x => x.Pass);
        var status = controlsOk ? "PASS" : "FAIL";

        return new RuntimeResult(
            status,
            cultureInfo.Name,
            fixture,
            rawItems,
            constructorSurface,
            editorAttempts,
            null,
            checks.ToArray());
    }

    static Assembly LoadYmmAssembly(string simpleName)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
        if (loaded is not null)
            return loaded;

        var path = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");
        if (!File.Exists(path))
            throw new FileNotFoundException($"YMM4 assembly was not found: {path}");
        return Assembly.LoadFrom(path);
    }

    static Type RequireType(Assembly assembly, string fullName)
        => assembly.GetType(fullName, throwOnError: false)
            ?? throw new TypeLoadException($"Type not found: {fullName} in {assembly.FullName}");

    static IEnumerable<ItemState> FlattenFileSourceTree(object root)
    {
        var queue = new Queue<(object Item, string Parent)>();
        foreach (var child in GetItems(root))
            queue.Enqueue((child, "/"));

        while (queue.Count > 0)
        {
            var (item, parent) = queue.Dequeue();
            var type = item.GetType();
            var name = Read<string>(item, "Name") ?? "";
            var path = Read<string>(item, "Path") ?? "";
            var id = ReadObject(item, "Id")?.ToString() ?? "";
            var enabled = Read<bool>(item, "IsEnabled");
            yield return new ItemState(type.FullName ?? type.Name, name, path, id, enabled, parent);

            foreach (var child in GetItems(item))
                queue.Enqueue((child, path));
        }
    }

    static IEnumerable<object> GetItems(object value)
    {
        var p = value.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p?.GetValue(value) is not System.Collections.IEnumerable enumerable)
            yield break;

        foreach (var x in enumerable)
            if (x is not null)
                yield return x;
    }

    static T? Read<T>(object target, string property)
    {
        var value = ReadObject(target, property);
        return value is T typed ? typed : default;
    }

    static object? ReadObject(object target, string property)
        => target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

    static ConstructorSurface[] CaptureConstructorSurface(Assembly tachieAssembly)
    {
        var names = new[]
        {
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdLayerEditorViewModel",
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdFolderViewModel",
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdLayerViewModel",
            "YukkuriMovieMaker.Plugin.Tachie.Psd.SwitchLayerCommand",
            "YukkuriMovieMaker.Plugin.Tachie.Psd.ShiftLayerCommand",
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdTachieCharacterParameter",
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdTachieItemParameter"
        };

        var rows = new List<ConstructorSurface>();
        foreach (var name in names)
        {
            var type = tachieAssembly.GetType(name, throwOnError: false);
            if (type is null)
            {
                rows.Add(new ConstructorSurface(name, ["<type-not-found>"]));
                continue;
            }

            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(c => FormatConstructor(c))
                .ToArray();
            rows.Add(new ConstructorSurface(name, constructors));
        }
        return rows.ToArray();
    }

    static string FormatConstructor(ConstructorInfo ctor)
        => $"{ctor.Attributes}: {ctor.DeclaringType?.FullName}({string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))})";

    static EditorAttempt[] TryCreateEditorTree(Assembly tachieAssembly, string fixture)
    {
        var editorType = tachieAssembly.GetType(
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdLayerEditorViewModel",
            throwOnError: false);
        if (editorType is null)
            return [new EditorAttempt("<none>", false, "PsdLayerEditorViewModel not found.", [])];

        var characterType = tachieAssembly.GetType(
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdTachieCharacterParameter",
            throwOnError: false);
        var itemType = tachieAssembly.GetType(
            "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdTachieItemParameter",
            throwOnError: false);

        object? character = characterType is null ? null : Activator.CreateInstance(characterType, nonPublic: true);
        object? item = itemType is null ? null : Activator.CreateInstance(itemType, nonPublic: true);
        SetIfPresent(character, "FilePath", fixture);
        SetIfPresent(item, "FilePath", fixture);

        var attempts = new List<EditorAttempt>();

        foreach (var ctor in editorType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var signature = FormatConstructor(ctor);
            try
            {
                var args = ctor.GetParameters()
                    .Select(p => CreateArgument(p.ParameterType, character, item, fixture))
                    .ToArray();

                if (args.Any(a => ReferenceEquals(a, UnsupportedArgument.Instance)))
                {
                    attempts.Add(new EditorAttempt(signature, false, "Unsupported constructor argument.", []));
                    continue;
                }

                var vm = ctor.Invoke(args.Select(a => ReferenceEquals(a, NullArgument.Instance) ? null : a).ToArray());
                if (vm is null)
                {
                    attempts.Add(new EditorAttempt(signature, false, "Constructor returned null.", []));
                    continue;
                }

                var load = editorType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        m.Name == "LoadItemsAsync" &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(string));
                if (load is not null)
                {
                    var returned = load.Invoke(vm, [fixture]);
                    if (returned is Task task)
                        task.GetAwaiter().GetResult();
                }

                var root = editorType.GetProperty("Root", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(vm);
                var rows = root is null ? [] : FlattenViewModelTree(root).ToArray();
                attempts.Add(new EditorAttempt(signature, root is not null, root is null ? "Root remained null." : "Root loaded.", rows));

                if (vm is IDisposable disposable)
                    disposable.Dispose();

                if (root is not null)
                    break;
            }
            catch (Exception ex)
            {
                attempts.Add(new EditorAttempt(signature, false, ex.GetBaseException().Message, []));
            }
        }

        return attempts.ToArray();
    }

    static IEnumerable<ViewModelState> FlattenViewModelTree(object root)
    {
        var queue = new Queue<object>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            var name = Read<string>(item, "Name") ?? "";
            var path = Read<string>(item, "Path") ?? "";
            var enabled = Read<bool>(item, "IsEnabled");

            var toggle = ReadObject(item, "ToggleEnableCommand");
            var switchCommand = ReadObject(item, "SwitchLayerCommand");
            var shiftCommand = ReadObject(item, "ShiftLayerCommand");

            yield return new ViewModelState(
                item.GetType().FullName ?? item.GetType().Name,
                name,
                path,
                enabled,
                CanExecute(toggle, item),
                CanExecute(switchCommand, item),
                CanExecute(shiftCommand, item));

            foreach (var child in GetItems(item))
                queue.Enqueue(child);
        }
    }

    static bool? CanExecute(object? command, object parameter)
    {
        if (command is null)
            return null;
        var method = command.GetType().GetMethod("CanExecute", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
            return null;
        return method.Invoke(command, [parameter]) as bool?;
    }

    static object CreateArgument(Type type, object? character, object? item, string fixture)
    {
        if (item is not null && type.IsInstanceOfType(item))
            return item;
        if (character is not null && type.IsInstanceOfType(character))
            return character;
        if (type == typeof(string))
            return fixture;
        if (!type.IsValueType)
        {
            var ctor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            if (ctor is not null)
                return ctor.Invoke(null);
            if (type.IsInterface || type.IsAbstract)
                return NullArgument.Instance;
            return NullArgument.Instance;
        }
        return Activator.CreateInstance(type) ?? UnsupportedArgument.Instance;
    }

    static void SetIfPresent(object? target, string property, object? value)
    {
        if (target is null)
            return;
        var p = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p?.CanWrite == true)
            p.SetValue(target, value);
    }

    static CheckResult Check(string id, bool pass, string observation)
        => new(id, pass, observation);

    sealed class NullArgument
    {
        public static readonly NullArgument Instance = new();
    }

    sealed class UnsupportedArgument
    {
        public static readonly UnsupportedArgument Instance = new();
    }
}

static class PsdFixtureWriter
{
    const int Width = 4;
    const int Height = 4;

    public static void Write(string path)
    {
        var layers = new[]
        {
            new Layer("*StarA", false, 220, 20, 20),
            new Layer("*StarB", false, 20, 220, 20),
            new Layer("!BangHidden", true, 20, 20, 220),
            new Layer("PlainHidden", true, 220, 220, 20),
            new Layer("PlainVisible", false, 220, 20, 220),
            new Layer("Pose", false, 20, 220, 220),
            new Layer("Pose:flipx", true, 128, 64, 220),
            new Layer("Pose:flipy", true, 64, 128, 220),
            new Layer("Pose:flipxy", true, 220, 128, 64),
        };

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        WriteAscii(file, "8BPS");
        WriteU16(file, 1);
        file.Write(new byte[6]);
        WriteU16(file, 4);
        WriteU32(file, Height);
        WriteU32(file, Width);
        WriteU16(file, 8);
        WriteU16(file, 3);

        WriteU32(file, 0);
        WriteU32(file, 0);

        using var records = new MemoryStream();
        using var images = new MemoryStream();

        foreach (var layer in layers)
        {
            WriteI32(records, 0);
            WriteI32(records, 0);
            WriteI32(records, Height);
            WriteI32(records, Width);

            WriteU16(records, 4);
            foreach (var channelId in new short[] { 0, 1, 2, -1 })
            {
                WriteI16(records, channelId);
                WriteU32(records, 2 + Width * Height);
            }

            WriteAscii(records, "8BIM");
            WriteAscii(records, "norm");
            records.WriteByte(255);
            records.WriteByte(0);
            records.WriteByte(layer.Hidden ? (byte)2 : (byte)0);
            records.WriteByte(0);

            using var extra = new MemoryStream();
            WriteU32(extra, 0);
            WriteU32(extra, 0);
            WritePascal4(extra, layer.Name);

            WriteU32(records, checked((uint)extra.Length));
            extra.Position = 0;
            extra.CopyTo(records);

            WriteChannel(images, layer.R);
            WriteChannel(images, layer.G);
            WriteChannel(images, layer.B);
            WriteChannel(images, 255);
        }

        using var layerInfoPayload = new MemoryStream();
        WriteI16(layerInfoPayload, checked((short)layers.Length));
        records.Position = 0;
        records.CopyTo(layerInfoPayload);
        images.Position = 0;
        images.CopyTo(layerInfoPayload);
        if ((layerInfoPayload.Length & 1) != 0)
            layerInfoPayload.WriteByte(0);

        using var layerMaskPayload = new MemoryStream();
        WriteU32(layerMaskPayload, checked((uint)layerInfoPayload.Length));
        layerInfoPayload.Position = 0;
        layerInfoPayload.CopyTo(layerMaskPayload);
        WriteU32(layerMaskPayload, 0);

        WriteU32(file, checked((uint)layerMaskPayload.Length));
        layerMaskPayload.Position = 0;
        layerMaskPayload.CopyTo(file);

        // The current PsdParser constructor used by YMM4 does not parse the merged image section,
        // but append a valid raw composite so the fixture remains a structurally ordinary PSD.
        WriteU16(file, 0);
        for (var c = 0; c < 4; c++)
            file.Write(new byte[Width * Height]);
    }

    static void WriteChannel(Stream stream, byte value)
    {
        WriteU16(stream, 0);
        var bytes = Enumerable.Repeat(value, Width * Height).Select(x => (byte)x).ToArray();
        stream.Write(bytes);
    }

    static void WritePascal4(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > 255)
            throw new ArgumentOutOfRangeException(nameof(value));
        var start = stream.Position;
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
        while ((stream.Position - start) % 4 != 0)
            stream.WriteByte(0);
    }

    static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));

    static void WriteU16(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)(value & 0xff));
    }

    static void WriteI16(Stream stream, short value) => WriteU16(stream, unchecked((ushort)value));

    static void WriteU32(Stream stream, uint value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)(value & 0xff));
    }

    static void WriteI32(Stream stream, int value) => WriteU32(stream, unchecked((uint)value));

    sealed record Layer(string Name, bool Hidden, byte R, byte G, byte B);
}

sealed record RuntimeResult(
    string Status,
    string Culture,
    string? FixturePath,
    ItemState[] RawItems,
    ConstructorSurface[] ConstructorSurface,
    EditorAttempt[] EditorAttempts,
    string? Error,
    CheckResult[]? Checks = null);

sealed record ItemState(
    string Type,
    string Name,
    string Path,
    string Id,
    bool IsEnabled,
    string Parent);

sealed record ConstructorSurface(string Type, string[] Constructors);
sealed record EditorAttempt(string Constructor, bool RootLoaded, string Observation, ViewModelState[] Items);
sealed record ViewModelState(
    string Type,
    string Name,
    string Path,
    bool IsEnabled,
    bool? ToggleCanExecute,
    bool? SwitchCanExecute,
    bool? ShiftCanExecute);
sealed record CheckResult(string Id, bool Pass, string Observation);
