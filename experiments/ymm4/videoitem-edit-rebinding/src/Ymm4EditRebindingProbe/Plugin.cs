using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4EditRebindingProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VideoItem Edit Rebinding";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output="";
    private static readonly List<object> requirements=[];

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_EDIT_REBIND_OUTPUT");
        if(scheduled||string.IsNullOrWhiteSpace(dir)) return;
        scheduled=true; output=Path.GetFullPath(dir); Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start),DispatcherPriority.ApplicationIdle);
    }

    private static void Start()
    {
        bool created=false;
        var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(300)};
        timer.Tick+=(_,_)=>{
            try
            {
                var main=Application.Current.Windows.Cast<Window>().Select(w=>w.DataContext)
                    .FirstOrDefault(x=>x?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel");
                if(main==null)return;
                var active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                if(active==null)
                {
                    if(!created){created=true;main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);}
                    return;
                }
                var timeline=FindTimeline(active);
                timer.Stop();
                Run(main,active,timeline);
                Write("PASS_EDIT_REBIND_DISCOVERY",null);
            }
            catch(Exception ex){timer.Stop();Write("FAIL_EDIT_REBIND_DISCOVERY",ex.ToString());}
        };
        timer.Start();
    }

    private static Timeline FindTimeline(object active)
    {
        foreach(var p in active.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            if(p.GetIndexParameters().Length!=0||!typeof(Timeline).IsAssignableFrom(p.PropertyType))continue;
            try{if(p.GetValue(active) is Timeline t)return t;}catch{}
        }
        throw new MissingMemberException("Timeline not found.");
    }

    private static bool Interesting(string n)
    {
        string[] keys=["Move","Trim","Resize","Crop","Length","Frame","Copy","Paste","Duplicate","Clone","Undo","Redo","History","Remove","Delete","Split"];
        return keys.Any(k=>n.Contains(k,StringComparison.OrdinalIgnoreCase));
    }

    private static string Method(MethodInfo m)
        =>$"{(m.IsPublic?"public":"nonpublic")} {(m.IsStatic?"static":"instance")} {m.ReturnType.FullName} {m.DeclaringType?.FullName}::{m.Name}({string.Join(", ",m.GetParameters().Select(p=>(p.ParameterType.FullName??p.ParameterType.ToString())+" "+p.Name+(p.HasDefaultValue?"="+(p.DefaultValue??"null"):"")))})";

    private static void Dump(string label, object obj)
    {
        var t=obj.GetType();
        var lines=new List<string>{$"TYPE {label} {t.AssemblyQualifiedName}"};
        foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(p=>Interesting(p.Name)).OrderBy(p=>p.Name))
        {
            object? value=null; string valueText="";
            if(p.GetIndexParameters().Length==0)
            {
                try{value=p.GetValue(obj);valueText=value is ICommand c?$" ICommand canNull={SafeCan(c,null)}":value?.ToString()??"<null>";}catch(Exception ex){valueText="<read:"+ex.GetBaseException().GetType().Name+">";}
            }
            lines.Add($"PROPERTY {p.Name} type={p.PropertyType.FullName} get={(p.GetMethod?.IsPublic==true?"public":"nonpublic/none")} set={(p.SetMethod?.IsPublic==true?"public":"nonpublic/none")}{valueText}");
        }
        foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(f=>Interesting(f.Name)).OrderBy(f=>f.Name))
        {
            object? value=null;string valueText="";
            try{value=f.GetValue(obj);valueText=value is ICommand c?$" ICommand canNull={SafeCan(c,null)}":value?.ToString()??"<null>";}catch(Exception ex){valueText="<read:"+ex.GetBaseException().GetType().Name+">";}
            lines.Add($"FIELD {f.Name} type={f.FieldType.FullName}{valueText}");
        }
        foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>Interesting(m.Name)).OrderBy(m=>m.Name).ThenBy(m=>m.GetParameters().Length))
            lines.Add("METHOD "+Method(m));
        File.WriteAllLines(Path.Combine(output,label+".txt"),lines);
    }

    private static void Run(object main,object active,Timeline timeline)
    {
        int fps=timeline.VideoInfo.FPS;
        string media=Environment.GetEnvironmentVariable("CNWL_EDIT_REBIND_MEDIA")??throw new InvalidOperationException("media missing");
        Check("fixture_exists",File.Exists(media));
        Check("fps_positive",fps>0);
        var item=new VideoItem{FilePath=media,Frame=300,Length=fps*10,Layer=2,ContentOffset=TimeSpan.FromSeconds(5),Remark="CNWL_EDIT_REBIND"};
        item.PlaybackRate2.SetFirstValue(100);item.PlaybackRate2.SetAnimationParameters(item.Length,fps);
        Check("insert_source",timeline.TryAddItems([item],item.Frame,item.Layer));
        timeline.SelectedItems=System.Collections.Immutable.ImmutableList.Create<IItem>(item);
        timeline.CurrentFrame=item.Frame+fps*4;
        Dump("main",main);Dump("active",active);Dump("timeline",timeline);Dump("videoitem",item);
        foreach(var a in AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name?.StartsWith("YukkuriMovieMaker",StringComparison.Ordinal)==true))
        {
            Type[] types;try{types=a.GetTypes();}catch(ReflectionTypeLoadException ex){types=ex.Types.Where(x=>x!=null).Cast<Type>().ToArray();}
            var lines=new List<string>();
            foreach(var t in types.Where(t=>Interesting(t.Name)||t.GetMembers(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Any(m=>Interesting(m.Name))).Take(500))
            {
                foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>Interesting(m.Name)).Take(50))
                    lines.Add(Method(m));
            }
            File.WriteAllLines(Path.Combine(output,"assembly-"+a.GetName().Name+".txt"),lines.Distinct().OrderBy(x=>x));
        }
        Check("surface_dumped",File.Exists(Path.Combine(output,"timeline.txt"))&&File.Exists(Path.Combine(output,"active.txt")));
    }

    private static bool SafeCan(ICommand c,object? p){try{return c.CanExecute(p);}catch{return false;}}

    private static void Check(string id,bool passed)
    {
        requirements.Add(new{id,passed});
        if(!passed)throw new InvalidOperationException("Assertion failed: "+id);
    }

    private static void Write(string status,string? error)
    {
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{schema="cnwl.edit-rebinding.discovery.v1",status,host="4.56.1.0 Lite",sourceHead=Environment.GetEnvironmentVariable("GITHUB_SHA"),requirements,error},new JsonSerializerOptions{WriteIndented=true}));
    }
}
