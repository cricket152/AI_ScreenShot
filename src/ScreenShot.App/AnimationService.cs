using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenShot.Core;
using Windows.UI.ViewManagement;
using Windows.Foundation;

namespace ScreenShot.App;

internal sealed class AnimationService : IDisposable
{
    private readonly UISettings system=new();
    private readonly DispatcherQueue dispatcher=DispatcherQueue.GetForCurrentThread();
    private readonly HashSet<AnimationScope> scopes=[];
    private bool disposed;
    internal PanelAppearance Appearance {get;private set;}
    internal event Action? Changed;
    internal AnimationService(PanelAppearance appearance)
    {Appearance=appearance.Normalize();system.AnimationsEnabledChanged+=SystemChanged;}
    internal MotionProfile Profile(AnimationArea area,bool feedback=false)
    {
        bool enabled;
        try{enabled=system.AnimationsEnabled;}catch{enabled=false;}
        return MotionProfile.Resolve(Appearance,area,enabled,feedback);
    }
    internal void Apply(PanelAppearance appearance)
    {
        appearance=appearance.Normalize();
        if(Appearance.EnableAnimations==appearance.EnableAnimations&&Appearance.Motion==appearance.Motion)return;
        Appearance=appearance;FinishAll();Changed?.Invoke();
    }
    private void SystemChanged(UISettings sender,object args)
        =>dispatcher.TryEnqueue(()=>{if(!disposed){FinishAll();Changed?.Invoke();}});
    private void FinishAll(){foreach(var scope in scopes.ToArray())scope.Finish();}
    internal AnimationScope CreateScope(Action? startProbe=null){var scope=new AnimationScope(this,()=>scopes.RemoveWhere(s=>s.IsDisposed),startProbe);scopes.Add(scope);return scope;}
    public void Dispose()
    {disposed=true;system.AnimationsEnabledChanged-=SystemChanged;foreach(var scope in scopes.ToArray())scope.Dispose();Changed=null;}
}

// One scope per window: no frame timers, HWND movement or global references to unloaded controls.
internal sealed class AnimationScope(AnimationService service,Action removed,Action? startProbe=null) : IDisposable
{
    private sealed record Running(Visual Visual,CompositionScopedBatch Batch,Action Finish,Action Replace);
    private readonly Dictionary<FrameworkElement,Running> running=[];
    private readonly Dictionary<FrameworkElement,Action> bindings=[];
    private readonly Dictionary<FrameworkElement,Action> watched=[];
    internal bool IsDisposed {get;private set;}
    internal int ActiveCount=>running.Count;
    internal int StartedCount {get;private set;}
    internal int FallbackCount {get;private set;}

