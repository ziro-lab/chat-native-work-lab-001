using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4MapValidationPerfProbe;
public sealed class PluginEntry:ILocalizePlugin{public string Name=>"Chat Native Work Lab — Recording Archive Map Validation Perf";public void SetCulture(CultureInfo c)=>Probe.Schedule();}
internal static class Probe
{
 static bool scheduled;static string output="";
 public static void Schedule(){var d=Environment.GetEnvironmentVariable("CNWL_YMM4_MAP_PERF_DIR");if(scheduled||string.IsNullOrWhiteSpace(d))return;scheduled=true;output=Path.GetFullPath(d);Directory.CreateDirectory(output);Application.Current.Dispatcher.BeginInvoke(new Action(Run),DispatcherPriority.ApplicationIdle);}
 static void Run()
 {
  try
  {
   var item=new VideoItem{Length=36000,ContentOffset=TimeSpan.FromSeconds(10)};item.PlaybackRate2.SetFirstValue(100);item.PlaybackRate2.SetAnimationParameters(item.Length,60);
   var prop=typeof(VideoItem).GetProperty("PlaybackRateMap",BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("PlaybackRateMap missing");var map=prop.GetValue(item)??throw new Exception("map null");
   Type[] sig=[typeof(TimeSpan),typeof(int),typeof(int),typeof(TimeSpan),typeof(TimeSpan),typeof(bool).MakeByRefType()];
   double RunUncached(int n)
   {
    var sw=Stopwatch.StartNew();long ticks=0;
    for(int i=0;i<n;i++){var m=map.GetType().GetMethod("GetSourceTime",sig)??throw new Exception("GetSourceTime missing");object[] a=[TimeSpan.FromSeconds((i%36000)/60d),36000,60,TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(1000),false];var v=m.Invoke(map,a);if(v is TimeSpan t)ticks+=t.Ticks;}
    sw.Stop();GC.KeepAlive(ticks);return sw.Elapsed.TotalSeconds;
   }
   var cached=map.GetType().GetMethod("GetSourceTime",sig)??throw new Exception("GetSourceTime missing");
   double RunCached(int n)
   {
    var sw=Stopwatch.StartNew();long ticks=0;
    for(int i=0;i<n;i++){object[] a=[TimeSpan.FromSeconds((i%36000)/60d),36000,60,TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(1000),false];var v=cached.Invoke(map,a);if(v is TimeSpan t)ticks+=t.Ticks;}
    sw.Stop();GC.KeepAlive(ticks);return sw.Elapsed.TotalSeconds;
   }
   RunCached(1000);RunUncached(1000);
   var u36=RunUncached(36000);var u360=RunUncached(360000);var c360=RunCached(360000);
   double cps(double s,int n)=>n/Math.Max(s,1e-9);
   var estimated72m=7200000d/cps(u360,360000);
   Append($"uncached_36000_seconds={u36:R}");
   Append($"uncached_360000_seconds={u360:R}");
   Append($"cached_360000_seconds={c360:R}");
   Append($"uncached_calls_per_second={cps(u360,360000):R}");
   Append($"cached_calls_per_second={cps(c360,360000):R}");
   Append($"estimated_7_2m_uncached_seconds={estimated72m:R}");
   File.WriteAllLines(Path.Combine(output,"result.txt"),[
    "status=PASS_MAP_VALIDATION_PERF_OBSERVATION",
    "uncached_36000_seconds="+u36.ToString("R",CultureInfo.InvariantCulture),
    "uncached_360000_seconds="+u360.ToString("R",CultureInfo.InvariantCulture),
    "cached_360000_seconds="+c360.ToString("R",CultureInfo.InvariantCulture),
    "estimated_7200000_uncached_seconds="+estimated72m.ToString("R",CultureInfo.InvariantCulture)
   ],new UTF8Encoding(false));
  }catch(Exception ex){Append("ERROR "+ex);File.WriteAllLines(Path.Combine(output,"result.txt"),["status=FAIL_EXCEPTION","detail="+ex.GetBaseException().Message],new UTF8Encoding(false));}
 }
 static void Append(string s)=>File.AppendAllText(Path.Combine(output,"perf.txt"),s+Environment.NewLine,new UTF8Encoding(false));
}
