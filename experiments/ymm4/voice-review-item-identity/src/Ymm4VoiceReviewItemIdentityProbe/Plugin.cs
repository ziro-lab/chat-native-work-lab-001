using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceReviewItemIdentityProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Voice Review Item Identity Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICE_REVIEW_IDENTITY_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    static void Start()
    {
        int ticks = 0;
        bool created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows.Cast<Window>()
                    .Select(w => w.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (main is null) return;

                var active = main.GetType().GetProperty("ActiveTimelineViewModel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(main);
                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                    }
                    if (ticks > 160) throw new TimeoutException("ActiveTimelineViewModel was not created.");
                    return;
                }

                timer.Stop();
                Run(active);
                Write("PASS_VOICE_REVIEW_ITEM_IDENTITY", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICE_REVIEW_ITEM_IDENTITY", ex.ToString());
            }
        };
        timer.Start();
    }

    static void Run(object active)
    {
        var timeline = FindTimeline(active) ?? throw new InvalidOperationException("Timeline not found.");
        Check("timeline_resolved", true);

        var guidProperty = typeof(VoiceItem).GetProperty("Guid", BindingFlags.Instance | BindingFlags.Public);
        Check("voiceitem_guid_public_readable",
            guidProperty?.GetMethod?.IsPublic == true && guidProperty.PropertyType == typeof(Guid));
        if (guidProperty is null) throw new MissingMemberException(typeof(VoiceItem).FullName, "Guid");

        var a = new VoiceItem { Serif = "A", Hatsuon = "A" };
        var b = new VoiceItem { Serif = "B", Hatsuon = "B" };

        var aGuid = (Guid)(guidProperty.GetValue(a) ?? Guid.Empty);
        var bGuid = (Guid)(guidProperty.GetValue(b) ?? Guid.Empty);

        Check("new_voiceitem_guid_nonempty", aGuid != Guid.Empty && bGuid != Guid.Empty);
        Check("new_voiceitem_guids_unique", aGuid != bGuid);

        a.Frame = 321;
        a.Layer = 7;
        a.Serif = "A edited";
        var afterEdit = (Guid)(guidProperty.GetValue(a) ?? Guid.Empty);
        Check("guid_stable_across_basic_edits", afterEdit == aGuid);

        Check("voice_added_to_real_timeline", timeline.TryAddItems([a], a.Frame, a.Layer));
        var afterAdd = (Guid)(guidProperty.GetValue(a) ?? Guid.Empty);
        Check("guid_stable_after_timeline_add", afterAdd == aGuid);
        Check("timeline_contains_same_guid",
            timeline.Items.OfType<VoiceItem>().Any(x => x.Guid == aGuid));

        string? json = null;
        string? jsonError = null;
        bool jsonContainsGuid = false;
        bool jsonRoundtripPreserved = false;
        try
        {
            json = JsonConvert.SerializeObject(a, Formatting.None, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });
            jsonContainsGuid = json.Contains(aGuid.ToString(), StringComparison.OrdinalIgnoreCase);
            try
            {
                var round = JsonConvert.DeserializeObject<VoiceItem>(json, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto
                });
                jsonRoundtripPreserved = round?.Guid == aGuid;
            }
            catch (Exception ex)
            {
                jsonError = "roundtrip: " + ex;
            }
        }
        catch (Exception ex)
        {
            jsonError = "serialize: " + ex;
        }

        var cloneLikeMethods = typeof(VoiceItem)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name.Contains("Clone", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Copy", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.ToString())
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        File.WriteAllText(Path.Combine(output, "behavior.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                host = "4.56.1.0 Lite",
                guidProperty = new
                {
                    declaringType = guidProperty.DeclaringType?.FullName,
                    guidProperty.PropertyType.FullName,
                    publicGet = guidProperty.GetMethod?.IsPublic == true,
                    publicSet = guidProperty.SetMethod?.IsPublic == true
                },
                firstGuid = aGuid,
                secondGuid = bGuid,
                afterBasicEdit = afterEdit,
                afterTimelineAdd = afterAdd,
                jsonContainsGuid,
                jsonRoundtripPreserved,
                jsonError,
                serializedPreview = json is null ? null : json[..Math.Min(json.Length, 1200)],
                cloneLikeMethods
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static Timeline? FindTimeline(object active)
    {
        for (var t = active.GetType(); t is not null; t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (typeof(Timeline).IsAssignableFrom(f.FieldType) && f.GetValue(active) is Timeline ft)
                    return ft;
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.GetIndexParameters().Length != 0 || !typeof(Timeline).IsAssignableFrom(p.PropertyType)) continue;
                try { if (p.GetValue(active) is Timeline pt) return pt; } catch { }
            }
        }
        return null;
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                schema = "cnwl.voice-review-item-identity.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
