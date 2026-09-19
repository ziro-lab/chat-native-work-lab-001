using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4LoopSurfaceProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name=>"Chat Native Work Lab — Recording Archive Loop Surface";
    public void SetCulture(CultureInfo cultureInfo)=>Probe.Schedule();
}
internal static class Probe
{
    static bool scheduled; static string output="";
    static readonly Dictionary<ushort,OpCode> Ops=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>unchecked((ushort)o.Value));
    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_LOOP_SURFACE_DIR");
        if(scheduled||string.IsNullOrWhiteSpace(dir))return; scheduled=true; output=Path.GetFullPath(dir);Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run),DispatcherPriority.ApplicationIdle);
    }
    static void Run()
    {
        try
        {
            var video=new VideoItem{Length=600,ContentOffset=TimeSpan.Zero};
            video.PlaybackRate2.SetFirstValue(100); video.PlaybackRate2.SetAnimationParameters(video.Length,60);
            var mapProp=typeof(VideoItem).GetProperty("PlaybackRateMap",BindingFlags.Instance|BindingFlags.NonPublic)??throw new MissingMemberException("PlaybackRateMap");
            var getSource=(object map)=>map.GetType().GetMethod("GetSourceTime",[typeof(TimeSpan),typeof(int),typeof(int),typeof(TimeSpan),typeof(TimeSpan)])??throw new MissingMethodException("GetSourceTime");
            double[] Sample(bool loop)
            {
                video.IsLooped=loop; var map=mapProp.GetValue(video)??throw new Exception("map null"); var m=getSource(map);
                return new[]{0d,1d,2d,3d,4d,5d,8d}.Select(t=>((TimeSpan)m.Invoke(map,[TimeSpan.FromSeconds(t),video.Length,60,TimeSpan.Zero,TimeSpan.FromSeconds(3)])!).TotalSeconds).ToArray();
            }
            var off=Sample(false); var on=Sample(true);
            Append("MAP_LOOP_FALSE="+string.Join(",",off.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))));
            Append("MAP_LOOP_TRUE="+string.Join(",",on.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))));
            var mapSame=off.Zip(on).All(x=>Math.Abs(x.First-x.Second)<1e-9);
            Append("PLAYBACK_RATE_MAP_IDENTICAL_WITH_LOOP_TOGGLE="+mapSame);

            var refs=new List<(MethodInfo Method,List<string> Refs,List<string> Lines)>();
            foreach(var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name?.StartsWith("YukkuriMovieMaker",StringComparison.Ordinal)==true))
            foreach(var type in SafeTypes(asm))
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
            {
                if(method.IsAbstract||method.ContainsGenericParameters)continue;
                var d=Decode(method); if(d==null)continue;
                if(d.Value.Refs.Any(r=>r.Contains("get_IsLooped",StringComparison.Ordinal)||r.Contains("isLooped",StringComparison.OrdinalIgnoreCase)))
                    refs.Add((method,d.Value.Refs,d.Value.Lines));
            }
            foreach(var hit in refs.OrderBy(x=>x.Method.DeclaringType?.FullName).ThenBy(x=>x.Method.Name))
            {
                Append("=== LOOP HIT "+Sig(hit.Method)+" ===");
                foreach(var r in hit.Refs.Distinct()) if(r.Contains("Loop",StringComparison.OrdinalIgnoreCase)||r.Contains("Content",StringComparison.OrdinalIgnoreCase)||r.Contains("PlaybackRate",StringComparison.OrdinalIgnoreCase)) Append("REF "+r);
                foreach(var line in hit.Lines) Append("IL "+line);
            }
            var videoHits=refs.Where(x=>x.Method.DeclaringType?.FullName?.Contains("Player.Video.Items.VideoSource",StringComparison.Ordinal)==true).ToArray();
            var videoSource=AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).FirstOrDefault(t=>t.FullName=="YukkuriMovieMaker.Player.Video.Items.VideoSource");
            if(videoSource!=null)
                foreach(var m in videoSource.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).Where(m=>m.Name.Contains("SourceTime",StringComparison.Ordinal)||m.Name=="Update"))
                    Append("VIDEOSOURCE_METHOD "+Sig(m));

            Assert(mapSame,"PlaybackRateMap mapping itself is unchanged by IsLooped toggle");
            Assert(refs.Count>0,"native YMM4 contains IsLooped references");
            Append("VIDEO_SOURCE_LOOP_HITS="+videoHits.Length);
            File.WriteAllLines(Path.Combine(output,"result.txt"),[
                "status=PASS_LOOP_SURFACE_OBSERVATION",
                "map_identical_with_loop_toggle="+mapSame,
                "islooped_il_hit_count="+refs.Count,
                "video_source_islooped_hit_count="+videoHits.Length
            ],new UTF8Encoding(false));
        }catch(Exception ex){Append("ERROR "+ex);File.WriteAllLines(Path.Combine(output,"result.txt"),["status=FAIL_EXCEPTION","detail="+ex.GetBaseException().Message],new UTF8Encoding(false));}
    }
    static (List<string> Lines,List<string> Refs)? Decode(MethodInfo method)
    {
        byte[]? b;try{b=method.GetMethodBody()?.GetILAsByteArray();}catch{return null;} if(b==null)return null;
        var lines=new List<string>();var refs=new List<string>();int i=0;
        while(i<b.Length)
        {
            int off=i;ushort code=b[i++];if(code==0xFE){if(i>=b.Length)break;code=(ushort)(0xFE00|b[i++]);}
            if(!Ops.TryGetValue(code,out var op))break;string operand="";
            switch(op.OperandType)
            {
                case OperandType.InlineNone:break;
                case OperandType.ShortInlineI:case OperandType.ShortInlineVar:case OperandType.ShortInlineBrTarget:operand=b[i].ToString();i++;break;
                case OperandType.InlineVar:operand=BitConverter.ToUInt16(b,i).ToString();i+=2;break;
                case OperandType.InlineI:case OperandType.InlineBrTarget:operand=BitConverter.ToInt32(b,i).ToString();i+=4;break;
                case OperandType.ShortInlineR:operand=BitConverter.ToSingle(b,i).ToString("R",CultureInfo.InvariantCulture);i+=4;break;
                case OperandType.InlineI8:operand=BitConverter.ToInt64(b,i).ToString();i+=8;break;
                case OperandType.InlineR:operand=BitConverter.ToDouble(b,i).ToString("R",CultureInfo.InvariantCulture);i+=8;break;
                case OperandType.InlineSwitch:{var n=BitConverter.ToInt32(b,i);i+=4+4*n;operand="switch["+n+"]";break;}
                case OperandType.InlineField:case OperandType.InlineMethod:case OperandType.InlineTok:case OperandType.InlineType:case OperandType.InlineString:case OperandType.InlineSig:
                {
                    var token=BitConverter.ToInt32(b,i);i+=4;operand=Resolve(method,token,op.OperandType);
                    if(op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)refs.Add(operand);break;
                }
            }
            lines.Add($"{off:X4}: {op.Name}{(operand.Length>0?" "+operand:"")}");
        }
        return(lines,refs);
    }
    static string Resolve(MethodInfo m,int token,OperandType kind){try{if(kind==OperandType.InlineString)return "string:"+m.Module.ResolveString(token);if(kind==OperandType.InlineSig)return "sig";var mem=m.Module.ResolveMember(token,m.DeclaringType?.IsGenericType==true?m.DeclaringType.GetGenericArguments():Type.EmptyTypes,m.IsGenericMethod?m.GetGenericArguments():Type.EmptyTypes);return mem==null?$"token:{token:X8}":(mem.DeclaringType?.FullName??mem.Module.Name)+"::"+mem.Name+" "+mem;}catch{return $"token:{token:X8}";}}
    static Type[] SafeTypes(Assembly a){try{return a.GetTypes();}catch(ReflectionTypeLoadException e){return e.Types.Where(t=>t!=null).Cast<Type>().ToArray();}}
    static string Sig(MethodInfo m)=>(m.DeclaringType?.FullName??"<global>")+"::"+m.Name+"("+string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))+")";
    static void Assert(bool ok,string name){if(!ok)throw new Exception("ASSERT FAIL: "+name);Append("ASSERT PASS: "+name);}
    static void Append(string s)=>File.AppendAllText(Path.Combine(output,"loop-surface.txt"),s+Environment.NewLine,new UTF8Encoding(false));
}
