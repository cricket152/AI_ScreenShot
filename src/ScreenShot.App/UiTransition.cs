using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using ScreenShot.Core;

namespace ScreenShot.App;

internal sealed class UiTransition : IDisposable
{
    internal sealed record Track(DependencyObject Target,string Property,Func<double> Read,Action<double> Write,double To,bool Animate=true);
    private Storyboard? storyboard;
    private EventHandler<object>? completedHandler;
    private Track[] tracks=[];
    internal bool IsRunning=>storyboard!=null;

    internal void Start(IEnumerable<Track> values,bool enabled,Action completed,MotionProfile profile)
    {
        Stop();tracks=values.ToArray();
        foreach(var track in tracks.Where(track=>!track.Animate))track.Write(track.To);
        tracks=tracks.Where(track=>track.Animate).ToArray();
        enabled=enabled&&profile.Enabled&&tracks.Any(track=>Math.Abs(track.Read()-track.To)>0.001);
        if(!enabled){foreach(var track in tracks)track.Write(track.To);tracks=[];completed();return;}
        var next=new Storyboard();
        foreach(var track in tracks)
        {
            var animation=new DoubleAnimation{From=track.Read(),To=track.To,
                Duration=new Duration(TimeSpan.FromMilliseconds(profile.DurationMs)),
                EnableDependentAnimation=track.Property is "Height" or "Width" or "RowSpacing",
                EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            Storyboard.SetTarget(animation,track.Target);Storyboard.SetTargetProperty(animation,track.Property);next.Children.Add(animation);
        }
        completedHandler=(_,_)=>
        {
            if(!ReferenceEquals(storyboard,next))return;
            next.Completed-=completedHandler;completedHandler=null;
            var final=tracks;storyboard=null;tracks=[];next.Stop();
            foreach(var track in final)track.Write(track.To);completed();
        };
        next.Completed+=completedHandler;
        storyboard=next;
        try{next.Begin();}
        catch{next.Completed-=completedHandler;completedHandler=null;storyboard=null;var final=tracks;tracks=[];next.Stop();foreach(var track in final)track.Write(track.To);completed();}
    }

    // Stop the previous timeline at its displayed values before retargeting.
    internal void Stop()
    {
        if(storyboard==null)return;
        var displayed=tracks.Select(track=>track.Read()).ToArray();var previous=tracks;
        var animation=storyboard;animation.Completed-=completedHandler;completedHandler=null;storyboard=null;tracks=[];animation.Stop();
        for(int index=0;index<previous.Length;index++)previous[index].Write(displayed[index]);
    }

    public void Dispose()=>Stop();
}
