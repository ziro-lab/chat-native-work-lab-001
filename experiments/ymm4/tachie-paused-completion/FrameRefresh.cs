namespace Lab.TachiePausedNotice;

internal interface IFrameAccess
{
    int Frame { get; set; }
    int LastFrame { get; }
    object Scope { get; }
    long NavigationVersion { get; }
    bool CanAct { get; }
}
internal sealed record FrameActionResult(string Status,int? Original,int? Target,int? Final,int Writes,string? Error);
internal static class FrameRefresh
{
    // Called once on the owner UI thread AFTER preparation, so the original is the current position at completion.
    internal static FrameActionResult Same(IFrameAccess access)
    {
        if(!access.CanAct)return new("SKIPPED_STATE",null,null,null,0,null);
        var original=access.Frame;var scope=access.Scope;var version=access.NavigationVersion;
        access.Frame=original;
        return new(access.CanAct&&ReferenceEquals(scope,access.Scope)&&access.NavigationVersion==version&&access.Frame==original?"SAME_ASSIGNED":"INTERVENING_STATE",original,original,access.Frame,1,null);
    }
    internal static FrameActionResult Nudge(IFrameAccess access,Action? afterMove=null)
    {
        if(!access.CanAct)return new("SKIPPED_STATE",null,null,null,0,null);
        var original=access.Frame;var scope=access.Scope;var version=access.NavigationVersion;
        if(original<0||original>access.LastFrame||access.LastFrame<1)return new("SKIPPED_BOUNDARY",original,null,original,0,null);
        var target=original<access.LastFrame?original+1:original-1;var writes=0;string? error=null;var status="MOVE_NOT_APPLIED";
        try
        {
            writes++;access.Frame=target;
            if(access.Frame!=target)return new(status,original,target,access.Frame,writes,null);
            status="MOVED";afterMove?.Invoke();
        }
        catch(Exception e){error=e.GetType().Name+": "+e.Message;status="MOVE_ERROR";}
        finally
        {
            // Never restore a captured position across a new Scene, play start, Dispose or reentrant navigation.
            if(ReferenceEquals(scope,access.Scope)&&access.CanAct&&access.Frame==target&&access.NavigationVersion==version+1)
            {
                try{writes++;access.Frame=original;status=access.Frame==original?error==null?"RESTORED":"ERROR_RESTORED":"RESTORE_NOT_APPLIED";}
                catch(Exception e){error=(error??"")+"; restore "+e.GetType().Name;status="RESTORE_FAILED";}
            }
            else if(status is "MOVED" or "MOVE_ERROR")status="SKIPPED_STALE_RESTORE";
        }
        return new(status,original,target,access.Frame,writes,error);
    }
}
