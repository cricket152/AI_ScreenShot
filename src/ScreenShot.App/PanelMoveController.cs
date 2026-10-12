using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ScreenShot.App;

internal sealed class PanelMoveController : IDisposable
{
    private readonly nint handle;
    private readonly Action<PanelMoveGesture> callback;
    private readonly Func<bool> canBegin;
    private readonly List<UIElement> areas=[];
    private readonly PointerEventHandler pressedHandler,movedHandler,releasedHandler;
    private UIElement? activeArea;
    private Pointer? activePointer;
    private Native.Point startPoint,currentPoint;
    private double startScale;
    private int deltaX,deltaY;
    private bool moved,disposed;

    internal PanelMoveController(nint handle,Action<PanelMoveGesture> callback,Func<bool> canBegin)
    {
        this.handle=handle;this.callback=callback;this.canBegin=canBegin;
        pressedHandler=Pressed;movedHandler=Moved;releasedHandler=Released;
    }

    internal bool IsDragging=>activeArea!=null;

    internal void Attach(UIElement area)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        ArgumentNullException.ThrowIfNull(area);
        if(areas.Any(item=>ReferenceEquals(item,area)))throw new InvalidOperationException("面板拖动区域已经绑定");
        area.AddHandler(UIElement.PointerPressedEvent,pressedHandler,true);
        area.AddHandler(UIElement.PointerMovedEvent,movedHandler,true);
        area.AddHandler(UIElement.PointerReleasedEvent,releasedHandler,true);
        area.PointerCanceled+=Canceled;area.PointerCaptureLost+=CaptureLost;
        if(area is FrameworkElement element)element.Unloaded+=Unloaded;
        areas.Add(area);
    }

    internal static bool IsEligibleSource(DependencyObject? source,UIElement area)
    {
        for(var node=source;node!=null;node=VisualTreeHelper.GetParent(node))
        {
            if(ReferenceEquals(node,area))return true;
            if(node is Control or RichTextBlock||node is TextBlock{IsTextSelectionEnabled:true})return false;
        }
        return false;
    }

    internal bool TryGetDragAreaAt(UIElement root,Windows.Foundation.Point point,out UIElement? area)
    {
        area=null;
        var source=VisualTreeHelper.FindElementsInHostCoordinates(point,root).FirstOrDefault();
        if(source==null)return false;
        for(DependencyObject? node=source;node!=null;node=VisualTreeHelper.GetParent(node))
        {
            if(node is not UIElement candidate||!areas.Any(item=>ReferenceEquals(item,candidate)))continue;
            if(!IsEligibleSource(source,candidate))return false;
            area=candidate;return true;
        }
        return false;
    }

    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(disposed||activeArea!=null||e.Pointer.PointerDeviceType!=PointerDeviceType.Mouse||sender is not UIElement area||
           !e.GetCurrentPoint(area).Properties.IsLeftButtonPressed||!IsEligibleSource(e.OriginalSource as DependencyObject,area)||!canBegin()||!Native.GetCursorPos(out var point))return;
        if(!area.CapturePointer(e.Pointer))return;
        activeArea=area;activePointer=e.Pointer;startPoint=currentPoint=point;
        startScale=Math.Max(96u,Native.GetDpiForWindow(handle))/96d;deltaX=deltaY=0;moved=false;
        e.Handled=true;Publish(new(PanelMovePhase.Begin,0,0,false,point.X,point.Y));
    }

    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(activeArea==null||e.Pointer.PointerId!=activePointer?.PointerId)return;
        if(!e.GetCurrentPoint(activeArea).Properties.IsLeftButtonPressed){Finish(PanelMovePhase.Cancel);return;}
        if(!Native.GetCursorPos(out var point))return;
        UpdatePoint(point);
        e.Handled=true;
        if(moved)Publish(new(PanelMovePhase.Update,deltaX,deltaY,true,point.X,point.Y));
    }

    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(activeArea==null||e.Pointer.PointerId!=activePointer?.PointerId)return;
        if(Native.GetCursorPos(out var point))UpdatePoint(point);
        e.Handled=true;Finish(PanelMovePhase.End);
    }

    private void UpdatePoint(Native.Point point)
    {
        currentPoint=point;
        // Screen coordinates remain independent of the window moving under the captured pointer.
        deltaX=(int)Math.Clamp((long)point.X-startPoint.X,int.MinValue,int.MaxValue);
        deltaY=(int)Math.Clamp((long)point.Y-startPoint.Y,int.MinValue,int.MaxValue);
        moved|=PanelMovePolicy.ReachedThreshold(deltaX,deltaY,startScale);
    }

    private void Publish(PanelMoveGesture gesture)
    {
        try{callback(gesture);}
        catch
        {
            // Release capture on callback failure and preserve the original exception.
            try{Finish(PanelMovePhase.Cancel);}catch{ }
            throw;
        }
    }

    private void Canceled(object sender,PointerRoutedEventArgs e){if(e.Pointer.PointerId==activePointer?.PointerId)Finish(PanelMovePhase.Cancel);}
    private void CaptureLost(object sender,PointerRoutedEventArgs e){if(e.Pointer.PointerId==activePointer?.PointerId)Finish(PanelMovePhase.Cancel);}
    private void Unloaded(object sender,RoutedEventArgs e){if(ReferenceEquals(sender,activeArea))Finish(PanelMovePhase.Cancel);}
    internal void Cancel()=>Finish(PanelMovePhase.Cancel);

    private void Finish(PanelMovePhase phase)
    {
        var area=activeArea;var pointer=activePointer;if(area==null)return;
        activeArea=null;activePointer=null;
        try{if(pointer!=null)area.ReleasePointerCapture(pointer);}
        finally{callback(new(phase,deltaX,deltaY,moved,currentPoint.X,currentPoint.Y));}
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{Cancel();}
        finally
        {
            foreach(var area in areas)
            {
                area.RemoveHandler(UIElement.PointerPressedEvent,pressedHandler);
                area.RemoveHandler(UIElement.PointerMovedEvent,movedHandler);
                area.RemoveHandler(UIElement.PointerReleasedEvent,releasedHandler);
                area.PointerCanceled-=Canceled;area.PointerCaptureLost-=CaptureLost;
                if(area is FrameworkElement element)element.Unloaded-=Unloaded;
            }
            areas.Clear();
        }
    }
}
