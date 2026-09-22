using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;

namespace Ymm4P3MissingController;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL P3 missing-plugin controller";
    public void SetCulture(CultureInfo cultureInfo) => Controller.Schedule();
}

internal static class Controller
{
    private static bool scheduled;
    private static string output = "";

    internal static void Schedule()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CNWL_P3_MISSING_PHASE"), "preserve", StringComparison.Ordinal))
            return;
        var dir = Environment.GetEnvironmentVariable("CNWL_P3_MISSING_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static object? PublicProperty(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

    private static MethodInfo PublicMethod(object target, string name, params Type[] types) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, types)
        ?? throw new MissingMethodException(target.GetType().FullName, name);

    private static string? ProjectPath(object root)
    {
        var reactive = PublicProperty(root, "ProjectFilePath");
        return reactive?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(reactive) as string;
    }

    private static void Bootstrap()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(350) };
        var ticks = 0;
        var created = false;
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (++ticks > 120) throw new TimeoutException("controller bootstrap");
                var root = Application.Current.Windows.Cast<Window>().Select(x => x.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (root is null) return;

                if (PublicProperty(root, "ActiveTimelineViewModel") is null)
                {
                    if (!created)
                    {
                        created = true;
                        PublicMethod(root, "CreateProject", Type.EmptyTypes).Invoke(root, null);
                    }
                    return;
                }

                timer.Stop();
                var scratch = Path.Combine(output, "controller-scratch.ymmp");
                PublicMethod(root, "SaveProject", typeof(string)).Invoke(root, [scratch]);
                await Task.Delay(700);

                var source = Path.Combine(output, "subject-source.ymmp");
                var target = Path.Combine(output, "subject-resaved-without-plugin.ymmp");
                PublicMethod(root, "OpenProject", typeof(string)).Invoke(root, [source]);
                await Task.Delay(2200);
                var pathApplied = string.Equals(
                    Path.GetFullPath(ProjectPath(root) ?? ""),
                    Path.GetFullPath(source),
                    StringComparison.OrdinalIgnoreCase);

                PublicMethod(root, "SaveProject", typeof(string)).Invoke(root, [target]);
                await Task.Delay(700);

                var subjectLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(x => string.Equals(x.GetName().Name, "Ymm4P3MissingSubject", StringComparison.OrdinalIgnoreCase));
                var harmonyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true);

                var pass = pathApplied && File.Exists(target) && !subjectLoaded && !harmonyLoaded;
                File.WriteAllText(Path.Combine(output, "controller-result.json"), JsonSerializer.Serialize(new
                {
                    status = pass ? "PASS_P3_MISSING_CONTROLLER" : "FAIL_P3_MISSING_CONTROLLER",
                    pathApplied,
                    targetExists = File.Exists(target),
                    subjectAssemblyLoaded = subjectLoaded,
                    harmonyLoaded
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(output, "controller-result.json"), JsonSerializer.Serialize(new
                {
                    status = "FAIL_P3_MISSING_CONTROLLER",
                    error = ex.ToString()
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        };
        timer.Start();
    }
}