    internal void Enter(FrameworkElement element,AnimationArea area,bool fade=true)
    {
        if(IsDisposed)return;
        if(!element.IsLoaded)
        {
            // Consume this request once, even if a restored tab is loaded again later.
            RoutedEventHandler? loaded=null;
            loaded=(_,_)=>{element.Loaded-=loaded;watched.Remove(element);Enter(element,area,fade);};
            if(watched.ContainsKey(element))return;
            element.Loaded+=loaded;watched[element]=()=>element.Loaded-=loaded;return;
        }
        var profile=service.Profile(area);
        Animate(element,profile,1,Vector3.One,Vector3.Zero,null,true,fade:fade);
    }
    internal void Exit(FrameworkElement element,AnimationArea area,Action completed,bool fade=true)
    {
        // Exit must release layout at the fade endpoint; a spring's long settling tail
        // would otherwise leave an invisible closed tab occupying its slot.
        var profile=service.Profile(area) with{Spring=false};
        Animate(element,profile,fade?0:1,new(profile.EnterScale,profile.EnterScale,1),new(0,-profile.Distance,0),completed,fade:fade);
    }
    internal void Reset(FrameworkElement element)
    {
        if(watched.Remove(element,out var detach))detach();
        Stop(element);
        try{var visual=ElementCompositionPreview.GetElementVisual(element);visual.Opacity=1;visual.Scale=Vector3.One;visual.Properties.InsertVector3("Translation",Vector3.Zero);}catch{FallbackCount++;}
    }
    internal void Reposition(FrameworkElement element,float delta)
    {
        var profile=service.Profile(AnimationArea.Tabs);
        if(!profile.Enabled||profile.Distance==0||Math.Abs(delta)<0.5)return;
        Animate(element,profile,1,Vector3.One,Vector3.Zero,null,false,new(delta,0,0));
    }
    internal void Feedback(FrameworkElement element,AnimationArea area,bool over,bool pressed)
    {
        var profile=service.Profile(area,true);
        float scale=!profile.Enabled?1:pressed?profile.PressedScale:over?profile.HoverScale:1;
        Animate(element,profile,1,new(scale,scale,1),Vector3.Zero,null);
    }
    internal void Drag(FrameworkElement element,AnimationArea area,float distance)
    {
        if(IsDisposed||!element.IsLoaded)return;
        var profile=service.Profile(area);Stop(element);
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element,true);
            var visual=ElementCompositionPreview.GetElementVisual(element);
            visual.Properties.InsertVector3("Translation",new(profile.Enabled?Math.Clamp(distance,-profile.Distance,profile.Distance):0,0,0));
        }
        catch{FallbackCount++;}
    }
    private void Animate(FrameworkElement element,MotionProfile profile,float opacity,Vector3 scale,Vector3 translation,Action? completed,bool enter=false,Vector3? initialTranslation=null,bool fade=true)
    {
        if(IsDisposed){completed?.Invoke();return;}
        bool replacing=running.ContainsKey(element);Stop(element);
        if(!profile.Enabled||!element.IsLoaded)
        {
            try{var target=ElementCompositionPreview.GetElementVisual(element);target.StopAnimation("Opacity");target.StopAnimation("Scale");target.StopAnimation("Translation");target.Opacity=opacity;target.Scale=Vector3.One;target.Properties.InsertVector3("Translation",Vector3.Zero);}catch{ }
            completed?.Invoke();return;
        }
        CompositionScopedBatch? batch=null;
        Visual? visual=null;
        bool finished=false;
        TypedEventHandler<object,CompositionBatchCompletedEventArgs>? handler=null;
        RoutedEventHandler? unloaded=null;
        void Finish(bool snap=true)
        {
            if(finished)return;finished=true;
            element.Unloaded-=unloaded;
            if(batch!=null)batch.Completed-=handler;
            running.Remove(element);
            try
            {
                visual?.StopAnimation("Opacity");visual?.StopAnimation("Scale");visual?.StopAnimation("Translation");
                if(visual!=null&&(snap||completed!=null)){visual.Opacity=opacity;visual.Scale=scale;visual.Properties.InsertVector3("Translation",translation);}
            }
            catch{FallbackCount++;}
            finally{try{batch?.Dispose();}catch{FallbackCount++;}completed?.Invoke();}
        }
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element,true);
            visual=ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint=new((float)element.ActualWidth/2,(float)element.ActualHeight/2,0);
            var compositor=visual.Compositor;
            if(enter&&!replacing){visual.Opacity=fade?0:1;visual.Scale=new(profile.EnterScale,profile.EnterScale,1);visual.Properties.InsertVector3("Translation",new(0,profile.Distance,0));}
            if(!fade)visual.Opacity=opacity;
            if(initialTranslation is {} initial)visual.Properties.InsertVector3("Translation",initial);
            startProbe?.Invoke();
            batch=compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            using var easing=compositor.CreateCubicBezierEasingFunction(new(0.2f,0),new(0,1));
            using var opacityAnimation=compositor.CreateScalarKeyFrameAnimation();opacityAnimation.InsertKeyFrame(1,opacity,easing);opacityAnimation.Duration=TimeSpan.FromMilliseconds(profile.DurationMs);opacityAnimation.StopBehavior=AnimationStopBehavior.LeaveCurrentValue;
            visual.StartAnimation("Opacity",opacityAnimation);
            void Vector(string property,Vector3 target)
            {
                if(profile.Spring&&profile.Distance!=0)
                {
                    using var spring=compositor.CreateSpringVector3Animation();spring.FinalValue=target;spring.DampingRatio=property=="Scale"?profile.Damping:profile.TranslationDamping;spring.Period=TimeSpan.FromMilliseconds(profile.SpringPeriodMs);spring.StopBehavior=AnimationStopBehavior.LeaveCurrentValue;
                    visual.StartAnimation(property,spring);
                }
                else
                {
                    using var animation=compositor.CreateVector3KeyFrameAnimation();animation.InsertKeyFrame(1,target,easing);animation.Duration=TimeSpan.FromMilliseconds(profile.DurationMs);animation.StopBehavior=AnimationStopBehavior.LeaveCurrentValue;
                    visual.StartAnimation(property,animation);
                }
            }
            Vector("Scale",scale);Vector("Translation",translation);
            handler=(_,_)=>Finish();unloaded=(_,_)=>Finish();batch.Completed+=handler;element.Unloaded+=unloaded;
            running[element]=new(visual,batch,()=>Finish(),()=>Finish(false));StartedCount++;batch.End();
        }
        catch{FallbackCount++;Finish();}
    }
    private void Stop(FrameworkElement element){if(running.TryGetValue(element,out var previous))previous.Replace();}
    internal void Finish()
    {
        while(running.Count>0)foreach(var animation in running.Values.ToArray())animation.Finish();
        foreach(var control in bindings.Keys.OfType<Control>())
            try{var visual=ElementCompositionPreview.GetElementVisual(control);visual.Scale=Vector3.One;visual.Properties.InsertVector3("Translation",Vector3.Zero);}catch{ }
    }

    internal void WatchTree(FrameworkElement root,AnimationArea area)
    {
        if(IsDisposed)return;
        if(bindings.ContainsKey(root)){if(root.IsLoaded)AttachTree(root,area);return;}
        RoutedEventHandler loaded=(_,_)=>AttachTree(root,area);
        RoutedEventHandler? unloaded=null;
        void Detach(){root.Loaded-=loaded;root.Unloaded-=unloaded;bindings.Remove(root);}
        unloaded=(_,_)=>Detach();root.Loaded+=loaded;root.Unloaded+=unloaded;bindings[root]=Detach;
        if(root.IsLoaded)AttachTree(root,area);
    }
    private void AttachTree(DependencyObject node,AnimationArea area)
    {
        if(node is Control control&&node is ButtonBase or Slider or ComboBox or ToggleSwitch)AttachFeedback(control,area);
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)AttachTree(VisualTreeHelper.GetChild(node,i),area);
    }
    private void AttachFeedback(Control control,AnimationArea area)
    {
        if(bindings.ContainsKey(control))return;
        bool over=false,pressed=false;
        PointerEventHandler entered=(_,_)=>{over=true;Feedback(control,area,over,pressed);};
        PointerEventHandler exited=(_,_)=>{over=false;pressed=false;Feedback(control,area,over,pressed);};
        PointerEventHandler down=(_,_)=>{pressed=true;Feedback(control,area,over,pressed);};
        PointerEventHandler up=(_,_)=>{pressed=false;Feedback(control,area,over,pressed);};
        KeyEventHandler keyDown=(_,e)=>{if(e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter){pressed=true;Feedback(control,area,over,pressed);}};
        KeyEventHandler keyUp=(_,_)=>{pressed=false;Feedback(control,area,over,pressed);};
        RoutedEventHandler lost=(_,_)=>{pressed=false;Feedback(control,area,over,pressed);};
        RoutedEventHandler? unloaded=null;
        void Detach()
        {
            control.PointerEntered-=entered;control.PointerExited-=exited;
            control.RemoveHandler(UIElement.PointerPressedEvent,down);control.RemoveHandler(UIElement.PointerReleasedEvent,up);control.PointerCanceled-=up;control.PointerCaptureLost-=up;
            control.RemoveHandler(UIElement.KeyDownEvent,keyDown);control.RemoveHandler(UIElement.KeyUpEvent,keyUp);control.LostFocus-=lost;control.Unloaded-=unloaded;
            Stop(control);bindings.Remove(control);
            try{var visual=ElementCompositionPreview.GetElementVisual(control);visual.Scale=Vector3.One;visual.Properties.InsertVector3("Translation",Vector3.Zero);}catch{ }
        }
        unloaded=(_,_)=>Detach();bindings[control]=Detach;
        control.PointerEntered+=entered;control.PointerExited+=exited;control.AddHandler(UIElement.PointerPressedEvent,down,true);control.AddHandler(UIElement.PointerReleasedEvent,up,true);control.PointerCanceled+=up;control.PointerCaptureLost+=up;
        control.AddHandler(UIElement.KeyDownEvent,keyDown,true);control.AddHandler(UIElement.KeyUpEvent,keyUp,true);control.LostFocus+=lost;control.Unloaded+=unloaded;
    }
    public void Dispose()
    {
        if(IsDisposed)return;IsDisposed=true;Finish();
        foreach(var detach in watched.Values.ToArray())detach();watched.Clear();
        foreach(var detach in bindings.Values.ToArray())detach();bindings.Clear();removed();
    }
}
