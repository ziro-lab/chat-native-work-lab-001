using System.Globalization;using System.IO;using System.Reflection;using System.Text;using System.Windows;using System.Windows.Threading;using YukkuriMovieMaker.Plugin;using YukkuriMovieMaker.Project;using YukkuriMovieMaker.Project.Items;
namespace Ymm4RelativeLiveOpenProbe;
public sealed class PluginEntry:ILocalizePlugin{public string Name=>"Chat Native Work Lab — Recording Archive Relative Live Open";public void SetCulture(CultureInfo c)=>Probe.Schedule();}
internal static class Probe
{
 static bool scheduled;static string output="",fixture="";
 public static void Schedule(){var d=Environment.GetEnvironmentVariable("CNWL_YMM4_RELATIVE_LIVE_DIR");var f=Environment.GetEnvironmentVariable("CNWL_YMM4_RELATIVE_LIVE_FIXTURE");if(scheduled||string.IsNullOrWhiteSpace(d)||string.IsNullOrWhiteSpace(f))return;scheduled=true;output=Path.GetFullPath(d);fixture=Path.GetFullPath(f);Directory.CreateDirectory(output);Application.Current.Dispatcher.BeginInvoke(new Action(Start),DispatcherPriority.ApplicationIdle);}
 static void Start(){var ticks=0;var created=false;var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};timer.Tick+=async(_,_)=>{try{foreach(Window w in Application.Current.Windows){var root=w.DataContext;if(root?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;var active=root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);if(active==null&&!created){created=true;root.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(root,null);break;}var model=root.GetType().GetField("model",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(root);var timeline=active?.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline;if(model==null||timeline==null)continue;timer.Stop();await RunAsync(root,model,timeline);return;}if(++ticks>120)throw new TimeoutException();}catch(Exception ex){timer.Stop();Append("ERROR "+ex);Result("FAIL_EXCEPTION",ex.GetBaseException().Message);}};timer.Start();}
 static async Task RunAsync(object root,object model,Timeline timeline)
 {
  var mt=model.GetType();var save=mt.GetMethod("SaveProject",[typeof(string)])??throw new MissingMethodException("SaveProject");
  var a=Path.Combine(output,"A");var b=Path.Combine(output,"B");Directory.CreateDirectory(Path.Combine(a,"recordings"));File.Copy(fixture,Path.Combine(a,"recordings","clip.mp4"),true);
  var item=new VideoItem{FilePath=Path.Combine("recordings","clip.mp4"),Frame=0,Length=120,Layer=10,Remark="CNWL_RELATIVE_LIVE"};if(!timeline.TryAddItems([item],0,10))throw new Exception("add failed");
  var pa=Path.Combine(a,"project.ymmp");save.Invoke(model,[pa]);await Idle();CopyDir(a,b);File.Delete(Path.Combine(a,"recordings","clip.mp4"));var pb=Path.Combine(b,"project.ymmp");
  var open=root.GetType().GetMethod("OpenProject",[typeof(string)])??throw new MissingMethodException("MainViewModel.OpenProject(string)");
  var ret=open.Invoke(root,[pb]);if(ret is Task task)await task;
  for(int i=0;i<60;i++){await Task.Delay(100);await Idle();var path=mt.GetProperty("ProjectFilePath")?.GetValue(model) as string??"";if(Same(path,pb))break;}
  var livePath=mt.GetProperty("ProjectFilePath")?.GetValue(model) as string??"";
  var active=root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root)??throw new Exception("active timeline missing after open");
  var loadedTimeline=active.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline??throw new Exception("timeline missing after open");
  var loaded=loadedTimeline.Items.OfType<VideoItem>().Single(x=>x.Remark=="CNWL_RELATIVE_LIVE");await Idle();await Idle();
  Append("LIVE_PROJECT_PATH="+livePath);Append("LIVE_VIDEO_FILEPATH="+loaded.FilePath);Append("LIVE_CONTENT_LENGTH="+loaded.ContentLength.TotalSeconds.ToString("R",CultureInfo.InvariantCulture));Append("B_MEDIA_EXISTS="+File.Exists(Path.Combine(b,"recordings","clip.mp4")));
  File.WriteAllLines(Path.Combine(output,"result.txt"),["status=PASS_RELATIVE_LIVE_OPEN_OBSERVATION","live_project_points_to_B="+Same(livePath,pb),"relative_preserved="+(!Path.IsPathFullyQualified(loaded.FilePath ?? "")),"content_length_positive_after_live_open="+(loaded.ContentLength>TimeSpan.Zero)],new UTF8Encoding(false));
 }
 static void CopyDir(string s,string d){Directory.CreateDirectory(d);foreach(var dir in Directory.GetDirectories(s,"*",SearchOption.AllDirectories))Directory.CreateDirectory(dir.Replace(s,d,StringComparison.OrdinalIgnoreCase));foreach(var file in Directory.GetFiles(s,"*",SearchOption.AllDirectories)){var t=file.Replace(s,d,StringComparison.OrdinalIgnoreCase);Directory.CreateDirectory(Path.GetDirectoryName(t)!);File.Copy(file,t,true);}}
 static bool Same(string a,string b){if(string.IsNullOrWhiteSpace(a)||string.IsNullOrWhiteSpace(b))return false;return string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);}
 static async Task Idle()=>await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle).Task;
 static void Append(string s)=>File.AppendAllText(Path.Combine(output,"relative-live.txt"),s+Environment.NewLine,new UTF8Encoding(false));static void Result(string s,string d)=>File.WriteAllLines(Path.Combine(output,"result.txt"),["status="+s,"detail="+d],new UTF8Encoding(false));
}
