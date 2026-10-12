namespace ScreenShot.App;

internal sealed partial class FloatingPanelWindow
{
    private bool aligningBall;
    private double BallCenterOffset=>outer.Padding.Top+(toolbarVisible?toolbarHeight:Math.Min(56,messageHeight))/2;
    // The HWND includes transparent padding: the visible pane-to-ball gap is eight logical pixels.
    private double BallHostGap=>8-outer.Padding.Right;

    private void AlignBallWithToolbar()
    {
        if(closed||fullScreen||aligningBall||app.Ball==null||!Native.GetWindowRect(Handle,out var panel))return;
        double scale=Math.Max(96u,Native.GetDpiForWindow(Handle))/96d;
        aligningBall=true;
        try{app.Ball.AlignWithToolbar(panel.Top+(int)Math.Round(BallCenterOffset*scale));}
        finally{aligningBall=false;}
    }

    internal void FollowBall(Native.Rect previous,Native.Rect next)
    {
        if(closed||fullScreen||aligningBall||!expanded||!Native.GetWindowRect(Handle,out var panel))return;
        FinishVisualTransition();moveController.Cancel();resizeController.Cancel();
        if(!TryWorkArea(next,out var work))return;
        double scale=Math.Max(96u,Native.GetDpiForWindow(Handle))/96d;
        int width=panel.Right-panel.Left,height=panel.Bottom-panel.Top;
        int gap=(int)Math.Round(BallHostGap*scale);
        bool wasLeft=(panel.Left+panel.Right)/2d<(previous.Left+previous.Right)/2d;
        bool leftFits=next.Left-gap-width>=work.Left,rightFits=next.Right+gap+width<=work.Right;
        bool left=leftFits&&(wasLeft||!rightFits);
        int x=Math.Clamp(left?next.Left-gap-width:next.Right+gap,work.Left,Math.Max(work.Left,work.Right-width));
        int y=Math.Clamp((next.Top+next.Bottom)/2-(int)Math.Round(BallCenterOffset*scale),work.Top,Math.Max(work.Top,work.Bottom-height));
        manuallyPositioned=true;
        AppWindow.Move(new Windows.Graphics.PointInt32(x,y));
        double newScale=Math.Max(96u,Native.GetDpiForWindow(Handle))/96d;
        FitToWorkArea(work,newScale);UpdateLayoutDimensions();
        width=(int)Math.Round(EffectiveLogicalWidth*newScale);height=(int)Math.Round(LogicalHeight*newScale);gap=(int)Math.Round(BallHostGap*newScale);
        x=Math.Clamp(left?next.Left-gap-width:next.Right+gap,work.Left,Math.Max(work.Left,work.Right-width));
        y=Math.Clamp((next.Top+next.Bottom)/2-(int)Math.Round(BallCenterOffset*newScale),work.Top,Math.Max(work.Top,work.Bottom-height));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x,y,width,height));
        AlignBallWithToolbar();UpdatePaneVisuals();
    }
}
