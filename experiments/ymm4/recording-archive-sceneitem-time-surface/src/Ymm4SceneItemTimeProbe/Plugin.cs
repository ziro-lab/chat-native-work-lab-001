using System.Globalization;using System.IO;using System.Reflection;using System.Reflection.Emit;using System.Text;using System.Windows;using System.Windows.Threading;using YukkuriMovieMaker.Plugin;using YukkuriMovieMaker.Project.Items;
namespace Ymm4SceneItemTimeProbe;
public sealed class PluginEntry:ILocalizePlugin{public string Name=>"Chat Native Work Lab — Recording Archive SceneItem Time Surface";public void SetCulture(CultureInfo c)=>Probe.Schedule();}
internal static class Probe
{
 static bool once;static string output="";static readonly Dictionary<ushort,OpCode> Ops=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>unchecked((ushort)o.Value));
 static readonly string[] Keys=["frame","length","offset","content","time","start","end","rate","loop","scene"];
 public static void Schedule(){var d=Environment.GetEnvironmentVariable("CNWL_YMM4_SCENEITEM_TIME_DIR");if(once||string.IsNullOrWhiteSpace(d))return;once=true;output=Path.GetFullPath(d);Directory.CreateDirectory(output);Application.Current.Dispatcher.BeginInvoke(new Action(Run),DispatcherPriority.ApplicationIdle);}
 static void Run(){try{
  var t=typeof(SceneItem);Append("TYPE="+t.FullName);
  for(Type? x=t;x!=null;x=x.BaseType){
   foreach(var p in x.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Where(p=>Keys.Any(k=>p.Name.Contains(k,StringComparison.OrdinalIgnoreCase))).OrderBy(p=>p.Name))Append($"PROP {Access(p.GetGetMethod(true)??p.GetSetMethod(true))} {p.PropertyType.FullName} {x.FullName}.{p.Name}");
   foreach(var f in x.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Where(f=>Keys.Any(k=>f.Name.Contains(k,StringComparison.OrdinalIgnoreCase))).OrderBy(f=>f.Name))Append($"FIELD {(f.IsPublic?"public":"nonpublic")} {f.FieldType.FullName} {x.FullName}.{f.Name}");
  }
  var hits=new List<(MethodInfo,List<string>)>();
  foreach(var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name?.StartsWith("YukkuriMovieMaker",StringComparison.Ordinal)==true))
  foreach(var ty in Safe(asm))foreach(var m in ty.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)){
   if(m.IsAbstract||m.ContainsGenericParameters)continue;var refs=Refs(m);if(refs==null)continue;
   var hasScene=refs.Any(r=>r.Contains("SceneItem::get_SceneId",StringComparison.Ordinal)||r.Contains("SceneItem::SceneId",StringComparison.Ordinal));
   var hasTiming=refs.Any(r=>r.Contains("get_Frame",StringComparison.Ordinal)||r.Contains("get_Length",StringComparison.Ordinal)||r.Contains("ContentOffset",StringComparison.Ordinal)||r.Contains("CurrentFrame",StringComparison.Ordinal)||r.Contains("TimeSpan",StringComparison.Ordinal));
   if(hasScene&&hasTiming)hits.Add((m,refs));
  }
  foreach(var h in hits.OrderBy(x=>x.Item1.DeclaringType?.FullName).ThenBy(x=>x.Item1.Name)){Append("HIT "+Sig(h.Item1));foreach(var r in h.Item2.Distinct())Append("REF "+r);}
  File.WriteAllLines(Path.Combine(output,"result.txt"),["status=PASS_SCENEITEM_TIME_SURFACE","timing_reference_hit_count="+hits.Count,"candidate_methods="+string.Join(";",hits.Select(x=>Sig(x.Item1)))],new UTF8Encoding(false));
 }catch(Exception ex){Append("ERROR "+ex);File.WriteAllLines(Path.Combine(output,"result.txt"),["status=FAIL_EXCEPTION","detail="+ex.GetBaseException().Message],new UTF8Encoding(false));}}
 static List<string>? Refs(MethodInfo m){byte[]? b;try{b=m.GetMethodBody()?.GetILAsByteArray();}catch{return null;}if(b==null)return null;var refs=new List<string>();int i=0;while(i<b.Length){ushort c=b[i++];if(c==0xFE){if(i>=b.Length)break;c=(ushort)(0xFE00|b[i++]);}if(!Ops.TryGetValue(c,out var op))break;switch(op.OperandType){case OperandType.InlineNone:break;case OperandType.ShortInlineI:case OperandType.ShortInlineVar:case OperandType.ShortInlineBrTarget:i+=1;break;case OperandType.InlineVar:i+=2;break;case OperandType.InlineI:case OperandType.InlineBrTarget:case OperandType.ShortInlineR:i+=4;break;case OperandType.InlineI8:case OperandType.InlineR:i+=8;break;case OperandType.InlineSwitch:var n=BitConverter.ToInt32(b,i);i+=4+4*n;break;case OperandType.InlineField:case OperandType.InlineMethod:case OperandType.InlineTok:case OperandType.InlineType:case OperandType.InlineString:case OperandType.InlineSig:var token=BitConverter.ToInt32(b,i);i+=4;if(op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)refs.Add(Resolve(m,token));break;}}return refs;}
 static string Resolve(MethodInfo m,int token){try{var mem=m.Module.ResolveMember(token,m.DeclaringType?.IsGenericType==true?m.DeclaringType.GetGenericArguments():Type.EmptyTypes,m.IsGenericMethod?m.GetGenericArguments():Type.EmptyTypes);return mem==null?$"token:{token:X8}":(mem.DeclaringType?.FullName??mem.Module.Name)+"::"+mem.Name+" "+mem;}catch{return $"token:{token:X8}";}}
 static Type[] Safe(Assembly a){try{return a.GetTypes();}catch(ReflectionTypeLoadException e){return e.Types.Where(x=>x!=null).Cast<Type>().ToArray();}}static string Access(MethodBase? m)=>m?.IsPublic==true?"public":"nonpublic";static string Sig(MethodInfo m)=>(m.DeclaringType?.FullName??"<global>")+"::"+m.Name+"("+string.Join(",",m.GetParameters().Select(p=>p.ParameterType.Name))+")";static void Append(string s)=>File.AppendAllText(Path.Combine(output,"sceneitem-time.txt"),s+Environment.NewLine,new UTF8Encoding(false));
}
