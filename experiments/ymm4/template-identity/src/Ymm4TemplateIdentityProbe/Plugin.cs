using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4TemplateIdentityProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Template Identity Probe";
    public void SetCulture(CultureInfo cultureInfo) => IdentityProof.Schedule();
}

internal static class IdentityProof
{
    private static bool scheduled; private static string output="";
    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_TEMPLATE_ID_DIR");
        if(scheduled||string.IsNullOrWhiteSpace(dir))return;scheduled=true;output=Path.GetFullPath(dir);Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }
    private static void Start()
    {
        var ticks=0;var projectCreated=false;var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(500)};
        timer.Tick+=(_,_)=>{try{ticks++;foreach(Window w in Application.Current.Windows){var main=w.DataContext;if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;var active=main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);if(active==null&&!projectCreated){projectCreated=true;main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);break;}var timeline=active?.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline;if(timeline==null)continue;timer.Stop();Run(timeline);return;}if(ticks>=90){timer.Stop();Result("FAIL_TIMEOUT",[]);}}catch(Exception ex){timer.Stop();File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));Result("FAIL_EXCEPTION",["message="+ex.GetBaseException().Message]);}};timer.Start();
    }
    private static void Run(Timeline timeline)
    {
        var character=new Character{Name="CNWL_TemplateIdentity"};
        var g1=Guid.Parse("11111111-2222-3333-4444-555555555555");var g2=Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var t1=new ItemTemplate(ItemTemplateGroup.TachieFaceItem,"CNWL Same Name",new IItem[]{new TachieFaceItem(character){Frame=11,Length=21,Layer=7}},timeline,g1);
        var t2=new ItemTemplate(ItemTemplateGroup.TachieFaceItem,"CNWL Same Name",new IItem[]{new TachieFaceItem(character){Frame=12,Length=22,Layer=8}},timeline,g2);
        ItemSettings.Default.Templates.Add(t1);ItemSettings.Default.Templates.Add(t2);
        try
        {
            var sb=new StringBuilder();DumpObject(sb,"TEMPLATE1",t1);DumpObject(sb,"TEMPLATE2",t2);DumpType(sb,"ITEM_SETTINGS",ItemSettings.Default.GetType(),ItemSettings.Default);
            File.WriteAllText(Path.Combine(output,"surface.txt"),sb.ToString(),new UTF8Encoding(false));
            var sameNameCoexists=ItemSettings.Default.Templates.Contains(t1)&&ItemSettings.Default.Templates.Contains(t2)&&t1.Name==t2.Name;
            var publicGuidProps=t1.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&p.PropertyType==typeof(Guid)).ToArray();
            var values=publicGuidProps.Select(p=>(p.Name,V1:(Guid)p.GetValue(t1)!,V2:(Guid)p.GetValue(t2)!)).ToArray();
            var constructorGuidObservable=values.Any(x=>(x.V1==g1&&x.V2==g2)||(x.V1==g2&&x.V2==g1));
            var distinctPublicGuid=values.Any(x=>x.V1!=Guid.Empty&&x.V2!=Guid.Empty&&x.V1!=x.V2);
            var equalityDistinct=!t1.Equals(t2);
            var saveMethods=ItemSettings.Default.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>new[]{"save","write","serialize","store"}.Any(k=>m.Name.Contains(k,StringComparison.OrdinalIgnoreCase))).Select(m=>m.ToString()).ToArray();
            Result("PASS_TEMPLATE_IDENTITY_DISCOVERY",
            [
                $"same_name_templates_coexist={sameNameCoexists}",
                $"public_guid_property_count={values.Length}",
                $"distinct_public_guid={distinctPublicGuid}",
                $"constructor_guid_observable={constructorGuidObservable}",
                $"equals_distinguishes_instances={equalityDistinct}",
                $"public_save_method_count={saveMethods.Length}",
                "public_guid_properties="+string.Join(";",values.Select(x=>$"{x.Name}:{x.V1}|{x.V2}")),
                "public_save_methods="+string.Join(";",saveMethods)
            ]);
        }
        finally{ItemSettings.Default.Templates.Remove(t1);ItemSettings.Default.Templates.Remove(t2);}
    }
    private static void DumpObject(StringBuilder sb,string title,object instance)
    {
        sb.AppendLine("=== "+title+" "+instance.GetType().FullName+" ===");
        foreach(var p in instance.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0).OrderBy(p=>p.Name))
        {try{var v=p.GetValue(instance);sb.AppendLine($"PROPERTY {p.PropertyType.FullName} {p.Name} = {Safe(v)}");}catch(Exception ex){sb.AppendLine($"PROPERTY {p.Name} = <throw {ex.GetBaseException().GetType().Name}>");}}
    }
    private static void DumpType(StringBuilder sb,string title,Type type,object instance)
    {
        sb.AppendLine("=== "+title+" "+type.FullName+" ===");
        foreach(var m in type.GetMembers(BindingFlags.Instance|BindingFlags.Public).Where(m=>new[]{"template","save","write","serialize","store","path","file","id","guid","key"}.Any(k=>m.Name.Contains(k,StringComparison.OrdinalIgnoreCase))).OrderBy(m=>m.MemberType).ThenBy(m=>m.Name))sb.AppendLine(m.ToString());
    }
    private static string Safe(object? v)=>v switch{null=>"<null>",string s=>'"'+s+'"',Guid g=>g.ToString(),System.Collections.ICollection c=>$"<{v.GetType().FullName} Count={c.Count}>",_=>v.GetType().IsPrimitive||v is Enum?v.ToString()??"":$"<{v.GetType().FullName}>"};
    private static void Result(string status,IEnumerable<string> details)=>File.WriteAllLines(Path.Combine(output,"result.txt"),new[]{"status="+status}.Concat(details),new UTF8Encoding(false));
}
