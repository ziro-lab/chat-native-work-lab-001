using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4EditRebindingProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VideoItem Edit Rebinding Bootstrap";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}
public sealed class EditRebindingTool : IToolPlugin
{
    public string Name => "CNWL VideoItem Edit Rebinding";
    public Type ViewModelType => typeof(EditRebindingModel);
    public Type ViewType => typeof(EditRebindingView);
    public bool AllowMultipleInstances => false;
}
public sealed class EditRebindingView : UserControl
{
    public EditRebindingView()=>Content=new TextBlock{Text="CNWL edit rebinding probe"};
}
public sealed class EditRebindingModel : ITimelineToolViewModel, IToolViewModel, IDisposable
{
    public string Title=>"CNWL VideoItem Edit Rebinding";
    public bool CanSuspend=>false;
    public void SetTimelineToolInfo(TimelineToolInfo info)=>Probe.Info=info;
    public ToolState SaveState()=>new(){Title=Title};
    public void LoadState(ToolState stateData){}
    public void Dispose(){}
    public event PropertyChangedEventHandler? PropertyChanged { add{} remove{} }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add{} remove{} }
}

internal static class Probe
{
    internal static TimelineToolInfo? Info;
    private static bool scheduled;
    private static string output="";
    private static readonly List<object> requirements=[];

