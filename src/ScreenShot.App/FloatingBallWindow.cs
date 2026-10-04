using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace ScreenShot.App;

internal sealed class FloatingBallWindow : Window
{
    internal const double LogicalSize=52;
    private readonly Button button;
    private readonly Action expand;
    private readonly SubclassProc subclass;
    private bool positioned,pressed,dragging,suppressClick,repositionPending,closed;
    private Native.Point pressPoint;
    private int pointerOffsetX,pointerOffsetY;
    public nint Handle {get;}

    public FloatingBallWindow(Action expand,Action hide,Action settings)
    {
        this.expand=expand;Title="AI ScreenShot 悬浮入口";
        Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.SetWindowLongPtr(Handle,-20,(Native.GetWindowLongPtr(Handle,-20)|0x80)&~0x40000);
        var presenter=OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;
        presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;
        AppWindow.SetPresenter(presenter);

        button=(Button)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"52\" Height=\"52\" Padding=\"0\" CornerRadius=\"26\" BorderThickness=\"1\" Background=\"{ThemeResource ButtonBackground}\" BorderBrush=\"{ThemeResource ButtonBorderBrush}\" />");
        button.Content=new FontIcon{Glyph="\uE722",FontSize=20};
        AutomationProperties.SetName(button,"AI ScreenShot 悬浮入口");
        ToolTipService.SetToolTip(button,"AI ScreenShot · 点击展开，拖动贴边");
        Content=button;
        button.Click+=(_,_)=>{if(!suppressClick&&!dragging)this.expand();};
        button.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(PointerPressed),true);
        button.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler(PointerMoved),true);
        button.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(PointerReleased),true);
        button.PointerCanceled+=(_,_)=>FinishDrag();
        button.PointerCaptureLost+=(_,_)=>{if(pressed)FinishDrag();};

        var menu=new MenuFlyout();
        var hideItem=new MenuFlyoutItem{Text="隐藏悬浮球",Icon=new SymbolIcon(Symbol.Cancel)};
        var settingsItem=new MenuFlyoutItem{Text="设置",Icon=new SymbolIcon(Symbol.Setting)};
        hideItem.Click+=(_,_)=>hide();settingsItem.Click+=(_,_)=>settings();
        menu.Items.Add(hideItem);menu.Items.Add(settingsItem);
        button.ContextFlyout=menu;

        subclass=WindowMessage;
        if(!SetWindowSubclass(Handle,subclass,1,0))throw new InvalidOperationException("无法初始化悬浮入口窗口");
        Closed+=(_,_)=>{closed=true;RemoveWindowSubclass(Handle,subclass,1);};
    }

    public void Present()
    {
        if(closed)return;
        if(!positioned)
        {
            var info=MonitorAt(new Native.Point());
            AppWindow.Move(new PointInt32(info.Work.Left,info.Work.Top));
            int size=PhysicalSize();
            ApplyBounds(info.Work.Right-size,info.Work.Top+(info.Work.Bottom-info.Work.Top-size)/2,size);
            positioned=true;
        }
        else SnapToEdge();
        AppWindow.Show(false);
        Native.SetWindowPos(Handle,new nint(-1),0,0,0,0,0x01|0x02|0x10|0x40);
    }

    public void SnapToEdge()
    {
        if(closed)return;
        var bounds=GetBounds();
        var info=MonitorAt(new Native.Point{X=bounds.Left+(bounds.Right-bounds.Left)/2,Y=bounds.Top+(bounds.Bottom-bounds.Top)/2});
        int size=PhysicalSize();
        bool left=bounds.Left+(bounds.Right-bounds.Left)/2<info.Work.Left+(info.Work.Right-info.Work.Left)/2;
        int x=left?info.Work.Left:info.Work.Right-size;
        int y=Math.Clamp(bounds.Top,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-size));
        ApplyBounds(x,y,size);positioned=true;
    }

    public Native.Rect GetBounds()
    {
        if(!Native.GetWindowRect(Handle,out var bounds))throw new InvalidOperationException("无法读取悬浮入口位置");
        return bounds;
    }

    private void PointerPressed(object sender,PointerRoutedEventArgs e)
    {
        if(!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed||!Native.GetCursorPos(out pressPoint))return;
        var bounds=GetBounds();pointerOffsetX=pressPoint.X-bounds.Left;pointerOffsetY=pressPoint.Y-bounds.Top;
        suppressClick=false;pressed=true;dragging=false;button.CapturePointer(e.Pointer);
    }

    private void PointerMoved(object sender,PointerRoutedEventArgs e)
    {
        if(!pressed||!Native.GetCursorPos(out var point))return;
        int threshold=Math.Max(4,(int)Math.Round(4*Native.GetDpiForWindow(Handle)/96d));
        if(!dragging&&Math.Abs(point.X-pressPoint.X)<threshold&&Math.Abs(point.Y-pressPoint.Y)<threshold)return;
        dragging=true;suppressClick=true;
        var info=MonitorAt(point);int size=PhysicalSize();
        int x=Math.Clamp(point.X-pointerOffsetX,info.Work.Left,Math.Max(info.Work.Left,info.Work.Right-size));
        int y=Math.Clamp(point.Y-pointerOffsetY,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-size));
        ApplyBounds(x,y,size);e.Handled=true;
    }

    private void PointerReleased(object sender,PointerRoutedEventArgs e)
    {
        if(!pressed)return;
        bool wasDragging=dragging;FinishDrag();
        button.ReleasePointerCapture(e.Pointer);
        if(wasDragging)e.Handled=true;
    }

    private void FinishDrag()
    {
        bool wasDragging=dragging;pressed=false;dragging=false;
        if(!wasDragging)return;
        SnapToEdge();
        // Button may raise Click later in this pointer-release dispatch.
        DispatcherQueue.TryEnqueue(()=>suppressClick=false);
    }

    private int PhysicalSize()=>Math.Max(1,(int)Math.Round(LogicalSize*Math.Max(96u,Native.GetDpiForWindow(Handle))/96d));

    private static Native.MonitorInfo MonitorAt(Native.Point point)
    {
        var info=new Native.MonitorInfo{Size=(uint)Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(Native.MonitorFromPoint(point,2),ref info))throw new InvalidOperationException("无法确定悬浮入口所在显示器");
        return info;
    }

    private void ApplyBounds(int x,int y,int size)
    {
        Native.SetWindowPos(Handle,new nint(-1),x,y,size,size,0x10);
        nint region=CreateEllipticRgn(0,0,size+1,size+1);
        if(region!=0&&SetWindowRgn(Handle,region,true)==0)Native.DeleteObject(region);
    }

    private nint WindowMessage(nint window,uint message,nint wparam,nint lparam,nuint id,nuint data)
    {
        nint result=DefSubclassProc(window,message,wparam,lparam);
        if(message is 0x02E0 or 0x007E or 0x001A)
        {
            if(!repositionPending&&!closed)
            {
                repositionPending=true;
                DispatcherQueue.TryEnqueue(()=>
                {
                    repositionPending=false;if(closed||!positioned)return;
                    if(dragging)
                    {
                        var bounds=GetBounds();ApplyBounds(bounds.Left,bounds.Top,PhysicalSize());
                    }
                    else SnapToEdge();
                });
            }
        }
        return result;
    }

    private delegate nint SubclassProc(nint window,uint message,nint wparam,nint lparam,nuint id,nuint data);
    [DllImport("comctl32.dll",SetLastError=true)] private static extern bool SetWindowSubclass(nint window,SubclassProc callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window,SubclassProc callback,nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window,uint message,nint wparam,nint lparam);
    [DllImport("gdi32.dll")] private static extern nint CreateEllipticRgn(int left,int top,int right,int bottom);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window,nint region,bool redraw);
}
