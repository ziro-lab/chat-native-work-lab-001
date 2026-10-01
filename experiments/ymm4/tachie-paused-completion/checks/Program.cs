using Lab.TachiePausedNotice;
using System.Text.Json;
var records=new List<object>();var assertions=0;
void Check(bool value){assertions++;if(!value)throw new Exception("Guard assertion failed");}
void Case(string name,Action action){action();records.Add(new{name,status="PASS"});}
Case("same-value-short-circuit-model",()=>{var c=new Access();var r=FrameRefresh.Same(c);Check(r.Status=="SAME_ASSIGNED");Check(c.NavigationVersion==0&&c.Frame==0);});
Case("start-one-frame-and-return",()=>{var c=new Access();var r=FrameRefresh.Nudge(c);Check(r.Target==1&&r.Final==0&&r.Writes==2);Check(c.NavigationVersion==2&&r.Status=="RESTORED");});
Case("end-one-frame-and-return",()=>{var c=new Access{Frame=299};var r=FrameRefresh.Nudge(c);Check(r.Target==298&&r.Final==299&&r.Status=="RESTORED");});
Case("one-frame-timeline-no-out-of-bounds-write",()=>{var c=new Access{LastFrame=0};var r=FrameRefresh.Nudge(c);Check(r.Status=="SKIPPED_BOUNDARY"&&r.Writes==0&&c.Frame==0);});
Case("completion-time-current-position",()=>{var c=new Access{Frame=91};var r=FrameRefresh.Nudge(c);Check(r.Original==91&&r.Target==92&&r.Final==91);});
Case("exception-after-move-restored",()=>{var c=new Access();var r=FrameRefresh.Nudge(c,()=>throw new InvalidOperationException("injected after move"));Check(r.Status=="ERROR_RESTORED"&&c.Frame==0&&r.Writes==2);});
Case("reentrant-user-position-not-overwritten",()=>{var c=new Access();var r=FrameRefresh.Nudge(c,()=>c.Frame=77);Check(r.Status=="SKIPPED_STALE_RESTORE"&&c.Frame==77&&r.Writes==1);});
Case("user-moves-away-and-back-to-target-not-overwritten",()=>{var c=new Access();var r=FrameRefresh.Nudge(c,()=>{c.Frame=77;c.Frame=1;});Check(r.Status=="SKIPPED_STALE_RESTORE"&&c.Frame==1&&r.Writes==1);});
Case("scene-change-not-restored",()=>{var c=new Access();var r=FrameRefresh.Nudge(c,()=>c.Scope=new object());Check(r.Status=="SKIPPED_STALE_RESTORE"&&r.Writes==1);});
Case("play-start-not-restored",()=>{var c=new Access();var r=FrameRefresh.Nudge(c,()=>c.CanAct=false);Check(r.Status=="SKIPPED_STALE_RESTORE"&&r.Writes==1);});
Case("already-playing-or-disposed-no-write",()=>{var c=new Access{CanAct=false};var r=FrameRefresh.Nudge(c);Check(r.Status=="SKIPPED_STATE"&&r.Writes==0);});
Case("restore-setter-failure-reported",()=>{var c=new Access();var r=FrameRefresh.Nudge(c,()=>c.FailNextWrite=true);Check(r.Status=="RESTORE_FAILED"&&r.Final==1);});
var result=new{schema="lab.frame-refresh-guards.v1",status="PASS",cases=records.Count,assertions,evidenceClass="pure adversarial state/fault simulation; not live user/Scene/audio evidence",records};
var json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);if(args.Length>0)File.WriteAllText(args[0],json);
internal sealed class Access:IFrameAccess
{
 int frame;public int Frame{get=>frame;set{if(FailNextWrite){FailNextWrite=false;throw new IOException("injected setter failure");}if(frame!=value){frame=value;NavigationVersion++;}}}
 public int LastFrame{get;set;}=299;public object Scope{get;set;}=new();public long NavigationVersion{get;private set;}public bool CanAct{get;set;}=true;public bool FailNextWrite{get;set;}
}
