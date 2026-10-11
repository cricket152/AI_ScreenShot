using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ScreenShot.App;

internal sealed class PanelResizeController : IDisposable
{
    private readonly nint handle;
    private readonly Action<PanelResizeGesture> callback;
    private readonly List<Attachment> attachments=[];
    private Grip? activeGrip;
    private Pointer? activePointer;
    private Native.Point startPoint;
    private double startScale,deltaX,deltaY;
    private bool disposed;

    internal PanelResizeController(nint handle,Action<PanelResizeGesture> callback){this.handle=handle;this.callback=callback;}
    internal bool IsDragging=>activeGrip!=null;
    internal void SetEnabled(bool enabled)
    {
        if(!enabled)Cancel();
        foreach(var attachment in attachments)foreach(var grip in attachment.Grips)grip.IsHitTestVisible=enabled;
    }

    internal void Attach(Border surface,PanelResizePane pane,double topGripThickness=PanelResizePolicy.GripThickness)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!double.IsFinite(topGripThickness)||topGripThickness<PanelResizePolicy.GripThickness)throw new ArgumentOutOfRangeException(nameof(topGripThickness));
        if(attachments.Any(item=>ReferenceEquals(item.Surface,surface)))throw new InvalidOperationException("面板边缘已经绑定");
        var child=surface.Child;var padding=surface.Padding;
        surface.Child=null;surface.Padding=new Thickness(0);
        var content=new Border{Padding=padding,Child=child};var wrapper=new Grid();wrapper.Children.Add(content);
        var grips=new List<Grip>();
        foreach(var edges in new[]{PanelResizeEdges.Left,PanelResizeEdges.Top,PanelResizeEdges.Right,PanelResizeEdges.Bottom,PanelResizeEdges.Left|PanelResizeEdges.Top,PanelResizeEdges.Right|PanelResizeEdges.Top,PanelResizeEdges.Left|PanelResizeEdges.Bottom,PanelResizeEdges.Right|PanelResizeEdges.Bottom})
        {
            var grip=new Grip(pane,edges){Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
            bool horizontal=(edges&(PanelResizeEdges.Left|PanelResizeEdges.Right))!=0,vertical=(edges&(PanelResizeEdges.Top|PanelResizeEdges.Bottom))!=0;
            if(horizontal)grip.Width=PanelResizePolicy.GripThickness;if(vertical)grip.Height=edges.HasFlag(PanelResizeEdges.Top)?topGripThickness:PanelResizePolicy.GripThickness;
            grip.HorizontalAlignment=edges.HasFlag(PanelResizeEdges.Left)?HorizontalAlignment.Left:edges.HasFlag(PanelResizeEdges.Right)?HorizontalAlignment.Right:HorizontalAlignment.Stretch;
            grip.VerticalAlignment=edges.HasFlag(PanelResizeEdges.Top)?VerticalAlignment.Top:edges.HasFlag(PanelResizeEdges.Bottom)?VerticalAlignment.Bottom:VerticalAlignment.Stretch;
            if(!horizontal)grip.Margin=new Thickness(PanelResizePolicy.GripThickness,0,PanelResizePolicy.GripThickness,0);
            else if(!vertical)grip.Margin=new Thickness(0,PanelResizePolicy.GripThickness,0,PanelResizePolicy.GripThickness);
            AutomationProperties.SetAccessibilityView(grip,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            grip.PointerPressed+=Pressed;grip.PointerMoved+=Moved;grip.PointerReleased+=Released;grip.PointerCanceled+=Canceled;grip.PointerCaptureLost+=CaptureLost;grip.Unloaded+=Unloaded;
            grips.Add(grip);wrapper.Children.Add(grip);
        }
        surface.Child=wrapper;attachments.Add(new(surface,wrapper,content,padding,grips));
    }

    internal bool TryGetGripAt(UIElement root,Windows.Foundation.Point point,out PanelResizePane pane,out PanelResizeEdges edges)
    {
        var grip=VisualTreeHelper.FindElementsInHostCoordinates(point,root).OfType<Grip>().FirstOrDefault();
        pane=grip?.Pane??default;edges=grip?.Edges??PanelResizeEdges.None;return grip!=null;
    }

    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(disposed||activeGrip!=null||e.Pointer.PointerDeviceType!=PointerDeviceType.Mouse||!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed||!Native.GetCursorPos(out var point))return;
        var grip=(Grip)sender;
        if(!grip.CapturePointer(e.Pointer))return;
        activeGrip=grip;activePointer=e.Pointer;startPoint=point;startScale=Math.Max(96u,Native.GetDpiForWindow(handle))/96d;deltaX=deltaY=0;
        e.Handled=true;callback(new(grip.Pane,grip.Edges,PanelResizePhase.Begin,0,0,startScale));
    }

    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(activeGrip==null||e.Pointer.PointerId!=activePointer?.PointerId)return;
        if(!e.GetCurrentPoint(activeGrip).Properties.IsLeftButtonPressed){Finish(PanelResizePhase.Cancel);return;}
        if(!Native.GetCursorPos(out var point))return;
        deltaX=(point.X-startPoint.X)/startScale;deltaY=(point.Y-startPoint.Y)/startScale;
        e.Handled=true;callback(new(activeGrip.Pane,activeGrip.Edges,PanelResizePhase.Update,deltaX,deltaY,startScale));
    }

    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(activeGrip==null||e.Pointer.PointerId!=activePointer?.PointerId)return;
        if(Native.GetCursorPos(out var point)){deltaX=(point.X-startPoint.X)/startScale;deltaY=(point.Y-startPoint.Y)/startScale;}
        e.Handled=true;Finish(PanelResizePhase.End);
    }

    private void Canceled(object sender,PointerRoutedEventArgs e){if(e.Pointer.PointerId==activePointer?.PointerId)Finish(PanelResizePhase.Cancel);}
    private void CaptureLost(object sender,PointerRoutedEventArgs e){if(e.Pointer.PointerId==activePointer?.PointerId)Finish(PanelResizePhase.Cancel);}
    private void Unloaded(object sender,RoutedEventArgs e){if(ReferenceEquals(sender,activeGrip))Finish(PanelResizePhase.Cancel);}
    internal void Cancel()=>Finish(PanelResizePhase.Cancel);

    private void Finish(PanelResizePhase phase)
    {
        var grip=activeGrip;var pointer=activePointer;if(grip==null)return;
        activeGrip=null;activePointer=null;
        try{if(pointer!=null)grip.ReleasePointerCapture(pointer);}
        finally{callback(new(grip.Pane,grip.Edges,phase,deltaX,deltaY,startScale));}
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;Cancel();
        foreach(var attachment in attachments)
        {
            foreach(var grip in attachment.Grips)
            {
                grip.PointerPressed-=Pressed;grip.PointerMoved-=Moved;grip.PointerReleased-=Released;grip.PointerCanceled-=Canceled;grip.PointerCaptureLost-=CaptureLost;grip.Unloaded-=Unloaded;grip.Dispose();
            }
            if(ReferenceEquals(attachment.Surface.Child,attachment.Wrapper))
            {
                attachment.Surface.Child=null;var child=attachment.Content.Child;attachment.Content.Child=null;attachment.Surface.Padding=attachment.Padding;attachment.Surface.Child=child;
            }
        }
        attachments.Clear();
    }

    private sealed record Attachment(Border Surface,Grid Wrapper,Border Content,Thickness Padding,List<Grip> Grips);
    private sealed class Grip : Control,IDisposable
    {
        private readonly InputSystemCursor cursor;
        internal PanelResizePane Pane {get;}
        internal PanelResizeEdges Edges {get;}
        internal Grip(PanelResizePane pane,PanelResizeEdges edges)
        {
            Pane=pane;Edges=edges;
            IsTabStop=false;
            Template=(ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Control\"><Border Background=\"{TemplateBinding Background}\" /></ControlTemplate>");
            var shape=edges switch
            {
                PanelResizeEdges.Left or PanelResizeEdges.Right=>InputSystemCursorShape.SizeWestEast,
                PanelResizeEdges.Top or PanelResizeEdges.Bottom=>InputSystemCursorShape.SizeNorthSouth,
                (PanelResizeEdges.Left|PanelResizeEdges.Top) or (PanelResizeEdges.Right|PanelResizeEdges.Bottom)=>InputSystemCursorShape.SizeNorthwestSoutheast,
                _=>InputSystemCursorShape.SizeNortheastSouthwest
            };
            cursor=InputSystemCursor.Create(shape);Loaded+=OnLoaded;
        }
        private void OnLoaded(object sender,RoutedEventArgs e)=>ProtectedCursor=cursor;
        public void Dispose(){Loaded-=OnLoaded;ProtectedCursor=null;cursor.Dispose();}
    }
}
