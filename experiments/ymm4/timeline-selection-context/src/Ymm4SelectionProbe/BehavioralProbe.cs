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
                    dispatcher.BeginInvoke(new Action(() => BehavioralSelectionProbe.Start(Path.GetFullPath(dir))));
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
                var voice = fixtures.OfType<VoiceItem>().Single();
                var face = fixtures.OfType<TachieFaceItem>().Single();

                timer.Stop();
                Run(active, timeline, voice, face);
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
        Append("ACTIVE_TIMELINE_VM " + active.GetType().FullName);
        DumpRelevant("Timeline public selection surface", timeline, publicOnly: true);
        DumpRelevant("VoiceItem public selection surface", voice, publicOnly: true);
        DumpRelevant("TachieFaceItem public selection surface", face, publicOnly: true);
        DumpRelevant("VoiceItem all selection surface", voice, publicOnly: false);

        var modelSelection = FindBoolLikeMember(voice.GetType(), publicOnly: true);
        var modelReadable = modelSelection != null;
        Append("MODEL_SELECTION_CANDIDATE " + Describe(modelSelection));

        var itemsProp = active.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.Public);
        var publicItems = itemsProp?.GetValue(active) as IEnumerable;
        var vmList = publicItems?.Cast<object>().ToArray() ?? [];
        Append($"PUBLIC_VM_ITEMS count={vmList.Length}");

        object? voiceVm = null;
        object? faceVm = null;
        var publicModelMapping = true;
        foreach (var vm in vmList)
        {
            DumpRelevant("TimelineItemViewModel " + vm.GetType().FullName, vm, publicOnly: true);
            var publicModel = FindReferencedItem(vm, BindingFlags.Instance | BindingFlags.Public);
            var anyModel = publicModel ?? FindReferencedItem(vm, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ReferenceEquals(anyModel, voice)) voiceVm = vm;
            if (ReferenceEquals(anyModel, face)) faceVm = vm;
            if (ReferenceEquals(anyModel, voice) || ReferenceEquals(anyModel, face)) publicModelMapping &= publicModel != null;
        }

        var vmMappingFound = voiceVm != null && faceVm != null;
        Append($"VM_MAPPING found={vmMappingFound} public={publicModelMapping}");

        MemberInfo? voiceVmSelection = voiceVm == null ? null : FindBoolLikeMember(voiceVm.GetType(), publicOnly: true);
        MemberInfo? faceVmSelection = faceVm == null ? null : FindBoolLikeMember(faceVm.GetType(), publicOnly: true);
        Append("VOICE_VM_SELECTION " + Describe(voiceVmSelection));
        Append("FACE_VM_SELECTION " + Describe(faceVmSelection));
        var publicVmSelection = voiceVmSelection != null && faceVmSelection != null;

        var voiceSelected = false;
        var faceSelected = false;
        var clearObserved = false;

        if (vmMappingFound && publicVmSelection)
        {
            SetBoolLike(voiceVm!, voiceVmSelection!, true);
            SetBoolLike(faceVm!, faceVmSelection!, false);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            voiceSelected = ReadBoolLike(voiceVm!, voiceVmSelection!) == true && ReadBoolLike(faceVm!, faceVmSelection!) == false;
            Append("ASSERT voice_vm_selected=" + voiceSelected);

            SetBoolLike(voiceVm!, voiceVmSelection!, false);
            SetBoolLike(faceVm!, faceVmSelection!, true);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            faceSelected = ReadBoolLike(voiceVm!, voiceVmSelection!) == false && ReadBoolLike(faceVm!, faceVmSelection!) == true;
            Append("ASSERT face_vm_selected=" + faceSelected);

            SetBoolLike(faceVm!, faceVmSelection!, false);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            clearObserved = ReadBoolLike(voiceVm!, voiceVmSelection!) == false && ReadBoolLike(faceVm!, faceVmSelection!) == false;
            Append("ASSERT selection_clear=" + clearObserved);
        }

        var modelSyncObserved = false;
        if (modelSelection != null && voiceVm != null && voiceVmSelection != null)
        {
            SetBoolLike(voiceVm, voiceVmSelection, true);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var selectedValue = ReadBoolLike(voice, modelSelection);
            SetBoolLike(voiceVm, voiceVmSelection, false);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var clearedValue = ReadBoolLike(voice, modelSelection);
            modelSyncObserved = selectedValue == true && clearedValue == false;
            Append($"ASSERT model_selection_sync={modelSyncObserved} selected={selectedValue} cleared={clearedValue}");
        }

        var characterReadable = voice.CharacterName == "CNWL_SelectA" && face.CharacterName == "CNWL_SelectA";
        Append("ASSERT character_readable=" + characterReadable);

        var status = vmMappingFound && publicVmSelection && voiceSelected && faceSelected && clearObserved && characterReadable
            ? "PASS_BEHAVIOR_VM"
            : "PARTIAL_BEHAVIOR";
        if (modelSyncObserved && modelReadable) status = "PASS_BEHAVIOR_MODEL";

        WriteResult(status, modelReadable, vmMappingFound, publicModelMapping, publicVmSelection, modelSyncObserved, characterReadable);
    }

    private static IItem? FindReferencedItem(object vm, BindingFlags flags)
    {
        foreach (var p in vm.GetType().GetProperties(flags).Where(x => x.CanRead && x.GetIndexParameters().Length == 0 && typeof(IItem).IsAssignableFrom(x.PropertyType)))
        {
            try { if (p.GetValue(vm) is IItem item) return item; } catch { }
        }
        foreach (var f in vm.GetType().GetFields(flags).Where(x => typeof(IItem).IsAssignableFrom(x.FieldType)))
        {
            try { if (f.GetValue(vm) is IItem item) return item; } catch { }
        }
        return null;
    }

    private static MemberInfo? FindBoolLikeMember(Type type, bool publicOnly)
    {
        var flags = BindingFlags.Instance | (publicOnly ? BindingFlags.Public : BindingFlags.Public | BindingFlags.NonPublic);
        var members = type.GetMembers(flags)
            .Where(x => x.Name.Equals("IsSelected", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Selected", StringComparison.OrdinalIgnoreCase))
            .Where(CanReadBoolLike)
            .OrderBy(x => x.Name.Equals("IsSelected", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Name)
            .ToArray();
        return members.FirstOrDefault();
    }

    private static bool CanReadBoolLike(MemberInfo member)
    {
        var type = member switch
        {
            PropertyInfo p when p.CanRead && p.GetIndexParameters().Length == 0 => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => null
        };
        if (type == typeof(bool)) return true;
        return type?.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.PropertyType == typeof(bool);
    }

    private static bool? ReadBoolLike(object instance, MemberInfo member)
    {
        object? value = member switch
        {
            PropertyInfo p => p.GetValue(instance),
            FieldInfo f => f.GetValue(instance),
            _ => null
        };
        if (value is bool b) return b;
        return value?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value) as bool?;
    }

    private static void SetBoolLike(object instance, MemberInfo member, bool value)
    {
        if (member is PropertyInfo p && p.PropertyType == typeof(bool) && p.SetMethod?.IsPublic == true)
        {
            p.SetValue(instance, value);
            return;
        }
        if (member is FieldInfo f && f.FieldType == typeof(bool) && f.IsPublic && !f.IsInitOnly)
        {
            f.SetValue(instance, value);
            return;
        }
        object? holder = member switch
        {
            PropertyInfo p => p.GetValue(instance),
            FieldInfo f => f.GetValue(instance),
            _ => null
        };
        var valueProperty = holder?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty?.PropertyType != typeof(bool) || valueProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException("Selection member is readable but not publicly settable: " + Describe(member));
        valueProperty.SetValue(holder, value);
    }

    private static void DumpRelevant(string title, object instance, bool publicOnly)
    {
        Append("=== " + title + " :: " + instance.GetType().FullName + " ===");
        var flags = BindingFlags.Instance | (publicOnly ? BindingFlags.Public : BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var member in instance.GetType().GetMembers(flags)
                     .Where(x => Keywords.Any(k => x.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(x => x.MemberType).ThenBy(x => x.Name).Take(160))
        {
            Append(Describe(member));
        }
    }

    private static string Describe(MemberInfo? member)
    {
        if (member == null) return "<none>";
        return member switch
        {
            PropertyInfo p => $"PROPERTY {(p.GetMethod?.IsPublic == true ? "public" : "nonpublic")} {p.PropertyType.FullName} {p.Name} read={p.CanRead} write={p.CanWrite}",
            FieldInfo f => $"FIELD {(f.IsPublic ? "public" : "nonpublic")} {f.FieldType.FullName} {f.Name}",
            EventInfo e => $"EVENT {(e.AddMethod?.IsPublic == true ? "public" : "nonpublic")} {e.EventHandlerType?.FullName} {e.Name}",
            MethodInfo m => $"METHOD {(m.IsPublic ? "public" : "nonpublic")} {m.ReturnType.FullName} {m.Name}",
            _ => member.MemberType + " " + member.Name
        };
    }

    private static void WriteResult(string status, bool modelReadable, bool vmMappingFound, bool publicModelMapping, bool publicVmSelection, bool modelSyncObserved, bool characterReadable)
    {
        var lines = new[]
        {
            "status=" + status,
            "model_public_selection_path=" + modelReadable,
            "vm_mapping_found=" + vmMappingFound,
            "vm_public_model_mapping=" + publicModelMapping,
            "vm_public_selection_path=" + publicVmSelection,
            "model_selection_sync_observed=" + modelSyncObserved,
            "character_readable=" + characterReadable
        };
        File.WriteAllLines(Path.Combine(output, "behavior-result.txt"), lines, new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "behavior-surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