    private sealed record ItemState(
        string Remark, bool SameReference, int Frame, int Length, int Layer,
        double OffsetSeconds, double Rate, string FilePath);
    private sealed record UndoState(
        bool UndoRestoredOriginalReference, bool RedoReusedSplitLeftReference, bool RedoReusedSplitRightReference,
        ItemState[] AfterSplit, ItemState[] AfterUndo, ItemState[] AfterRedo);

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_EDIT_REBIND_OUTPUT");
        if(scheduled||string.IsNullOrWhiteSpace(dir))return;
        scheduled=true;output=Path.GetFullPath(dir);Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start),DispatcherPriority.ApplicationIdle);
    }

    private static void Start()
    {
        int ticks=0;bool created=false,opened=false;
        var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(300)};
        timer.Tick+=async(_,_)=>{
            try
            {
                ticks++;
                var main=Application.Current.Windows.Cast<Window>().Select(w=>w.DataContext)
                    .FirstOrDefault(x=>x?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel");
                if(main==null)return;
                var active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                if(active==null)
                {
                    if(!created){created=true;main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);}
                    return;
                }
                if(!opened)opened=OpenTool(main);
                var timeline=Info?.Timeline??FindTimelineGraph(main,active);
                if(timeline!=null)
                {
                    timer.Stop();
                    await RunAsync(main,active,timeline);
                    Write("PASS_EDIT_REBINDING",null);
                    return;
                }
                if(ticks>180)throw new TimeoutException("Timeline could not be resolved.");
            }
            catch(Exception ex){timer.Stop();Write("FAIL_EDIT_REBINDING",ex.ToString());}
        };
        timer.Start();
    }

    private static async Task RunAsync(object main,object active,Timeline timeline)
    {
        int fps=timeline.VideoInfo.FPS;
        string media=Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_EDIT_REBIND_MEDIA")??throw new InvalidOperationException("media missing"));
        Check("fixture_exists",File.Exists(media));
        Check("fps_positive",fps>0);

        var split=typeof(Timeline).GetMethods(BindingFlags.Instance|BindingFlags.Public)
            .Single(m=>m.Name=="SplitSelectedAndGroupedItems"&&m.GetParameters().Length==2);
        object splitNone=Enum.Parse(split.GetParameters()[1].ParameterType,"None");
        var trim=typeof(Timeline).GetMethod("ChangeSelectedAndGroupedItemsLength",BindingFlags.Instance|BindingFlags.Public)
            ??throw new MissingMethodException("ChangeSelectedAndGroupedItemsLength");
        object neighborNone=Enum.Parse(trim.GetParameters()[2].ParameterType,"None");
        var move=typeof(Timeline).GetMethod("MoveSelectedAndGroupedItems",BindingFlags.Instance|BindingFlags.Public)
            ??throw new MissingMethodException("MoveSelectedAndGroupedItems");
        var copy=typeof(Timeline).GetMethod("CopySelectedAndGroupedItems",BindingFlags.Instance|BindingFlags.Public)
            ??throw new MissingMethodException("CopySelectedAndGroupedItems");
        var paste=typeof(Timeline).GetMethod("PasteCopiedItemsAsync",BindingFlags.Instance|BindingFlags.Public)
            ??throw new MissingMethodException("PasteCopiedItemsAsync");
        Check("public_edit_surfaces",split.IsPublic&&trim.IsPublic&&move.IsPublic&&copy.IsPublic&&paste.IsPublic);

        // Head trim: dragging the head inward by +120 frames.
        var head=NewItem(media,300,600,10,5,"HEAD_TRIM",fps);
        Check("head_trim_insert",timeline.TryAddItems([head],head.Frame,head.Layer));
        timeline.SelectedItems=ImmutableList.Create<IItem>(head);
        trim.Invoke(timeline,[120,true,neighborNone]);
        Check("head_trim_same_reference",timeline.Items.Any(x=>ReferenceEquals(x,head)));
        Check("head_trim_timeline",head.Frame==420&&head.Length==480);
        Check("head_trim_source",Near(head.ContentOffset.TotalSeconds,7)&&SamePath(head.FilePath,media)&&Near(Rate(head,fps),100));

        // Tail trim: dragging the tail inward by -120 frames.
        var tail=NewItem(media,1200,600,11,5,"TAIL_TRIM",fps);
        Check("tail_trim_insert",timeline.TryAddItems([tail],tail.Frame,tail.Layer));
        timeline.SelectedItems=ImmutableList.Create<IItem>(tail);
        trim.Invoke(timeline,[-120,false,neighborNone]);
        Check("tail_trim_same_reference",timeline.Items.Any(x=>ReferenceEquals(x,tail)));
        Check("tail_trim_timeline",tail.Frame==1200&&tail.Length==480);
        Check("tail_trim_source",Near(tail.ContentOffset.TotalSeconds,5)&&SamePath(tail.FilePath,media)&&Near(Rate(tail,fps),100));

        // Split, then move the right piece. Source coordinates must stay unchanged.
        var moveSource=NewItem(media,2200,600,12,5,"MOVE_AFTER_SPLIT",fps);
        Check("move_insert",timeline.TryAddItems([moveSource],moveSource.Frame,moveSource.Layer));
        timeline.SelectedItems=ImmutableList.Create<IItem>(moveSource);
        int splitFrame=moveSource.Frame+240;
        split.Invoke(timeline,[splitFrame,splitNone]);
        var movePieces=FindByRemark(timeline,"MOVE_AFTER_SPLIT").OrderBy(x=>x.Frame).ToArray();
        Check("move_split_two",movePieces.Length==2);
        var right=movePieces[1];
        var rightBefore=State(right,right,fps);
        timeline.SelectedItems=ImmutableList.Create<IItem>(right);
        move.Invoke(timeline,[600,0,0]);
        Check("move_same_reference",timeline.Items.Any(x=>ReferenceEquals(x,right)));
        Check("move_timeline_changed",right.Frame==rightBefore.Frame+600&&right.Layer==rightBefore.Layer);
        Check("move_source_unchanged",right.Length==rightBefore.Length&&Near(right.ContentOffset.TotalSeconds,rightBefore.OffsetSeconds)
            &&SamePath(right.FilePath,rightBefore.FilePath)&&Near(Rate(right,fps),rightBefore.Rate));

        // Real host copy/paste: two distinct occurrences can represent the exact same source range.
        var original=NewItem(media,4000,300,20,8,"DUPLICATE_SOURCE",fps);
        Check("duplicate_insert",timeline.TryAddItems([original],original.Frame,original.Layer));
        timeline.SelectedItems=ImmutableList.Create<IItem>(original);
        copy.Invoke(timeline,null);
        await Task.Delay(150);
        await (Task)(paste.Invoke(timeline,[5000,21])??throw new InvalidOperationException("Paste task missing"));
        await Task.Delay(200);
        var dup=FindByRemark(timeline,"DUPLICATE_SOURCE")
            .Where(v=>SamePath(v.FilePath,media)&&Near(v.ContentOffset.TotalSeconds,8)&&v.Length==300&&Near(Rate(v,fps),100))
            .OrderBy(v=>v.Frame).ToArray();
        Check("duplicate_two_occurrences",dup.Length>=2&&dup.Distinct(ReferenceEqualityComparer.Instance).Count()>=2);
        Check("duplicate_same_source_range",dup.Take(2).All(v=>Near(v.ContentOffset.TotalSeconds,8)&&v.Length==300&&SamePath(v.FilePath,media)));
        Check("duplicate_different_timeline_positions",dup.Select(v=>v.Frame).Distinct().Count()>=2);

        // Undo/Redo of a real split. Observe semantic restoration and object-reference churn.
        var undoManager=FindUndoRedoManager(main,active,timeline)
            ??throw new MissingMemberException("UndoRedoManager instance not found.");
        var undoAsync=undoManager.GetType().GetMethod("UndoAsync",BindingFlags.Instance|BindingFlags.Public)
            ??throw new MissingMethodException("UndoRedoManager.UndoAsync");
        var redoAsync=undoManager.GetType().GetMethod("RedoAsync",BindingFlags.Instance|BindingFlags.Public)
            ??throw new MissingMethodException("UndoRedoManager.RedoAsync");
        var isUndoable=undoManager.GetType().GetProperty("IsUndoable")??throw new MissingMemberException("IsUndoable");
        var isRedoable=undoManager.GetType().GetProperty("IsRedoable")??throw new MissingMemberException("IsRedoable");
        Check("undo_manager_surface",undoAsync.IsPublic&&redoAsync.IsPublic);

        var undoSource=NewItem(media,6500,600,30,5,"UNDO_SPLIT",fps);
        Check("undo_insert",timeline.TryAddItems([undoSource],undoSource.Frame,undoSource.Layer));
        timeline.SelectedItems=ImmutableList.Create<IItem>(undoSource);
        split.Invoke(timeline,[undoSource.Frame+240,splitNone]);
        var afterSplit=FindByRemark(timeline,"UNDO_SPLIT").OrderBy(x=>x.Frame).ToArray();
        Check("undo_split_created",afterSplit.Length==2&&!afterSplit.Any(x=>ReferenceEquals(x,undoSource)));
        Check("undo_available_after_split",(bool)(isUndoable.GetValue(undoManager)??false));
        var splitLeft=afterSplit[0];var splitRight=afterSplit[1];
        await (Task)(undoAsync.Invoke(undoManager,null)??throw new InvalidOperationException("Undo task missing"));
        await Task.Delay(150);
        var afterUndo=FindByRemark(timeline,"UNDO_SPLIT").OrderBy(x=>x.Frame).ToArray();
        Check("undo_restores_one_piece",afterUndo.Length==1&&afterUndo[0].Frame==6500&&afterUndo[0].Length==600&&Near(afterUndo[0].ContentOffset.TotalSeconds,5));
        Check("redo_available_after_undo",(bool)(isRedoable.GetValue(undoManager)??false));
        bool undoOriginalRef=ReferenceEquals(afterUndo[0],undoSource);
        await (Task)(redoAsync.Invoke(undoManager,null)??throw new InvalidOperationException("Redo task missing"));
        await Task.Delay(150);
        var afterRedo=FindByRemark(timeline,"UNDO_SPLIT").OrderBy(x=>x.Frame).ToArray();
        Check("redo_restores_two_pieces",afterRedo.Length==2&&afterRedo[0].Frame==6500&&afterRedo[0].Length==240
            &&afterRedo[1].Frame==6740&&afterRedo[1].Length==360&&Near(afterRedo[0].ContentOffset.TotalSeconds,5)&&Near(afterRedo[1].ContentOffset.TotalSeconds,9));

        var undoObservation=new UndoState(
            undoOriginalRef,
            afterRedo.Length>0&&ReferenceEquals(afterRedo[0],splitLeft),
            afterRedo.Length>1&&ReferenceEquals(afterRedo[1],splitRight),
            afterSplit.Select(v=>State(v,undoSource,fps)).ToArray(),
            afterUndo.Select(v=>State(v,undoSource,fps)).ToArray(),
            afterRedo.Select(v=>State(v,undoSource,fps)).ToArray());
        File.WriteAllText(Path.Combine(output,"behavior.json"),JsonSerializer.Serialize(new
        {
            headTrim=State(head,head,fps),
            tailTrim=State(tail,tail,fps),
            moveBefore=rightBefore,
            moveAfter=State(right,right,fps),
            duplicate=dup.Select(v=>State(v,original,fps)).ToArray(),
            undoRedo=undoObservation
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    private static VideoItem NewItem(string path,int frame,int length,int layer,double offset,string remark,int fps)
    {
        var v=new VideoItem{FilePath=path,Frame=frame,Length=length,Layer=layer,ContentOffset=TimeSpan.FromSeconds(offset),Remark=remark};
        v.PlaybackRate2.SetFirstValue(100);v.PlaybackRate2.SetAnimationParameters(length,fps);return v;
    }
    private static VideoItem[] FindByRemark(Timeline t,string remark)=>t.Items.OfType<VideoItem>().Where(v=>v.Remark==remark).ToArray();
    private static double Rate(VideoItem v,int fps)=>v.PlaybackRate2.GetValue(0,v.Length,fps);
    private static bool SamePath(string? a,string b)=>a!=null&&string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
    private static bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;
    private static ItemState State(VideoItem v,VideoItem reference,int fps)=>new(
        v.Remark??"",ReferenceEquals(v,reference),v.Frame,v.Length,v.Layer,v.ContentOffset.TotalSeconds,Rate(v,fps),Path.GetFullPath(v.FilePath??""));

    private static object? FindUndoRedoManager(params object[] roots)
    {
        var visited=new HashSet<object>(ReferenceEqualityComparer.Instance);
        object? Walk(object? obj,int depth)
        {
            if(obj==null||depth>7||!visited.Add(obj))return null;
            var type=obj.GetType();
            if(type.FullName=="YukkuriMovieMaker.UndoRedo.UndoRedoManager")return obj;
            if(type.IsPrimitive||obj is string||obj is Type||obj is MemberInfo)return null;
            string asm=type.Assembly.GetName().Name??"";
            bool allowed=asm.StartsWith("YukkuriMovieMaker",StringComparison.Ordinal)
                ||asm.StartsWith("Reactive",StringComparison.Ordinal)
                ||asm=="System.Collections.Immutable";
            if(!allowed)return null;
            foreach(var f in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                object? value=null;try{value=f.GetValue(obj);}catch{}
                if(value==null)continue;
                var found=Walk(value,depth+1);if(found!=null)return found;
            }
            foreach(var p in type.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                if(p.GetIndexParameters().Length!=0||p.Name is "Items" or "SelectedItems" or "Characters")continue;
                object? value=null;try{value=p.GetValue(obj);}catch{}
                if(value==null)continue;
                var found=Walk(value,depth+1);if(found!=null)return found;
            }
            return null;
        }
        foreach(var root in roots){var found=Walk(root,0);if(found!=null)return found;}
        return null;
    }

    private static Timeline? FindTimelineGraph(object main,object active)
    {
        var visited=new HashSet<object>(ReferenceEqualityComparer.Instance);
        Timeline? Walk(object? obj,int depth)
        {
            if(obj==null||depth>4||!visited.Add(obj))return null;
            if(obj is Timeline found)return found;
            var type=obj.GetType();
            if(type.Assembly.GetName().Name?.StartsWith("YukkuriMovieMaker",StringComparison.Ordinal)!=true
                &&!type.FullName!.StartsWith("Reactive.Bindings",StringComparison.Ordinal))return null;
            foreach(var p in type.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                if(p.GetIndexParameters().Length!=0||p.Name is "Items" or "SelectedItems" or "Characters")continue;
                object? value=null;try{value=p.GetValue(obj);}catch{}
                if(value is Timeline t)return t;
                if(value!=null&&depth<4)
                {var nested=Walk(value,depth+1);if(nested!=null)return nested;}
            }
            foreach(var f in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                object? value=null;try{value=f.GetValue(obj);}catch{}
                if(value is Timeline t)return t;
                if(value!=null&&depth<4)
                {var nested=Walk(value,depth+1);if(nested!=null)return nested;}
            }
            return null;
        }
        return Walk(active,0)??Walk(main,0);
    }

    private static bool OpenTool(object main)
    {
        var roots=main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) as IEnumerable;
        if(roots==null)return false;
        bool Visit(object item,int depth)
        {
            if(depth>6)return false;
            var type=item.GetType();var header=type.GetProperty("Header")?.GetValue(item)?.ToString()??"";
            if(header.Contains("CNWL VideoItem Edit Rebinding",StringComparison.Ordinal)
                &&type.GetProperty("Command")?.GetValue(item) is ICommand command)
            {
                var p=type.GetProperty("CommandParameter")?.GetValue(item);
                if(command.CanExecute(p)){command.Execute(p);return true;}
            }
            foreach(var name in new[]{"Items","Children","MenuItems"})
                if(type.GetProperty(name)?.GetValue(item) is IEnumerable children)
                    foreach(var child in children)if(child!=null&&Visit(child,depth+1))return true;
            return false;
        }
        foreach(var root in roots)if(root!=null&&Visit(root,0))return true;
        return false;
    }

    private static void Check(string id,bool passed)
    {
        requirements.Add(new{id,passed});
        File.AppendAllText(Path.Combine(output,"assertions.txt"),$"ASSERT {(passed?"PASS":"FAIL")} {id}\n");
        if(!passed)throw new InvalidOperationException("Assertion failed: "+id);
    }

    private static void Write(string status,string? error)
    {
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{
            schema="cnwl.edit-rebinding.v1",status,host="4.56.1.0 Lite",sourceHead=Environment.GetEnvironmentVariable("GITHUB_SHA"),requirements,error
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
