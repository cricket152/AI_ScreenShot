using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ScreenShot.App;

internal sealed partial class FloatingPanelWindow
{
    private bool fullScreen,composerCollapsed;
    private sealed record FloatingState(Native.Rect Bounds,bool ToolbarVisible,bool MessageVisible);
    private FloatingState? floatingState;
    private readonly List<(DependencyObject Element,CornerRadius Radius)> floatingCorners=[];
    private Thickness floatingContentPadding;
    private readonly UiTransition composerTransition=new();
    internal bool IsFullScreen=>fullScreen;
    internal bool IsComposerCollapsed=>composerCollapsed;
    internal Button FullScreenButton=>fullScreenButton;
    internal Button ComposerToggleButton=>current.InputToggle;
    internal Border ComposerSurface=>current.InputSurface;
    internal double ComposerTranslation=>current.InputTranslation.Y;
    internal bool IsComposerTransitioning=>composerTransition.IsRunning;
    internal Grid TabRow=>tabRow;

    private void UpdateFullScreenButton()
    {
        fullScreenButton.Content=UiDesign.Icon(fullScreen?"\uE73F":"\uE740",14);
        Label(fullScreenButton,fullScreen?"退出全屏（Esc）":"全屏显示对话");
    }

    private Border MessageContentHost=>((Grid)MessageSurface.Child).Children.OfType<Border>().Single(border=>border.Child!=null);

    private void ToggleFullScreen()
    {
        if(closed||!expanded)return;
        if(ExitFullScreen())return;
        FinishVisualTransition();moveController.Cancel();resizeController.Cancel();ClosePreview();paneMenu.Hide();
        if(!Native.GetWindowRect(Handle,out var bounds))return;
        var info=new Native.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(Native.MonitorFromPoint(new Native.Point{X=(bounds.Left+bounds.Right)/2,Y=(bounds.Top+bounds.Bottom)/2},2),ref info))return;
        floatingState=new(bounds,toolbarVisible,messageVisible);
        fullScreen=true;toolbarVisible=false;messageVisible=true;
        // Keep the frameless presenter and use physical monitor bounds, including on secondary displays.
        outer.Padding=new Thickness(0);layout.RowDefinitions[1].Height=new GridLength(1,GridUnitType.Star);
        floatingContentPadding=MessageContentHost.Padding;MessageContentHost.Padding=new Thickness(0);
        floatingCorners.Clear();floatingCorners.Add((MessageSurface,MessageSurface.CornerRadius));
        foreach(var border in ((Grid)MessageSurface.Child).Children.OfType<Border>())floatingCorners.Add((border,border.CornerRadius));
        foreach(var material in ((Grid)MessageSurface.Child).Children.OfType<SystemBackdropElement>())floatingCorners.Add((material,material.CornerRadius));
        foreach(var item in floatingCorners)SetCorner(item.Element,new CornerRadius(0));
        tabRow.Padding=new Thickness(8,0,8,0);resizeController.SetEnabled(false);
        UpdateFullScreenButton();ApplyPaneVisibility();UpdateLayoutDimensions();shadowWindow.Hide();app.Ball?.AppWindow.Hide();
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top));
        outer.UpdateLayout();UpdatePaneVisuals();fullScreenButton.Focus(FocusState.Programmatic);
    }

    internal bool ExitFullScreen()
    {
        if(!fullScreen||floatingState is not {} state)return false;
        fullScreen=false;floatingState=null;toolbarVisible=state.ToolbarVisible;messageVisible=state.MessageVisible;
        outer.Padding=UiDesign.Thickness("SS.Spacing.M");layout.RowDefinitions[1].Height=GridLength.Auto;
        MessageContentHost.Padding=floatingContentPadding;
        foreach(var item in floatingCorners)SetCorner(item.Element,item.Radius);floatingCorners.Clear();
        tabRow.Padding=new Thickness(0);resizeController.SetEnabled(true);UpdateFullScreenButton();ApplyPaneVisibility();UpdateLayoutDimensions();
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(state.Bounds.Left,state.Bounds.Top,state.Bounds.Right-state.Bounds.Left,state.Bounds.Bottom-state.Bounds.Top));
        ResizeAfterVisibilityChange();outer.UpdateLayout();UpdatePaneVisuals();app.ShowFloatingBallIfWanted();
        if(messageVisible)fullScreenButton.Focus(FocusState.Programmatic);else paneMenuButton.Focus(FocusState.Programmatic);
        return true;
    }

    private static void SetCorner(DependencyObject element,CornerRadius radius)
    {if(element is Border border)border.CornerRadius=radius;else if(element is SystemBackdropElement material)material.CornerRadius=radius;}

    private void UpdateComposerToggle(ConversationView view)
    {
        view.InputToggle.Content=UiDesign.Icon(composerCollapsed?"\uE70E":"\uE70D",12);
        Label(view.InputToggle,composerCollapsed?"展开输入区":"收起输入区");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(view.InputToggle,composerCollapsed?"保留截图和提示词草稿，展开后继续发送":"隐藏提示词、发送按钮和模式选择，扩大对话阅读区域");
    }

    private void ApplyComposerState(ConversationView view)
    {
        if(view.InputSurface==null)return;
        view.InputSurface.Visibility=composerCollapsed?Visibility.Collapsed:Visibility.Visible;
        view.InputSurface.Opacity=composerCollapsed?0:1;view.InputTranslation.Y=composerCollapsed?20:0;
        view.InputControls.IsEnabled=!composerCollapsed;UpdateComposerToggle(view);
    }

    private void FinishComposerTransition()
    {composerTransition.Stop();ApplyComposerState(current);}

    private void SetComposerCollapsed(bool collapsed,bool animate=true)
    {
        if(closed)return;
        composerTransition.Stop();composerCollapsed=collapsed;
        var view=current;var surface=view.InputSurface;UpdateComposerToggle(view);
        if(collapsed&&outer.XamlRoot!=null&&FocusManager.GetFocusedElement(outer.XamlRoot) is DependencyObject focused)
        {
            for(var node=focused;node!=null;node=VisualTreeHelper.GetParent(node))
                if(ReferenceEquals(node,surface)){view.InputToggle.Focus(FocusState.Programmatic);break;}
        }
        surface.Visibility=Visibility.Visible;view.InputControls.IsEnabled=!collapsed;
        composerTransition.Start(new UiTransition.Track[]{
            OpacityTrack(surface,collapsed?0:1),
            new(view.InputTranslation,"Y",()=>view.InputTranslation.Y,value=>view.InputTranslation.Y=value,collapsed?20:0)
        }.Select(track=>track with{Animate=appearance.Motion.Style!=ScreenShot.Core.AnimationStyle.Minimal||track.Property=="Opacity"}),
            animate&&expanded&&!hiddenForOperation,()=>ApplyComposerState(view),app.Animations.Profile(ScreenShot.Core.AnimationArea.Panel));
    }
}
