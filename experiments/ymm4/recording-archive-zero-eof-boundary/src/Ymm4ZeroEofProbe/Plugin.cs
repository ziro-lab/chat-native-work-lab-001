using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4ZeroEofProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Zero EOF Boundary";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output="", fixture="";

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_ZERO_EOF_DIR");
        var media=Environment.GetEnvironmentVariable("CNWL_YMM4_ZERO_EOF_FIXTURE");
        if(scheduled||string.IsNullOrWhiteSpace(dir)||string.IsNullOrWhiteSpace(media)) return;
        scheduled=true; output=Path.GetFullPath(dir); fixture=Path.GetFullPath(media); Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    private static void Start()
    {
        var ticks=0; var created=false;
        var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};
        timer.Tick += async (_,_) =>
        {
            try
            {
                foreach(Window w in Application.Current.Windows)
                {
                    var root=w.DataContext;
                    if(root?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active=root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);
                    if(active==null&&!created){created=true;root.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(root,null);break;}
                    var timeline=active?.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if(timeline==null) continue;
                    timer.Stop(); await RunAsync(timeline); return;
                }
                if(++ticks>=100) throw new TimeoutException("timeline not ready");
            }
            catch(Exception ex){timer.Stop();Append("ERROR "+ex);WriteResult("FAIL_EXCEPTION",ex.GetBaseException().Message);}
        };
        timer.Start();
    }

    private static async Task RunAsync(Timeline timeline)
    {
        var item=new VideoItem(fixture){Frame=0,Layer=10,Remark="CNWL_ZERO_EOF"};
        Assert(timeline.TryAddItems([item],0,10),"fixture VideoItem added");
        await Idle(); await Idle();
        var duration=item.ContentLength.TotalSeconds;
        Assert(duration>0,"ContentLength positive");
        var fps=timeline.VideoInfo.FPS;
        Assert(fps>0,"timeline FPS positive");
        item.Length=Math.Max(2,fps*2);
        item.PlaybackRate2.SetFirstValue(0);
        item.PlaybackRate2.SetAnimationParameters(item.Length,fps);
        var mapProp=typeof(VideoItem).GetProperty("PlaybackRateMap",BindingFlags.Instance|BindingFlags.NonPublic)
            ?? throw new MissingMemberException("PlaybackRateMap");
        var map=mapProp.GetValue(item) ?? throw new InvalidOperationException("map null");
        var method=map.GetType().GetMethod("GetSourceTime",[typeof(TimeSpan),typeof(int),typeof(int),typeof(TimeSpan),typeof(TimeSpan)])
            ?? throw new MissingMethodException("GetSourceTime");
        double Source(double offset,double itemSec)
        {
            var raw=method.Invoke(map,[TimeSpan.FromSeconds(itemSec),item.Length,fps,TimeSpan.FromSeconds(offset),item.ContentLength]);
            return raw is TimeSpan ts?ts.TotalSeconds:double.NaN;
        }

        var lastFrame=Math.Max(0,duration-1d/fps);
        foreach(var offset in new[]{lastFrame,duration})
        {
            var s0=Source(offset,0); var s1=Source(offset,1);
            Append($"CASE offset={offset:R} source0={s0:R} source1={s1:R}");
            Assert(Math.Abs(s0-s1)<1e-7,$"0% freezes source at offset={offset:R}");
        }

        var ffmpeg=FFmpegResourceLocator.GetFFmpegExePath();
        Assert(File.Exists(ffmpeg),"YMM4 bundled ffmpeg exists");
        var lastRows=FrameHashRows(ffmpeg,fixture,lastFrame);
        var eofRows=FrameHashRows(ffmpeg,fixture,duration);
        Append($"FRAMEHASH last_frame_time={lastFrame:R} rows={lastRows}");
        Append($"FRAMEHASH exact_eof_time={duration:R} rows={eofRows}");

        File.WriteAllLines(Path.Combine(output,"result.txt"),
        [
            "status=PASS_ZERO_EOF_BOUNDARY_OBSERVATION",
            "duration="+duration.ToString("R",CultureInfo.InvariantCulture),
            "last_frame_hash_rows="+lastRows,
            "exact_eof_hash_rows="+eofRows,
            "exact_eof_decodes_frame="+(eofRows>0)
        ],new UTF8Encoding(false));
    }

    private static int FrameHashRows(string ffmpeg,string path,double time)
    {
        var psi=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var a in new[]{"-v","error","-nostdin","-ss",time.ToString("0.#########",CultureInfo.InvariantCulture),"-i",path,"-map","0:v:0","-frames:v","1","-an","-f","framehash","-hash","sha256","-"}) psi.ArgumentList.Add(a);
        using var p=Process.Start(psi) ?? throw new IOException("ffmpeg start failed");
        var stdout=p.StandardOutput.ReadToEnd(); var stderr=p.StandardError.ReadToEnd(); p.WaitForExit();
        Append($"FFMPEG t={time:R} exit={p.ExitCode} stderr={stderr.Trim()}");
        return stdout.Split('\n').Select(x=>x.Trim()).Count(x=>x.Length>0&&!x.StartsWith('#'));
    }

    private static async Task Idle()=>await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle).Task;
    private static void Assert(bool ok,string name){if(!ok)throw new InvalidOperationException("ASSERT FAIL: "+name);Append("ASSERT PASS: "+name);}
    private static void Append(string s)=>File.AppendAllText(Path.Combine(output,"zero-eof.txt"),s+Environment.NewLine,new UTF8Encoding(false));
    private static void WriteResult(string status,string detail)=>File.WriteAllLines(Path.Combine(output,"result.txt"),["status="+status,"detail="+detail],new UTF8Encoding(false));
}
