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

        var type = typeof(VoiceItem);
        var identityMembers = new List<object>();

        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (p.Name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                    || p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                    || p.Name.Contains("ItemId", StringComparison.OrdinalIgnoreCase)
                    || p.PropertyType == typeof(Guid))
                {
                    identityMembers.Add(new
                    {
                        kind = "property",
                        declaringType = t.FullName,
                        p.Name,
                        type = p.PropertyType.FullName,
                        publicGet = p.GetMethod?.IsPublic == true,
                        publicSet = p.SetMethod?.IsPublic == true,
                        visibility = p.GetMethod?.IsPublic == true ? "public"
                            : p.GetMethod?.IsFamily == true ? "protected"
                            : p.GetMethod?.IsAssembly == true ? "internal"
                            : "nonpublic"
                    });
                }
            }

            foreach (var fld in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (fld.Name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                    || fld.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                    || fld.Name.Contains("ItemId", StringComparison.OrdinalIgnoreCase)
                    || fld.FieldType == typeof(Guid))
                {
                    identityMembers.Add(new
                    {
                        kind = "field",
                        declaringType = t.FullName,
                        fld.Name,
                        type = fld.FieldType.FullName,
                        publicGet = fld.IsPublic,
                        publicSet = fld.IsPublic && !fld.IsInitOnly,
                        visibility = fld.IsPublic ? "public"
                            : fld.IsFamily ? "protected"
                            : fld.IsAssembly ? "internal"
                            : "nonpublic"
                    });
                }
            }
        }

        var interfaceSurface = type.GetInterfaces()
            .Select(i => new
            {
                type = i.FullName,
                properties = i.GetProperties()
                    .Where(p => p.Name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                             || p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                             || p.Name.Contains("ItemId", StringComparison.OrdinalIgnoreCase)
                             || p.PropertyType == typeof(Guid))
                    .Select(p => new
                    {
                        p.Name,
                        type = p.PropertyType.FullName,
                        publicGet = p.GetMethod?.IsPublic == true,
                        publicSet = p.SetMethod?.IsPublic == true
                    }).ToArray()
            })
            .Where(x => x.properties.Length > 0)
            .ToArray();

        Check("identity_surface_inventoried", true);

        var a = new VoiceItem { Serif = "A", Hatsuon = "A" };
        var b = new VoiceItem { Serif = "B", Hatsuon = "B" };

        var guidMember = FindGuidMember(type);
        object? aIdentity = ReadMember(guidMember, a);
        object? bIdentity = ReadMember(guidMember, b);

        var guidAccessibleByPublicReflection = guidMember switch
        {
            PropertyInfo p => p.GetMethod?.IsPublic == true,
            FieldInfo fld => fld.IsPublic,
            _ => false
        };

        Check("voice_added_to_real_timeline", timeline.TryAddItems([a], 321, 7));

        object? afterTimelineIdentity = ReadMember(guidMember, a);
        var identityStableIfObservable = aIdentity is null || Equals(aIdentity, afterTimelineIdentity);
        Check("observable_identity_stable_after_timeline_add", identityStableIfObservable);

        string? jsonA = null;
        string? jsonB = null;
        string? jsonError = null;
        string? serializedGuidA = null;
        string? serializedGuidB = null;
        try
        {
            jsonA = JsonConvert.SerializeObject(a, Formatting.None, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });
            jsonB = JsonConvert.SerializeObject(b, Formatting.None, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

            serializedGuidA = TryReadGuidFromJson(jsonA);
            serializedGuidB = TryReadGuidFromJson(jsonB);
        }
        catch (Exception ex)
        {
            jsonError = ex.ToString();
        }

        var serializedGuidObserved =
            Guid.TryParse(serializedGuidA, out var sgA) &&
            Guid.TryParse(serializedGuidB, out var sgB) &&
            sgA != Guid.Empty &&
            sgB != Guid.Empty &&
            sgA != sgB;

        Check("serialized_guid_observed_unique", serializedGuidObserved);

        a.Serif = "A edited";
        a.Frame = 999;
        a.Layer = 2;

        string? jsonAfterEdit = null;
        string? serializedGuidAfterEdit = null;
        try
        {
            jsonAfterEdit = JsonConvert.SerializeObject(a, Formatting.None, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });
            serializedGuidAfterEdit = TryReadGuidFromJson(jsonAfterEdit);
        }
        catch { }

        Check("serialized_guid_stable_across_basic_edits",
            serializedGuidA is not null && serializedGuidA == serializedGuidAfterEdit);

        var cloneLikeMethods = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name.Contains("Clone", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Copy", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
            .Select(m => new
            {
                m.Name,
                visibility = m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "nonpublic",
                signature = m.ToString()
            })
            .Distinct()
            .OrderBy(x => x.Name)
            .ToArray();

        File.WriteAllText(Path.Combine(output, "behavior.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                host = "4.56.1.0 Lite",
                compileTimePublicGuid = false,
                identityMembers,
                interfaceSurface,
                selectedGuidMember = guidMember is null ? null : new
                {
                    kind = guidMember.MemberType.ToString(),
                    declaringType = guidMember.DeclaringType?.FullName,
                    guidMember.Name,
                    type = guidMember switch
                    {
                        PropertyInfo p => p.PropertyType.FullName,
                        FieldInfo fld => fld.FieldType.FullName,
                        _ => null
                    },
                    publicReadable = guidAccessibleByPublicReflection
                },
                reflectedIdentityA = aIdentity?.ToString(),
                reflectedIdentityB = bIdentity?.ToString(),
                reflectedIdentityAfterTimelineAdd = afterTimelineIdentity?.ToString(),
                serializedGuidA,
                serializedGuidB,
                serializedGuidAfterEdit,
                serializedGuidObserved,
                jsonError,
                serializedPreview = jsonA is null ? null : jsonA[..Math.Min(jsonA.Length, 1600)],
                cloneLikeMethods
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static MemberInfo? FindGuidMember(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var p = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(x => x.Name.Equals("Guid", StringComparison.OrdinalIgnoreCase)
                                  || x.PropertyType == typeof(Guid));
            if (p is not null) return p;

            var f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(x => x.Name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                                  || x.FieldType == typeof(Guid));
            if (f is not null) return f;
        }
        return null;
    }

    static object? ReadMember(MemberInfo? member, object target)
    {
        try
        {
            return member switch
            {
                PropertyInfo p => p.GetValue(target),
                FieldInfo f => f.GetValue(target),
                _ => null
            };
        }
        catch { return null; }
    }

    static string? TryReadGuidFromJson(string json)
    {
        try
        {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            var token = obj["Guid"];
            return token?.Type == Newtonsoft.Json.Linq.JTokenType.String ? token.ToObject<string>() : token?.ToString();
        }
        catch { return null; }
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
