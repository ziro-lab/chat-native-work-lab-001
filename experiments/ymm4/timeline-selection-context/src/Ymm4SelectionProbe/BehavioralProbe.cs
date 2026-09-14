#pragma warning disable CA2255 // Intentional validation-only module bootstrap inside the host process.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4SelectionProbe;

internal static class BehavioralProbeBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SELECTION_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _ = dispatcher.BeginInvoke(new Action(() => BehavioralSelectionProbe.Start(Path.GetFullPath(dir))));
                    return;
                }
                await Task.Delay(100);
            }
        });
    }
}

internal static class BehavioralSelectionProbe
{
    private static string output = "";
    private static readonly string[] Keywords = ["select", "selection", "item", "model"];

    internal static void Start(string outputDir)
    {
        output = outputDir;
        var ticks = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows.Cast<Window>()
                    .Select(x => x.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                var active = main?.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                if (active == null) return;
                var timeline = active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                if (timeline == null) return;
                var fixtures = timeline.Items.Where(x => x.Remark == "CNWL_SELECTION_FIXTURE").ToArray();
                if (fixtures.Length != 2) return;

                timer.Stop();
                Run(active, timeline, fixtures.OfType<VoiceItem>().Single(), fixtures.OfType<TachieFaceItem>().Single());
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", false, false, false, false, false, false);
            }

            if (ticks >= 100)
            {
                timer.Stop();
                WriteResult("FAIL_TIMEOUT", false, false, false, false, false, false);
            }
        };
        timer.Start();
    }

    private static void Run(object active, Timeline timeline, VoiceItem voice, TachieFaceItem face)
    {
        DumpRelevant("Timeline public selection surface", timeline, true);
        DumpRelevant("VoiceItem public selection surface", voice, true);
        DumpRelevant("TachieFaceItem public selection surface", face, true);

        var modelSelection = FindBoolLikeMember(voice.GetType(), true);
        var modelReadable = modelSelection != null;
        Append("MODEL_SELECTION_CANDIDATE " + Describe(modelSelection));

        var publicItems = active.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.Public)?.GetValue(active) as IEnumerable;
        var vmList = publicItems?.Cast<object>().ToArray() ?? [];
        Append($"PUBLIC_VM_ITEMS count={vmList.Length}");

        object? voiceVm = null;
        object? faceVm = null;
        var publicModelMapping = true;
        foreach (var vm in vmList)
        {
            DumpRelevant("TimelineItemViewModel", vm, true);
            var publicModel = FindReferencedItem(vm, BindingFlags.Instance | BindingFlags.Public);
            var anyModel = publicModel ?? FindReferencedItem(vm, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ReferenceEquals(anyModel, voice)) voiceVm = vm;
            if (ReferenceEquals(anyModel, face)) faceVm = vm;
            if (ReferenceEquals(anyModel, voice) || ReferenceEquals(anyModel, face)) publicModelMapping &= publicModel != null;
        }

        var vmMappingFound = voiceVm != null && faceVm != null;
        var voiceVmSelection = voiceVm == null ? null : FindBoolLikeMember(voiceVm.GetType(), true);
        var faceVmSelection = faceVm == null ? null : FindBoolLikeMember(faceVm.GetType(), true);
        var publicVmSelection = voiceVmSelection != null && faceVmSelection != null;
        Append($"VM_MAPPING found={vmMappingFound} public={publicModelMapping}");
        Append("VOICE_VM_SELECTION " + Describe(voiceVmSelection));
        Append("FACE_VM_SELECTION " + Describe(faceVmSelection));

        var voiceSelected = false;
        var faceSelected = false;
        var clearObserved = false;
        if (vmMappingFound && publicVmSelection)
        {
            SetBoolLike(voiceVm!, voiceVmSelection!, true);
            SetBoolLike(faceVm!, faceVmSelection!, false);
            Pump();
            voiceSelected = ReadBoolLike(voiceVm!, voiceVmSelection!) == true && ReadBoolLike(faceVm!, faceVmSelection!) == false;

            SetBoolLike(voiceVm!, voiceVmSelection!, false);
            SetBoolLike(faceVm!, faceVmSelection!, true);
            Pump();
            faceSelected = ReadBoolLike(voiceVm!, voiceVmSelection!) == false && ReadBoolLike(faceVm!, faceVmSelection!) == true;

            SetBoolLike(faceVm!, faceVmSelection!, false);
            Pump();
            clearObserved = ReadBoolLike(voiceVm!, voiceVmSelection!) == false && ReadBoolLike(faceVm!, faceVmSelection!) == false;
            Append($"ASSERT vm_voice={voiceSelected} vm_face={faceSelected} clear={clearObserved}");
        }

        var modelSyncObserved = false;
        if (modelSelection != null && voiceVm != null && voiceVmSelection != null)
        {
            SetBoolLike(voiceVm, voiceVmSelection, true);
            Pump();
            var selectedValue = ReadBoolLike(voice, modelSelection);
            SetBoolLike(voiceVm, voiceVmSelection, false);
            Pump();
            var clearedValue = ReadBoolLike(voice, modelSelection);
            modelSyncObserved = selectedValue == true && clearedValue == false;
            Append($"ASSERT model_sync={modelSyncObserved} selected={selectedValue} cleared={clearedValue}");
        }

        var characterReadable = voice.CharacterName == "CNWL_SelectA" && face.CharacterName == "CNWL_SelectA";
        var status = modelSyncObserved && modelReadable
            ? "PASS_BEHAVIOR_MODEL"
            : vmMappingFound && publicVmSelection && voiceSelected && faceSelected && clearObserved && characterReadable
                ? "PASS_BEHAVIOR_VM"
                : "PARTIAL_BEHAVIOR";
        WriteResult(status, modelReadable, vmMappingFound, publicModelMapping, publicVmSelection, modelSyncObserved, characterReadable);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IItem? FindReferencedItem(object vm, BindingFlags flags)
    {
        foreach (var prop in vm.GetType().GetProperties(flags).Where(x => x.CanRead && x.GetIndexParameters().Length == 0 && typeof(IItem).IsAssignableFrom(x.PropertyType)))
        {
            try { if (prop.GetValue(vm) is IItem item) return item; } catch { }
        }
        foreach (var field in vm.GetType().GetFields(flags).Where(x => typeof(IItem).IsAssignableFrom(x.FieldType)))
        {
            try { if (field.GetValue(vm) is IItem item) return item; } catch { }
        }
        return null;
    }

    private static MemberInfo? FindBoolLikeMember(Type type, bool publicOnly)
    {
        var flags = BindingFlags.Instance | (publicOnly ? BindingFlags.Public : BindingFlags.Public | BindingFlags.NonPublic);
        return type.GetMembers(flags)
            .Where(x => x.Name.Equals("IsSelected", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Selected", StringComparison.OrdinalIgnoreCase))
            .Where(CanReadBoolLike)
            .OrderBy(x => x.Name.Equals("IsSelected", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Name)
            .FirstOrDefault();
    }

    private static bool CanReadBoolLike(MemberInfo member)
    {
        var memberType = member switch
        {
            PropertyInfo prop when prop.CanRead && prop.GetIndexParameters().Length == 0 => prop.PropertyType,
            FieldInfo field => field.FieldType,
            _ => null
        };
        return memberType == typeof(bool) || memberType?.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.PropertyType == typeof(bool);
    }

    private static bool? ReadBoolLike(object instance, MemberInfo member)
    {
        object? memberValue = member switch
        {
            PropertyInfo prop => prop.GetValue(instance),
            FieldInfo field => field.GetValue(instance),
            _ => null
        };
        if (memberValue is bool value) return value;
        var raw = memberValue?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(memberValue);
        return raw is bool b ? b : null;
    }

    private static void SetBoolLike(object instance, MemberInfo member, bool value)
    {
        if (member is PropertyInfo boolProp && boolProp.PropertyType == typeof(bool) && boolProp.SetMethod?.IsPublic == true)
        {
            boolProp.SetValue(instance, value);
            return;
        }
        if (member is FieldInfo boolField && boolField.FieldType == typeof(bool) && boolField.IsPublic && !boolField.IsInitOnly)
        {
            boolField.SetValue(instance, value);
            return;
        }
        object? holder = member switch
        {
            PropertyInfo holderProp => holderProp.GetValue(instance),
            FieldInfo holderField => holderField.GetValue(instance),
            _ => null
        };
        var valueProperty = holder?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty?.PropertyType != typeof(bool) || valueProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException("Selection member is not publicly settable: " + Describe(member));
        valueProperty.SetValue(holder, value);
    }

    private static void DumpRelevant(string title, object instance, bool publicOnly)
    {
        Append("=== " + title + " :: " + instance.GetType().FullName + " ===");
        var flags = BindingFlags.Instance | (publicOnly ? BindingFlags.Public : BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var member in instance.GetType().GetMembers(flags)
                     .Where(x => Keywords.Any(k => x.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(x => x.MemberType).ThenBy(x => x.Name).Take(160))
            Append(Describe(member));
    }

    private static string Describe(MemberInfo? member) => member switch
    {
        null => "<none>",
        PropertyInfo prop => $"PROPERTY {(prop.GetMethod?.IsPublic == true ? "public" : "nonpublic")} {prop.PropertyType.FullName} {prop.Name} read={prop.CanRead} write={prop.CanWrite}",
        FieldInfo field => $"FIELD {(field.IsPublic ? "public" : "nonpublic")} {field.FieldType.FullName} {field.Name}",
        EventInfo ev => $"EVENT {(ev.AddMethod?.IsPublic == true ? "public" : "nonpublic")} {ev.EventHandlerType?.FullName} {ev.Name}",
        MethodInfo method => $"METHOD {(method.IsPublic ? "public" : "nonpublic")} {method.ReturnType.FullName} {method.Name}",
        _ => member.MemberType + " " + member.Name
    };

    private static void WriteResult(string status, bool modelReadable, bool vmMappingFound, bool publicModelMapping, bool publicVmSelection, bool modelSyncObserved, bool characterReadable)
    {
        File.WriteAllLines(Path.Combine(output, "behavior-result.txt"),
        [
            "status=" + status,
            "model_public_selection_path=" + modelReadable,
            "vm_mapping_found=" + vmMappingFound,
            "vm_public_model_mapping=" + publicModelMapping,
            "vm_public_selection_path=" + publicVmSelection,
            "model_selection_sync_observed=" + modelSyncObserved,
            "character_readable=" + characterReadable
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "behavior-surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
