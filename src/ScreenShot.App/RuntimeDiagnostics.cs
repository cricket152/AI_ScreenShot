using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ScreenShot.App;

internal static class RuntimeDiagnostics
{
    internal static async Task<string> VerifyFluentPanelAsync(FloatingPanelWindow panel,FloatingBallWindow ball,string output)
    {
        VerifyAppIcons(panel,ball);
        var root=(FrameworkElement)panel.Content;
        var toolbar=(Grid)panel.VisibilityMenuButton.Parent;
        var buttons=((StackPanel)toolbar.Children[0]).Children.OfType<Button>().ToArray();
        if(buttons.Length!=5)throw new InvalidOperationException("Fluent toolbar lost an action");
        foreach(var button in buttons)
        {
            button.ApplyTemplate();
            var states=Descendants<FrameworkElement>(button).SelectMany(element=>VisualStateManager.GetVisualStateGroups(element)).FirstOrDefault(group=>group.Name=="CommonStates");
            if(states==null||!new[]{"Normal","PointerOver","Pressed","Disabled"}.All(name=>states.States.Any(state=>state.Name==name)))
                throw new InvalidOperationException("Toolbar action lost native interaction states");
        }
        var ballRoot=(FrameworkElement)ball.Content;
        string materialChecks=await VerifyMaterialsAsync(panel);
        foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
        {
            ballRoot.RequestedTheme=theme;await Task.Delay(120);
            await File.WriteAllBytesAsync(Path.Combine(output,$"floating-ball-{theme}.png"),await RenderAsync(ballRoot));
        }
        Native.GetWindowRect(panel.Handle,out var before);double scale=Native.GetDpiForWindow(panel.Handle)/96d;
        double width=(before.Right-before.Left)/scale;
        panel.ResizeForDiagnostic(PanelResizePane.Message,PanelResizeEdges.Right,420-width,0);await Task.Delay(150);
        foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
        {
            root.RequestedTheme=theme;await Task.Delay(120);
            if(buttons.Any(button=>Math.Abs(button.ActualWidth-buttons[0].ActualWidth)>1||Math.Abs(button.ActualHeight-UiDesign.Number("SS.ControlHeight"))>1))
                throw new InvalidOperationException("Compact toolbar actions have inconsistent sizes");
            var tools=(FrameworkElement)toolbar.Children[0];
            var toolsEnd=tools.TransformToVisual(toolbar).TransformPoint(new Windows.Foundation.Point(tools.ActualWidth,0));
            var menuStart=panel.VisibilityMenuButton.TransformToVisual(toolbar).TransformPoint(new Windows.Foundation.Point());
            if(toolsEnd.X>menuStart.X+1)throw new InvalidOperationException("Compact toolbar overlaps the visibility menu");
            var selector=panel.ComposerModeSelector;var sendButton=panel.ComposerSendButton;
            var selectorPosition=selector.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            var sendPosition=sendButton.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            if(selector.ActualWidth<76||selectorPosition.X+selector.ActualWidth>root.ActualWidth||selectorPosition.Y+selector.ActualHeight>sendPosition.Y||Math.Abs(selectorPosition.X-sendPosition.X)>1||Math.Abs(selector.ActualWidth-sendButton.ActualWidth)>1)
                throw new InvalidOperationException("Composer mode selector is clipped or not aligned above Send");
            await File.WriteAllBytesAsync(Path.Combine(output,$"floating-compact-{theme}.png"),await RenderAsync(root));
        }
        panel.ResizeForDiagnostic(PanelResizePane.Message,PanelResizeEdges.Right,width-420,0);await Task.Delay(120);
        string motionChecks=await VerifyPanelTransitionsAsync(panel,output);
        return materialChecks+motionChecks+"PASS: five uniform native toolbar actions retain Normal/Hover/Pressed/Disabled templates; 420px layout keeps the menu separate\nPASS: Light/Dark ball and compact panel render from the generated UI only\n";
    }

    private static async Task<string> VerifyPanelTransitionsAsync(FloatingPanelWindow panel,string output)
    {
        var root=(FrameworkElement)panel.Content;var original=panel.Appearance;
        var foreground=Native.GetForegroundWindow();var originalTheme=root.RequestedTheme;
        var tools=(StackPanel)((Grid)panel.VisibilityMenuButton.Parent).Children[0];
        string prompt=panel.PromptText,reply=panel.ResponseText;int messages=panel.MessageCount;
        bool systemMotion=new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        async Task Settle()
        {
            var deadline=DateTime.UtcNow.AddSeconds(3);
            while(panel.IsTransitioning&&DateTime.UtcNow<deadline)await Task.Delay(30);
            if(panel.IsTransitioning)throw new InvalidOperationException("Panel motion never settled");
            await Task.Delay(80);root.UpdateLayout();
        }
        try
        {
            panel.ApplyAppearance(original with{EnableAnimations=true});
            root.UpdateLayout();VerifyDirectPanelHost(panel);await VerifyHostTransparencyAsync(panel,output);
            var itemPeer=new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(panel.ToolbarSizeItem);
            var invoke=(Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider?)itemPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            if(invoke==null)throw new InvalidOperationException("Toolbar size menu lacks native Invoke");
            double expandedWidth=panel.ToolbarSurface.ActualWidth,messageWidth=panel.MessageSurface.ActualWidth;
            invoke.Invoke();
            if(!panel.IsToolbarCompact||panel.ToolbarSizeItem.Text!="扩展工具栏"||panel.IsTransitioning!=systemMotion)
                throw new InvalidOperationException("Toolbar compact action did not start the requested motion policy");
            await Settle();
            if(tools.Children.OfType<Button>().Count(button=>button.Visibility==Visibility.Visible)!=2||panel.ToolbarSurface.ActualWidth>=expandedWidth-1||Math.Abs(panel.MessageSurface.ActualWidth-messageWidth)>1)
                throw new InvalidOperationException("Compact toolbar must retain only two actions without resizing messages");
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {root.RequestedTheme=theme;await Task.Delay(120);await File.WriteAllBytesAsync(Path.Combine(output,$"floating-capsule-no-base-{theme}.png"),await RenderAsync(root));}
            panel.ToggleMessage();if(panel.IsTransitioning!=systemMotion)throw new InvalidOperationException("Message hide skipped motion policy");await Settle();
            Native.GetWindowRect(panel.Handle,out var compactBounds);
            double scale=Native.GetDpiForWindow(panel.Handle)/96d;
            if((compactBounds.Right-compactBounds.Left)/scale>=300||panel.MessageSurface.Visibility!=Visibility.Collapsed)
                throw new InvalidOperationException("Toolbar-only compact window retained empty space");
            await File.WriteAllBytesAsync(Path.Combine(output,"floating-capsule-only.png"),await RenderAsync(root));
            await VerifyToolbarExpansionPathAsync(panel,invoke,systemMotion);await Settle();
            if(panel.IsToolbarCompact||tools.Children.OfType<Button>().Count(button=>button.Visibility==Visibility.Visible)!=5||panel.ToolbarSizeItem.Text!="缩小工具栏")
                throw new InvalidOperationException("Expanded toolbar did not restore all actions");
            panel.ToggleMessage();await Settle();
            if(Math.Abs(panel.MessageSurface.ActualWidth-panel.ToolbarSurface.ActualWidth)>1)throw new InvalidOperationException("Expanding panels lost shared alignment");
            panel.ToggleToolbar();await Settle();
            if(panel.ToolbarSurface.Visibility!=Visibility.Collapsed||panel.VisibilityMenuInToolbar)throw new InvalidOperationException("Toolbar hide lost the message menu");
            panel.ToggleToolbar();await Settle();
            panel.ToggleToolbarSize();await Task.Delay(30);panel.ToggleToolbarSize();await Settle();
            if(panel.IsToolbarCompact||tools.Children.OfType<Button>().Count(button=>button.Visibility==Visibility.Visible)!=5)
                throw new InvalidOperationException("Rapid toolbar retargeting left stale action visibility");
            panel.Collapse();panel.Open();await Settle();
            if(!Native.IsWindowVisible(panel.Handle)||!panel.IsExpanded)throw new InvalidOperationException("A late collapse completion hid a reopened panel");
            if(Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Pane transitions changed foreground before operation hiding");
            await VerifyPaneExitPathAsync(panel,systemMotion);await Settle();
            if(foreground==panel.Handle)foreground=Native.GetForegroundWindow();
            panel.Open();await Settle();
            root.UpdateLayout();VerifyDirectPanelHost(panel);
            panel.HideForOperation();
            if(panel.IsTransitioning||Native.IsWindowVisible(panel.Handle))throw new InvalidOperationException("Capture/operation hiding must be immediate and cancel animations");
            // Hiding an active panel releases its own focus; reopening must preserve the resulting foreground.
            if(foreground==panel.Handle)foreground=Native.GetForegroundWindow();
            panel.RestoreAfterOperation();if(panel.IsTransitioning!=systemMotion)throw new InvalidOperationException("Floating reopen skipped motion policy");await Settle();
            panel.ToggleToolbarSize();panel.ApplyAppearance(panel.Appearance with{EnableAnimations=false});
            if(panel.IsTransitioning||!panel.IsToolbarCompact||tools.Children.OfType<Button>().Count(button=>button.Visibility==Visibility.Visible)!=2)
                throw new InvalidOperationException("Disabling animations did not finish the target state");
            panel.ToggleToolbarSize();panel.ToggleMessage();
            if(panel.IsTransitioning||panel.MessageSurface.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Disabled animation preference delayed pane visibility");
            panel.ToggleMessage();
            if(panel.PromptText!=prompt||panel.ResponseText!=reply||panel.MessageCount!=messages)
                throw new InvalidOperationException("Panel transitions discarded content");
            var finalForeground=Native.GetForegroundWindow();
            if(finalForeground!=foreground)
                throw new InvalidOperationException($"Panel transition foreground changed: before=0x{foreground:X}, after=0x{finalForeground:X}, panel=0x{panel.Handle:X}");
        }
        finally
        {
            panel.ApplyAppearance(original with{EnableAnimations=false});
            if(panel.IsToolbarCompact)panel.ToggleToolbarSize();if(!panel.IsToolbarVisible)panel.ToggleToolbar();if(!panel.IsMessageVisible)panel.ToggleMessage();
            panel.ApplyAppearance(original);root.RequestedTheme=originalTheme;
        }
        return "PASS: transparent host has no base or HWND clipping; two rounded native panes draw independently; generated two-color DWM samples confirm transparent window perimeter/padding/corner/gap and continuous shadow/outline/body edges in Light/Dark; compact/expanded menu restores two/five actions and shrinks a toolbar-only HWND\n"+
            "PASS: sampled toolbar expansion keeps its left/top origin fixed and grows monotonically right; pane shadow follows message/all-window exit\n"+
            "PASS: toolbar size, pane visibility and floating reopen obey application/system animation policy; rapid retargeting cannot apply stale visibility; operation hiding cancels immediately\n"+
            "PASS: transitions preserve messages/prompt/foreground; disabling motion during a transition commits its target without leaving an invisible panel\n";
    }

    private static Native.Rect PaneDrawingBounds(FloatingPanelWindow panel)
    {
        var shapes=panel.PaneShapes;
        return shapes.Count==0?new():new Native.Rect{Left=shapes.Min(shape=>shape.Left),Top=shapes.Min(shape=>shape.Top),Right=shapes.Max(shape=>shape.Right),Bottom=shapes.Max(shape=>shape.Bottom)};
    }

    private static void VerifyDirectPanelHost(FloatingPanelWindow panel)
    {
        var root=(FrameworkElement)panel.Content;
        if(root is not Grid||panel.HostAlpha!=0||panel.MaterialBackdrops.Count!=2||panel.EdgeStrokes.Count!=2||root.Clip!=null||new[]{panel.ToolbarSurface,panel.MessageSurface}.Any(pane=>pane.Clip!=null||(pane.Child as UIElement)?.Clip!=null))
            throw new InvalidOperationException("Floating panes require a transparent host, exactly two materials/outlines, and no visual clipping");
        Native.GetWindowRect(panel.Handle,out var window);var origin=new Native.Point();Native.ClientToScreen(panel.Handle,ref origin);
        Native.GetClientRect(panel.Handle,out var client);
        if(origin.X!=window.Left||origin.Y!=window.Top||client.Right!=window.Right-window.Left||client.Bottom!=window.Bottom-window.Top||(Native.GetWindowLongPtr(panel.Handle,-16).ToInt64()&0x00c40000)!=0||(Native.GetWindowLongPtr(panel.Handle,-20).ToInt64()&0x00020300)!=0)
            throw new InvalidOperationException("Transparent host retained a native non-client frame");
        foreach(var pair in new[]{panel.ToolbarSurface,panel.MessageSurface}.Zip(panel.EdgeStrokes))
        {
            var pane=pair.First;var layers=((Grid)pane.Child).Children.Take(2).OfType<FrameworkElement>().Append(pair.Second);
            if(pane.BorderThickness!=new Thickness(0)||layers.Count()!=3||layers.Any(layer=>layer.TransformToVisual(pane).TransformPoint(new Windows.Foundation.Point())!=new Windows.Foundation.Point()||Math.Abs(layer.ActualWidth-pane.ActualWidth)>0.01||Math.Abs(layer.ActualHeight-pane.ActualHeight)>0.01))
                throw new InvalidOperationException("Pane material and outline must meet the shadow at the same outer bounds");
        }
        double scale=Native.GetDpiForWindow(panel.Handle)/96d;
        var region=Native.CreateRectRgn(0,0,0,0);
        if(region==0)throw new InvalidOperationException("Cannot allocate diagnostic region");
        nint Hit(double x,double y)
        {int sx=origin.X+(int)Math.Round(x*scale),sy=origin.Y+(int)Math.Round(y*scale);return Native.SendMessage(panel.Handle,0x84,0,(nint)((sy&0xffff)<<16|(sx&0xffff)));}
        try
        {
            if(Native.GetWindowRgn(panel.Handle,region)!=0)throw new InvalidOperationException("Directly drawn panes must not install a native clipping region");
            if(Hit(root.ActualWidth/2,5)!=-1||Hit(5,root.ActualHeight/2)!=-1)throw new InvalidOperationException("Transparent padding intercepts input");
            foreach(var pane in new[]{panel.ToolbarSurface,panel.MessageSurface})
            {
                var position=pane.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                if(Hit(position.X+pane.ActualWidth/2,position.Y+pane.ActualHeight/2)==-1||Hit(position.X+1,position.Y+1)!=-1)
                    throw new InvalidOperationException("Pane input hit testing does not follow its directly drawn rounded rectangle");
            }
            var toolbar=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            var message=panel.MessageSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            if(Hit(toolbar.X+panel.ToolbarSurface.ActualWidth/2,(toolbar.Y+panel.ToolbarSurface.ActualHeight+message.Y)/2)!=-1)
                throw new InvalidOperationException("Transparent pane gap intercepts input");
        }
        finally{Native.DeleteObject(region);}
    }

    private static async Task VerifyToolbarExpansionPathAsync(FloatingPanelWindow panel,Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke,bool systemMotion)
    {
        var root=(FrameworkElement)panel.Content;
        var before=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        Native.GetWindowRect(panel.Handle,out var window);double width=panel.ToolbarSurface.ActualWidth;
        invoke.Invoke();int frames=0;var deadline=DateTime.UtcNow.AddSeconds(3);
        do
        {
            root.UpdateLayout();var origin=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            Native.GetWindowRect(panel.Handle,out var current);
            if(Math.Abs(origin.X-before.X)>1||Math.Abs(origin.Y-before.Y)>1||current.Left!=window.Left||current.Top!=window.Top||panel.ToolbarSurface.ActualWidth<width-1)
                throw new InvalidOperationException("Toolbar expansion moved its origin or rebounded in the wrong direction");
            width=panel.ToolbarSurface.ActualWidth;frames++;
            if(!panel.IsTransitioning)break;await Task.Delay(15);
        }while(DateTime.UtcNow<deadline);
        if(panel.IsTransitioning||systemMotion&&frames<2)throw new InvalidOperationException("Toolbar expansion path was not observed");
    }

    private static async Task VerifyHostTransparencyAsync(FloatingPanelWindow panel,string output)
    {
        var original=panel.Appearance;var root=(FrameworkElement)panel.Content;var foreground=Native.GetForegroundWindow();var originalTheme=root.RequestedTheme;
        int perimeterChecks=0,paneChecks=0,cornerChecks=0;
        uint fixtureColor=0;
        Native.WindowProc procedure=(window,message,w,l)=>
        {
            if(message==0x14)return 1;
            if(message!=0x0f)return Native.DefWindowProc(window,message,w,l);
            var dc=Native.BeginPaint(window,out var paint);nint brush=0;
            try{Native.GetClientRect(window,out var area);brush=Native.CreateSolidBrush(fixtureColor);Native.FillRect(dc,ref area,brush);}
            finally{if(brush!=0)Native.DeleteObject(brush);Native.EndPaint(window,ref paint);}
            return 0;
        };
        var type=new Native.WindowClass{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.WindowClass>(),Proc=procedure,Instance=Native.GetModuleHandle(null),ClassName="AIScreenShot.GeneratedTransparency."+Guid.NewGuid().ToString("N")};
        if(Native.RegisterClassEx(ref type)==0)throw new InvalidOperationException("Cannot register generated transparency fixture");
        var probeHandle=Native.CreateWindowEx(0x08000088,type.ClassName,"",0x80000000,0,0,1,1,0,0,type.Instance,0);
        if(probeHandle==0){Native.UnregisterClass(type.ClassName,type.Instance);throw new InvalidOperationException("Cannot create generated transparency fixture");}
        try
        {
            // Exercise framework style writes rather than merely checking the constructor.
            Native.SetWindowLongPtr(panel.Handle,-16,Native.GetWindowLongPtr(panel.Handle,-16)|0x00c40000);
            Native.SetWindowLongPtr(panel.Handle,-20,Native.GetWindowLongPtr(panel.Handle,-20)|0x00020300);
            Native.SetWindowPos(panel.Handle,0,0,0,0,0,0x01|0x02|0x04|0x10|0x20);
            panel.ApplyAppearance(original with{ShowShadow=false});root.UpdateLayout();VerifyDirectPanelHost(panel);
            Native.GetWindowRect(panel.Handle,out var bounds);var origin=new Native.Point();Native.ClientToScreen(panel.Handle,ref origin);
            if(!Native.SetWindowPos(probeHandle,panel.Handle,bounds.Left-32,bounds.Top-32,bounds.Right-bounds.Left+64,bounds.Bottom-bounds.Top+64,0x10|0x40))throw new InvalidOperationException("Cannot position generated transparency fixture");
            double scale=Native.GetDpiForWindow(panel.Handle)/96d;
            var toolbar=panel.ToolbarSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            var message=panel.MessageSurface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            var points=new[]{new Windows.Foundation.Point(root.ActualWidth/2,5),new(5,root.ActualHeight/2),new(toolbar.X+1,toolbar.Y+1),new(toolbar.X+panel.ToolbarSurface.ActualWidth/2,(toolbar.Y+panel.ToolbarSurface.ActualHeight+message.Y)/2)};
            foreach(var color in new[]{Windows.UI.Color.FromArgb(255,36,86,166),Windows.UI.Color.FromArgb(255,201,125,55)})
            {
                fixtureColor=(uint)(color.R|(color.G<<8)|(color.B<<16));Native.InvalidateRect(probeHandle,0,true);await Task.Delay(120);Native.DwmFlush();
                nint screen=Native.GetDC(0);
                if(screen==0)throw new InvalidOperationException("Cannot read generated fixture pixels");
                try
                {
                    var edgePoints=Enumerable.Range(0,25).SelectMany(index=>new[]{
                        new Native.Point{X=bounds.Left,Y=bounds.Top+(bounds.Bottom-bounds.Top-1)*index/24},new(){X=bounds.Right-1,Y=bounds.Top+(bounds.Bottom-bounds.Top-1)*index/24},
                        new(){X=bounds.Left+(bounds.Right-bounds.Left-1)*index/24,Y=bounds.Top},new(){X=bounds.Left+(bounds.Right-bounds.Left-1)*index/24,Y=bounds.Bottom-1}});
                    foreach(var point in points.Select(point=>new Native.Point{X=origin.X+(int)Math.Round(point.X*scale),Y=origin.Y+(int)Math.Round(point.Y*scale)}).Concat(edgePoints))
                    {
                        uint pixel=Native.GetPixel(screen,point.X,point.Y);
                        if(pixel==uint.MaxValue||Math.Abs((int)(pixel&255)-color.R)>12||Math.Abs((int)((pixel>>8)&255)-color.G)>12||Math.Abs((int)((pixel>>16)&255)-color.B)>12)
                            throw new InvalidOperationException($"Transparent host obscures generated fixture at ({point.X-bounds.Left},{point.Y-bounds.Top}): expected {color.R}/{color.G}/{color.B}, observed {pixel&255}/{(pixel>>8)&255}/{(pixel>>16)&255}; styles {Native.GetWindowLongPtr(panel.Handle,-16):X}/{Native.GetWindowLongPtr(panel.Handle,-20):X}");
                        perimeterChecks++;
                    }
                }
                finally{Native.ReleaseDC(0,screen);}
            }
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            foreach(double borderWidth in new[]{0d,0.5,1,3})
            {
                root.RequestedTheme=theme;
                panel.ApplyAppearance(original with{ShowBorders=borderWidth>0,BorderThickness=Math.Max(0.5,borderWidth),ShowShadow=true,ShadowSpread=12,ShadowOpacity=0.25,AccentMix=0,Toolbar=new(ScreenShot.Core.SurfaceMaterial.Transparent,1),Messages=new(ScreenShot.Core.SurfaceMaterial.Transparent,1)});
                await Task.Delay(120);root.UpdateLayout();VerifyDirectPanelHost(panel);Native.DwmFlush();
                Native.GetWindowRect(panel.Handle,out bounds);Native.GetWindowRect(panel.ShadowWindow.Handle,out var shadowBounds);
                nint screen=Native.GetDC(0);
                if(screen==0)throw new InvalidOperationException("Cannot inspect generated pane boundary pixels");
                try
                {
                    foreach(var pair in panel.PaneShapes.Zip(panel.MaterialBackdrops))
                    {
                        var shape=pair.First;var tint=pair.Second.TintColor;
                        int x=(shape.Left+shape.Right)/2;
                        int bodyInset=(int)Math.Ceiling(borderWidth*scale)+1;
                        uint body=Native.GetPixel(screen,bounds.Left+x,bounds.Top+shape.Top+bodyInset);
                        if(Math.Abs((int)(body&255)-tint.R)>12||Math.Abs((int)((body>>8)&255)-tint.G)>12||Math.Abs((int)((body>>16)&255)-tint.B)>12)
                            throw new InvalidOperationException("Pane body does not fill its shared drawing bounds");
                        // The first inside pixel belongs to the body or inward outline;
                        // the immediately adjacent outside pixel belongs to the shadow.
                        uint edge=Native.GetPixel(screen,bounds.Left+x,bounds.Top+shape.Top);
                        int shadowX=bounds.Left+x-shadowBounds.Left,shadowY=bounds.Top+shape.Top-1-shadowBounds.Top;
                        byte alpha=panel.ShadowWindow.LastPixels[(shadowY*(shadowBounds.Right-shadowBounds.Left)+shadowX)*4+3];
                        if(alpha==0||Math.Max(Math.Abs((int)(edge&255)-(int)(fixtureColor&255)),Math.Max(Math.Abs((int)((edge>>8)&255)-(int)((fixtureColor>>8)&255)),Math.Abs((int)((edge>>16)&255)-(int)((fixtureColor>>16)&255))))<20)
                            throw new InvalidOperationException("A clear seam separates the pane body/outline from its shadow");
                        if(borderWidth==0&&edge!=body)throw new InvalidOperationException("Borderless material remains inset from its shadow boundary");
                        paneChecks++;
                        double radius=shape.Diameter/2d;
                        foreach(int sx in new[]{-1,1})foreach(int sy in new[]{-1,1})
                        {
                            double cx=sx<0?shape.Left+radius:shape.Right-radius,cy=sy<0?shape.Top+radius:shape.Bottom-radius;
                            Native.Point NearCorner(double distance)
                            {
                                int px=(int)Math.Floor(cx+sx*(radius+distance)/Math.Sqrt(2)),py=(int)Math.Floor(cy+sy*(radius+distance)/Math.Sqrt(2));
                                return Enumerable.Range(px-1,3).SelectMany(x=>Enumerable.Range(py-1,3).Select(y=>new Native.Point{X=x,Y=y}))
                                    .MinBy(point=>Math.Abs(Math.Sqrt(Math.Pow(point.X+0.5-cx,2)+Math.Pow(point.Y+0.5-cy,2))-radius-distance));
                            }
                            var inside=NearCorner(-0.9);var outside=NearCorner(0.9);
                            uint corner=Native.GetPixel(screen,bounds.Left+inside.X,bounds.Top+inside.Y);
                            int ox=bounds.Left+outside.X-shadowBounds.Left,oy=bounds.Top+outside.Y-shadowBounds.Top;
                            byte cornerAlpha=panel.ShadowWindow.LastPixels[(oy*(shadowBounds.Right-shadowBounds.Left)+ox)*4+3];
                            if(cornerAlpha==0||Math.Max(Math.Abs((int)(corner&255)-(int)(fixtureColor&255)),Math.Max(Math.Abs((int)((corner>>8)&255)-(int)((fixtureColor>>8)&255)),Math.Abs((int)((corner>>16)&255)-(int)((fixtureColor>>16)&255))))<20)
                                throw new InvalidOperationException("A clear seam remains at a rounded pane corner");
                            cornerChecks++;
                        }
                    }
                }
                finally{Native.ReleaseDC(0,screen);}
            }
            if(Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Generated transparency check activated a window");
            await File.WriteAllTextAsync(Path.Combine(output,"panel-boundaries.txt"),$"PASS: {perimeterChecks} DWM fixture samples include all four HWND edges, padding, corner and pane gap after attempted native frame restoration.\nPASS: {paneChecks} straight and {cornerChecks} rounded Light/Dark pane edge cases with outlines off / 0.5 / 1 / 3px verify adjacent outer shadow and inner body/outline pixels, plus common material/outline drawing bounds.\nOnly pixels over a generated application-owned color fixture were sampled; no desktop image was captured.\n");
        }
        finally{Native.DestroyWindow(probeHandle);Native.UnregisterClass(type.ClassName,type.Instance);GC.KeepAlive(procedure);panel.ApplyAppearance(original);root.RequestedTheme=originalTheme;}
    }

    private static async Task VerifyPaneExitPathAsync(FloatingPanelWindow panel,bool systemMotion)
    {
        var root=(FrameworkElement)panel.Content;
        async Task Exit(Action action,bool wholeWindow)
        {
            var initial=PaneDrawingBounds(panel);double initialHeight=panel.MessageSurface.ActualHeight;
            action();int frames=0;bool synchronized=false;var deadline=DateTime.UtcNow.AddSeconds(3);
            do
            {
                root.UpdateLayout();var bounds=PaneDrawingBounds(panel);
                if(panel.IsTransitioning&&panel.MessageSurface.ActualHeight<initialHeight-2)
                {
                    if(bounds.Bottom>=initial.Bottom-1)throw new InvalidOperationException("Pane geometry waits for exit motion to finish");
                    var panes=new[]{panel.ToolbarSurface,panel.MessageSurface}.Where(pane=>pane.Visibility==Visibility.Visible&&pane.Height>=0.5).ToArray();
                    if(panes.Length>0)
                    {
                        double scale=Native.GetDpiForWindow(panel.Handle)/96d;
                        Native.GetWindowRect(panel.Handle,out var nativeBounds);var clientOrigin=new Native.Point();Native.ClientToScreen(panel.Handle,ref clientOrigin);
                        double bottom=panes.Max(pane=>pane.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y+pane.ActualHeight)*scale+clientOrigin.Y-nativeBounds.Top;
                        if(Math.Abs(bounds.Bottom-bottom)>2*scale)throw new InvalidOperationException("Shadow geometry trails the pane exit");
                    }
                    synchronized=true;
                }
                frames++;if(!panel.IsTransitioning)break;await Task.Delay(15);
            }while(DateTime.UtcNow<deadline);
            if(panel.IsTransitioning||systemMotion&&(!synchronized||frames<2))throw new InvalidOperationException("Pane exit motion was not observed");
            if(wholeWindow&&Native.IsWindowVisible(panel.Handle))throw new InvalidOperationException("Whole-window exit did not hide its HWND");
        }
        await Exit(panel.ToggleMessage,false);panel.ToggleMessage();
        while(panel.IsTransitioning)await Task.Delay(15);
        await Exit(panel.Collapse,true);
    }

    private static async Task<string> VerifyMaterialsAsync(FloatingPanelWindow panel)
    {
        var root=(FrameworkElement)panel.Content;var previous=panel.Appearance;
        var previousTheme=root.RequestedTheme;var foreground=Native.GetForegroundWindow();
        string materialState="";
        Windows.UI.Color? lightTint=null;
        var elements=Descendants<SystemBackdropElement>(root).ToArray();
        if(elements.Length!=2||elements.Any(element=>element.IsHitTestVisible)||root.Opacity!=1)
            throw new InvalidOperationException("Independent native surfaces must not intercept input or fade content");
        try
        {
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                root.RequestedTheme=theme;await Task.Delay(150);
                if(theme==ElementTheme.Light)lightTint=panel.MaterialBackdrops[0].TintColor;
                else if(lightTint==panel.MaterialBackdrops[0].TintColor)throw new InvalidOperationException("Material tint failed to follow the theme resources");
                foreach(var material in Enum.GetValues<ScreenShot.Core.SurfaceMaterial>())
                {
                    panel.ApplyAppearance(previous with{Toolbar=new(material,0.37),Messages=new(material,1)});await Task.Delay(120);
                    var backdrops=panel.MaterialBackdrops;
                    if(backdrops.Any(item=>!item.IsConnected||!item.IsAlwaysActive||item.Theme!=(theme==ElementTheme.Dark?Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark:Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light)))
                        throw new InvalidOperationException($"A backdrop lost connection, theme, or persistent activation: root={root.ActualTheme}, requested={theme}; targets="+string.Join(';',backdrops.Select(item=>item.IsConnected+"/"+item.IsAlwaysActive+"/"+item.Theme)));
                    if(backdrops[0].Appearance.Opacity!=0.37||backdrops[1].Appearance.Opacity!=1||backdrops.Any(item=>item.Appearance.Material!=material))
                        throw new InvalidOperationException("Surface appearance values are not independent");
                    bool acrylic=material!=ScreenShot.Core.SurfaceMaterial.Transparent&&Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
                    if(backdrops.Any(item=>item.UsesAcrylic!=acrylic)||root.Opacity!=1||panel.ComposerSendButton.Opacity!=1)
                        throw new InvalidOperationException("Changing material faded content or failed to replace the controller");
                }
            }
            if(Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Live material updates changed foreground focus");
        }
        finally
        {panel.ApplyAppearance(previous);root.RequestedTheme=previousTheme;}
        await Task.Delay(150);
        if(Native.GetForegroundWindow()!=panel.Handle&&panel.MaterialBackdrops.Any(item=>item.TargetHasInputFocus||!item.IsAlwaysActive))
            throw new InvalidOperationException("An inactive target cannot retain independent material activation");
        materialState=string.Join("/",panel.MaterialBackdrops.Select(item=>item.State?.ToString()??"Tint"));
        return "PASS: two independent native backdrop targets switch Acrylic/Thin/transparent and opacity 37/100 in both themes; text/buttons stay opaque; material updates retain foreground and IsInputActive\n"+
            $"PASS: inactive native targets retain material activation; current OS material states: {materialState}\n";
    }

    internal static async Task<string> VerifySettingsDesignAsync(SettingsWindow window,string output)
    {
        var root=(Border)window.Content;var navigation=(NavigationView)root.Child;
        var items=navigation.MenuItems.OfType<NavigationViewItem>().ToArray();
        if(!items.Select(item=>item.Tag?.ToString()).SequenceEqual(new[]{"general","appearance","codex","agent","about"}))
            throw new InvalidOperationException("Settings categories are incomplete");
        var pageScroller=(ScrollViewer)((Grid)navigation.Content).Children[1];
        navigation.SelectedItem=items.Single(item=>Equals(item.Tag,"agent"));await Task.Delay(120);
        var agentPage=(StackPanel)pageScroller.Content;
        var modelEditor=Descendants<TextBox>(agentPage).Single(box=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box)=="Model name");
        if(!Descendants<TextBox>(agentPage).Any(box=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box)=="Base URL")||
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(Descendants<PasswordBox>(agentPage).Single())!="API key")
            throw new InvalidOperationException("Agent field labels are not professional display names");
        string agentDraft=modelEditor.Text;modelEditor.Text="unsaved-agent-draft";
        if(Descendants<PasswordBox>(agentPage).Count()!=1||Descendants<NumberBox>(agentPage).Count()!=1||Descendants<ToggleSwitch>(agentPage).Count()!=1)
            throw new InvalidOperationException("Agent configuration controls are incomplete");
        navigation.SelectedItem=items[0];navigation.SelectedItem=items.Single(item=>Equals(item.Tag,"agent"));
        if(modelEditor.Text!="unsaved-agent-draft")throw new InvalidOperationException("Agent draft was discarded on navigation");
        modelEditor.Text=agentDraft;

        var originalSize=window.AppWindow.Size;string draft=window.DefaultPromptEditor.Text;
        navigation.SelectedItem=items.Single(item=>Equals(item.Tag,"appearance"));await Task.Delay(120);
        var appearancePage=(StackPanel)pageScroller.Content;
        if(Descendants<CheckBox>(appearancePage).Any()||Descendants<TextBlock>(appearancePage).Any(text=>text.Text.Contains("底板")))throw new InvalidOperationException("Retired window base settings are still visible");
        var animations=Descendants<ToggleSwitch>(appearancePage).Single(toggle=>toggle!=window.BorderToggle&&toggle!=window.ShadowToggle);
        bool previousMotion=animations.IsOn;
        var sliders=Descendants<Slider>(appearancePage).Where(slider=>slider!=window.BorderWidthSlider&&slider!=window.ShadowSpreadSlider&&slider!=window.ShadowOpacitySlider&&slider!=window.AccentMixSlider).ToArray();
        if(sliders.Length!=2||Descendants<ComboBox>(appearancePage).Count()!=4)throw new InvalidOperationException("Appearance settings require theme/accent selectors and exactly two material editors");
        var toolbarOpacity=sliders.Single(slider=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(slider)=="工具条底色不透明度");
        double previousOpacity=toolbarOpacity.Value;toolbarOpacity.Value=23;
        animations.IsOn=false;
        if(window.AppearanceDraft.EnableAnimations||window.AppearanceDraft.Toolbar.Opacity!=0.23||sliders.Any(editor=>!editor.IsEnabled))
            throw new InvalidOperationException("Pane material and animation controls failed to update the draft");
        var appearanceDraft=window.AppearanceDraft;
        Windows.UI.Color? lightColor=null;
        foreach(bool compact in new[]{false,true})
        {
            if(compact)
            {
                double scale=Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window))/96d;
                window.AppWindow.Resize(new((int)Math.Round(520*scale),(int)Math.Round(640*scale)));
            }
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                root.RequestedTheme=theme;
                foreach(var item in items)
                {
                    navigation.SelectedItem=item;await Task.Delay(120);root.UpdateLayout();
                    var content=(StackPanel)pageScroller.Content;
                    foreach(var surface in content.Children.OfType<Border>())
                    {
                        var surfacePosition=surface.TransformToVisual(pageScroller).TransformPoint(new Windows.Foundation.Point());
                        if(surfacePosition.X< -1||surfacePosition.X+surface.ActualWidth>pageScroller.ActualWidth+1)
                            throw new InvalidOperationException($"A settings row exceeds the viewport (x={surfacePosition.X:F1}, width={surface.ActualWidth:F1}, viewport={pageScroller.ActualWidth:F1})");
                        var row=(Grid)surface.Child;var control=(FrameworkElement)row.Children[1];
                        var position=control.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
                        if(position.X< -1||position.X+control.ActualWidth>row.ActualWidth+1)
                            throw new InvalidOperationException("A settings editor exceeds its row");
                        if(control is Grid modifiers)
                        {
                            foreach(var check in modifiers.Children.OfType<CheckBox>())
                            {
                                var checkPosition=check.TransformToVisual(modifiers).TransformPoint(new Windows.Foundation.Point());
                                if(checkPosition.X+check.ActualWidth>modifiers.ActualWidth+1)
                                    throw new InvalidOperationException("A shortcut modifier is clipped");
                            }
                        }
                    }
                    if(item.Tag?.ToString()=="general")
                    {
                        var caption=Descendants<TextBlock>(content).First(text=>text.Style==(Style)Application.Current.Resources["SS.CaptionTextStyle"]);
                        var color=((SolidColorBrush)caption.Foreground).Color;
                        if(theme==ElementTheme.Light)lightColor=color;
                        else if(lightColor==color)throw new InvalidOperationException("Settings captions did not follow the theme");
                    }
                    // RenderTargetBitmap excludes Mica; use its theme fallback only in generated previews.
                    var fallback=(Border)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" RequestedTheme=\"{theme}\" Background=\"{{ThemeResource SolidBackgroundFillColorBaseBrush}}\" />");
                    var background=root.Background;root.Background=fallback.Background;
                    try{await File.WriteAllBytesAsync(Path.Combine(output,$"settings-{item.Tag}-{(compact?"compact-":"")}{theme}.png"),await RenderAsync(root));}
                    finally{root.Background=background;}
                }
            }
        }
        navigation.SelectedItem=items[0];window.AppWindow.Resize(originalSize);
        if(window.DefaultPromptEditor.Text!=draft)throw new InvalidOperationException("Settings navigation discarded the unsaved prompt");
        if(window.AppearanceDraft!=appearanceDraft)throw new InvalidOperationException("Settings navigation discarded the material draft");
        toolbarOpacity.Value=previousOpacity;
        animations.IsOn=previousMotion;
        string themeChecks=await VerifyThemeSettingsAsync(window,output);
        return themeChecks+"PASS: five native settings categories render in Light/Dark and 520px compact layout without clipped editors or shortcut modifiers\nPASS: settings theme resources update and page navigation retains unsaved prompt and appearance drafts; theme/accent selectors and exactly two native material editors are present with no base settings\n";
    }

    private static async Task<string> VerifyThemeSettingsAsync(SettingsWindow window,string output)
    {
        var app=(App)Application.Current;var original=window.AppearanceDraft;
        var root=(FrameworkElement)window.Content;var navigation=(NavigationView)((Border)root).Child;
        navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().Single(item=>item.Tag?.ToString()=="appearance");
        var foreground=Native.GetForegroundWindow();
        var save=Descendants<Button>(root).Single(button=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)=="保存外观");
        var choices=new[]{ScreenShot.Core.AppThemeMode.Light,ScreenShot.Core.AppThemeMode.Dark,ScreenShot.Core.AppThemeMode.System};
        try
        {
            for(int index=0;index<choices.Length;index++)
            {
                window.ThemeSelector.SelectedIndex=index;window.AccentSelector.SelectedIndex=1;
                window.AccentPicker.Color=AppThemeController.ParseColor("#C64ED6");await Task.Delay(100);root.UpdateLayout();
                var expected=choices[index] switch{ScreenShot.Core.AppThemeMode.Light=>ElementTheme.Light,ScreenShot.Core.AppThemeMode.Dark=>ElementTheme.Dark,_=>app.Themes.EffectiveTheme};
                foreach(var target in new[]{root,(FrameworkElement)app.Panel!.Content,(FrameworkElement)app.Ball!.Content})
                    if(target.RequestedTheme!=expected)throw new InvalidOperationException("Application theme was not propagated to every window");
                if(window.AppearanceDraft.Theme!=choices[index]||window.AppearanceDraft.AccentMode!=ScreenShot.Core.AccentColorMode.Custom||window.AppearanceDraft.CustomAccentColor!="#C64ED6")
                    throw new InvalidOperationException("Native theme/accent controls did not update their draft");
                if(save.Background is not SolidColorBrush background||background.Color!=window.AccentPicker.Color)
                    throw new InvalidOperationException("Native primary button did not use the scoped custom accent");
                if(save.Foreground is not SolidColorBrush text||AppThemeController.Contrast(background.Color,text.Color)<4.5)
                    throw new InvalidOperationException("Custom primary button text contrast is insufficient");
                foreach(var stroke in app.Panel!.EdgeStrokes)
                    if(stroke.BorderBrush is not SolidColorBrush border||border.Color!=window.AccentPicker.Color||stroke.IsHitTestVisible)
                        throw new InvalidOperationException("Panel outline did not follow the exact custom accent or intercepted input");
                var dictionaries=root.Resources.MergedDictionaries.Last().ThemeDictionaries;
                if(((ResourceDictionary)dictionaries["HighContrast"]).Count!=0)throw new InvalidOperationException("Custom accents override system high contrast resources");
                await File.WriteAllBytesAsync(Path.Combine(output,$"settings-theme-{choices[index]}.png"),await RenderAsync(root));
                if(app.Panel.MaterialBackdrops.Any(item=>item.Theme!=(root.ActualTheme==ElementTheme.Dark?Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark:Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light)))
                    throw new InvalidOperationException("Application theme and floating materials diverged");
            }
            var pickerFlyout=(Flyout)window.AccentPickerButton.Flyout;
            pickerFlyout.ShowAt(window.AccentPickerButton);await Task.Delay(120);
            if(!pickerFlyout.IsOpen||window.AccentPicker.ActualWidth<=0||window.AccentPicker.IsAlphaEnabled||!window.AccentPicker.IsHexInputVisible)
                throw new InvalidOperationException("Custom accent editor is not a usable opaque native ColorPicker");
            window.AccentPicker.Color=AppThemeController.ParseColor("#37B99A");await Task.Delay(80);pickerFlyout.Hide();
            if(window.AppearanceDraft.CustomAccentColor!="#37B99A"||app.Themes.AccentColor!=window.AccentPicker.Color)
                throw new InvalidOperationException("Native color editing did not update the live accent");
            foreach(var color in new[]{"#FFFFFF","#000000"})
            {
                window.AccentPicker.Color=AppThemeController.ParseColor(color);await Task.Delay(60);
                if(AppThemeController.Contrast(((SolidColorBrush)save.Background).Color,((SolidColorBrush)save.Foreground).Color)<4.5)
                    throw new InvalidOperationException("Black/white custom accents lost readable button text");
            }
            window.AccentSelector.SelectedIndex=0;await Task.Delay(80);
            var system=new Windows.UI.ViewManagement.UISettings();
            var nativeAccent=system.GetColorValue(root.ActualTheme==ElementTheme.Dark?Windows.UI.ViewManagement.UIColorType.AccentLight2:Windows.UI.ViewManagement.UIColorType.AccentDark1);
            if(window.AccentPickerButton.IsEnabled||((SolidColorBrush)save.Background).Color!=nativeAccent||app.Themes.AccentColor!=system.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent))
                throw new InvalidOperationException("Following Windows retained a custom accent or editable color picker");
            foreach(var stroke in app.Panel!.EdgeStrokes)
                if(((SolidColorBrush)stroke.BorderBrush).Color!=app.Themes.AccentColor)throw new InvalidOperationException("Panel outline retained a custom color in Windows mode");
            Native.GetWindowRect(app.Panel.Handle,out var borderBounds);
            var paneSize=(app.Panel.ToolbarSurface.ActualWidth,app.Panel.MessageSurface.ActualHeight);
            window.BorderToggle.IsOn=true;
            foreach(double width in new[]{0.5,1,3})
            {
                window.BorderWidthSlider.Value=width;await Task.Delay(70);root.UpdateLayout();
                if(app.Panel.EdgeStrokes.Any(stroke=>stroke.BorderThickness!=new Thickness(width))||window.AppearanceDraft.BorderThickness!=width)
                    throw new InvalidOperationException("Native border slider did not update every panel outline");
            }
            window.BorderToggle.IsOn=false;await Task.Delay(70);
            if(window.BorderWidthSlider.IsEnabled||app.Panel.EdgeStrokes.Any(stroke=>stroke.BorderThickness!=new Thickness(0))||window.AppearanceDraft.ShowBorders)
                throw new InvalidOperationException("Disabling panel borders left an outline or active width editor");
            window.BorderToggle.IsOn=true;await Task.Delay(70);
            Native.GetWindowRect(app.Panel.Handle,out var afterBorderBounds);
            if(app.Panel.EdgeStrokes.Any(stroke=>stroke.BorderThickness!=new Thickness(3))||borderBounds.Left!=afterBorderBounds.Left||borderBounds.Top!=afterBorderBounds.Top||borderBounds.Right!=afterBorderBounds.Right||borderBounds.Bottom!=afterBorderBounds.Bottom||paneSize!=(app.Panel.ToolbarSurface.ActualWidth,app.Panel.MessageSurface.ActualHeight))
                throw new InvalidOperationException("Border toggling lost width or changed panel geometry");
            if(Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException($"Theme/accent preview changed foreground focus: before=0x{foreground:X}, after=0x{Native.GetForegroundWindow():X}, settings=0x{WinRT.Interop.WindowNative.GetWindowHandle(window):X}, panel=0x{app.Panel.Handle:X}");
            await VerifySurfaceEffectsAsync(window,app.Panel,output);
        }
        finally
        {
            window.AccentPickerButton.Flyout.Hide();
            window.ThemeSelector.SelectedIndex=Array.IndexOf(choices,original.Theme);
            window.AccentSelector.SelectedIndex=(int)original.AccentMode;window.AccentPicker.Color=AppThemeController.ParseColor(original.CustomAccentColor);
            window.BorderToggle.IsOn=original.ShowBorders;window.BorderWidthSlider.Value=original.BorderThickness;
            window.ShadowToggle.IsOn=original.ShowShadow;window.ShadowSpreadSlider.Value=original.ShadowSpread;window.ShadowOpacitySlider.Value=original.ShadowOpacity*100;window.AccentMixSlider.Value=original.AccentMix*100;
        }
        return "PASS: executable embedded icon, native window icons, tray icon loading and floating entrance artwork use the approved assets\n"+
            "PASS: two rounded panes draw over a transparent unclipped HWND; native shadow is outside-only, click-through, nonactivating and follows hide/restore/move; tint mixing changes real material/fallback colors\n"+
            "PASS: outlines follow custom/Windows accent in all themes; native border toggle/0.5-3px slider preserve width and panel geometry without intercepting input\n"+
            "PASS: native Light/Dark/System selectors update settings/ball/panel and material configuration; scoped custom accents reach primary buttons with readable text, including black/white\n"+
            "PASS: native RGB/HEX ColorPicker edits preview live; Windows mode restores native accent shades and disables custom editing; high contrast keeps system resources; preview retains foreground\n";
    }

    private static async Task VerifySurfaceEffectsAsync(SettingsWindow window,FloatingPanelWindow panel,string output)
    {
        var app=(App)Application.Current;var foreground=Native.GetForegroundWindow();
        window.AccentSelector.SelectedIndex=1;window.AccentPicker.Color=AppThemeController.ParseColor("#EDA8BA");
        foreach(int theme in new[]{0,1})
        {
            window.ThemeSelector.SelectedIndex=theme;
            foreach(var material in Enum.GetValues<ScreenShot.Core.SurfaceMaterial>())
            {
                window.AccentMixSlider.Value=0;app.ApplyAppearance(window.AppearanceDraft with{Toolbar=new(material,0.6),Messages=new(material,0.6)});await Task.Delay(60);
                var neutral=panel.MaterialBackdrops.Select(item=>item.TintColor).ToArray();
                foreach(int amount in new[]{35,100})
                {
                    window.AccentMixSlider.Value=amount;app.ApplyAppearance(window.AppearanceDraft with{Toolbar=new(material,0.6),Messages=new(material,0.6)});await Task.Delay(60);
                    for(int index=0;index<panel.MaterialBackdrops.Count;index++)
                    {
                        var actual=panel.MaterialBackdrops[index].TintColor;var accent=window.AccentPicker.Color;
                        var expected=Windows.UI.Color.FromArgb(255,(byte)Math.Round(neutral[index].R+(accent.R-neutral[index].R)*amount/100d),(byte)Math.Round(neutral[index].G+(accent.G-neutral[index].G)*amount/100d),(byte)Math.Round(neutral[index].B+(accent.B-neutral[index].B)*amount/100d));
                        if(actual!=expected)throw new InvalidOperationException("Material mixing does not match the requested RGB proportion");
                    }
                }
            }
        }
        window.AccentMixSlider.Value=35;window.ShadowToggle.IsOn=true;window.ShadowSpreadSlider.Value=24;window.ShadowOpacitySlider.Value=25;
        app.ApplyAppearance(window.AppearanceDraft with{ShowBorders=false});panel.Open();await Task.Delay(180);
        VerifyDirectPanelHost(panel);
        if(!Native.IsWindowVisible(panel.ShadowWindow.Handle)||panel.ShadowWindow.LastPixels.All(value=>value==0)||Native.SendMessage(panel.ShadowWindow.Handle,0x84,0,0)!=-1||(Native.GetWindowLongPtr(panel.ShadowWindow.Handle,-20).ToInt64()&0x080800a0)!=0x080800a0)
            throw new InvalidOperationException("Native shadow did not render or lacks click-through/no-activation styles");
        window.ShadowToggle.IsOn=false;await Task.Delay(60);
        if(Native.IsWindowVisible(panel.ShadowWindow.Handle)||window.ShadowSpreadSlider.IsEnabled||window.ShadowOpacitySlider.IsEnabled)throw new InvalidOperationException("Shadow toggle failed to hide the native layer or disable editors");
        window.ShadowToggle.IsOn=true;await Task.Delay(60);
        if(!Native.IsWindowVisible(panel.ShadowWindow.Handle)||window.AppearanceDraft.ShadowOpacity!=0.25||window.AppearanceDraft.ShadowSpread!=24)throw new InvalidOperationException("Shadow preferences did not survive toggling");
        Native.GetWindowRect(panel.ShadowWindow.Handle,out var before);
        var title=panel.MessageDragTitle;var titleOrigin=title.TransformToVisual((UIElement)panel.Content).TransformPoint(new Windows.Foundation.Point());
        if(!panel.MoveAtForDiagnostic(new(titleOrigin.X+title.ActualWidth/2,titleOrigin.Y+title.ActualHeight/2),-8,-7))throw new InvalidOperationException("Shadow movement could not use the existing title drag area");
        Native.GetWindowRect(panel.ShadowWindow.Handle,out var after);
        if(after.Left-before.Left!=-8||after.Top-before.Top!=-7)throw new InvalidOperationException("Native shadow lagged behind panel movement");
        panel.HideForOperation();if(Native.IsWindowVisible(panel.ShadowWindow.Handle))throw new InvalidOperationException("Screenshot pre-hide leaves the shadow visible");
        panel.RestoreAfterOperation();await Task.Delay(180);
        if(!Native.IsWindowVisible(panel.ShadowWindow.Handle)||Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("Shadow restore changes foreground or fails to reappear");
        await File.WriteAllTextAsync(Path.Combine(output,"panel-effects.txt"),$"PASS: native shadow render {System.Diagnostics.Stopwatch.GetElapsedTime(0,panel.ShadowWindow.LastRenderTicks).TotalMilliseconds:F2}ms, mask {System.Diagnostics.Stopwatch.GetElapsedTime(0,panel.ShadowWindow.LastMaskTicks).TotalMilliseconds:F2}ms; current premultiplied mask {panel.ShadowWindow.LastPixels.Length} bytes; shadow renderer never samples desktop pixels.");
    }

    private static void VerifyAppIcons(FloatingPanelWindow panel,FloatingBallWindow ball)
    {
        foreach(var handle in new[]{panel.Handle,WinRT.Interop.WindowNative.GetWindowHandle(ball)})
            if(Native.SendMessage(handle,0x7f,1,0)==0)throw new InvalidOperationException("Native window icon was not assigned");
        nint tray=AppIcon.LoadTrayIcon();try{if(tray==Native.LoadIcon(0,32512))throw new InvalidOperationException("Tray still uses the generic icon");}finally{Native.DestroyIcon(tray);}
        nint large=0,small=0;
        try{if(Native.ExtractIconEx(Environment.ProcessPath!,0,out large,out small,1)==0||large==0||small==0)throw new InvalidOperationException("Executable is missing embedded application icons");}
        finally{if(large!=0)Native.DestroyIcon(large);if(small!=0)Native.DestroyIcon(small);}
        if(!Descendants<Image>((FrameworkElement)ball.Content).Any(image=>image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap&&bitmap.UriSource?.LocalPath.EndsWith("AppIcon.png",StringComparison.OrdinalIgnoreCase)==true))
            throw new InvalidOperationException("Floating entrance lost the approved artwork");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        var pending=new Stack<DependencyObject>();pending.Push(root);
        while(pending.TryPop(out var element))
        {
            if(element is T match)yield return match;
            for(int index=0;index<VisualTreeHelper.GetChildrenCount(element);index++)pending.Push(VisualTreeHelper.GetChild(element,index));
        }
    }

    public static async Task<string> VerifyClipboardBackupAsync()
    {
        int checks=0;
        void Check(bool condition,string description){if(!condition)throw new InvalidOperationException(description);checks++;}
        byte[] png=await FixtureAsync();
        using var original=new InMemoryRandomAccessStream();
        using(var writer=new DataWriter(original.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync();}
        original.Seek(0);var reference=RandomAccessStreamReference.CreateFromStream(original);
        var source=new Windows.ApplicationModel.DataTransfer.DataPackage();
        source.SetText("generated text");source.SetRtf(@"{\rtf1 generated}");
        string html=Windows.ApplicationModel.DataTransfer.HtmlFormatHelper.CreateHtmlFormat("<b>generated</b><img src=\"fixture.png\">");
        source.SetHtmlFormat(html);source.ResourceMap["fixture.png"]=reference;source.SetBitmap(reference);
        source.SetWebLink(new Uri("https://example.invalid/generated"));source.SetApplicationLink(new Uri("aiscreenshot-test:generated"));
        source.SetData("AIScreenShot.Generated.String","custom generated data");
        source.SetData("AIScreenShot.Generated.Stream",original);
        source.RequestedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        using var backup=await ClipboardBackup.ReadAsync(source.GetView(),CancellationToken.None);
        original.Dispose(); // The clone must remain readable after the old owner releases its streams.
        var view=backup.Content.GetView();
        Check(backup.SkippedFormats==0&&source.GetView().AvailableFormats.All(view.Contains),$"Generated formats were not preserved (source={source.GetView().AvailableFormats.Count}, backup={view.AvailableFormats.Count}, skipped={backup.SkippedFormats})");
        Check(await view.GetTextAsync()=="generated text","Text backup");
        Check(await view.GetRtfAsync()==@"{\rtf1 generated}","RTF backup");
        Check(await view.GetHtmlFormatAsync()==html,"HTML backup");
        Check((string)await view.GetDataAsync("AIScreenShot.Generated.String")=="custom generated data","Custom string backup");
        Check((await view.GetWebLinkAsync()).Host=="example.invalid","WebLink backup");
        Check((await view.GetApplicationLinkAsync()).Scheme=="aiscreenshot-test","ApplicationLink backup");
        Check(view.RequestedOperation==Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move,"Requested operation preservation");
        async Task<byte[]> ReadAsync(IRandomAccessStreamReference streamReference)
        {
            using var stream=await streamReference.OpenReadAsync();
            using var reader=new DataReader(stream);await reader.LoadAsync((uint)stream.Size);
            byte[] bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);return bytes;
        }
        Check((await ReadAsync(await view.GetBitmapAsync())).SequenceEqual(png),"Independent bitmap backup");
        var resources=await view.GetResourceMapAsync();
        Check((await ReadAsync(resources["fixture.png"])).SequenceEqual(png),"Independent HTML resource backup");
        var custom=(IRandomAccessStream)await view.GetDataAsync("AIScreenShot.Generated.Stream");
        Check((await ReadAsync(RandomAccessStreamReference.CreateFromStream(custom))).SequenceEqual(png),"Independent custom stream backup");
        using var empty=await ClipboardBackup.ReadAsync(new Windows.ApplicationModel.DataTransfer.DataPackage().GetView(),CancellationToken.None);
        Check(empty.Content.GetView().AvailableFormats.Count==0,"Empty clipboard snapshot");
        return $"PASS: {checks} in-memory WinRT clipboard backup assertions\nPASS: text, RTF, HTML/resources, bitmap, links, custom string/stream, requested operation, empty data\nPASS: copied streams survive original owner release\nSystem clipboard was not read or modified; no Codex message was sent.\n";
    }
    public static string VerifyOverlay()
    {
        const int width=37,height=29;
        var bounds=new ScreenShot.Core.PixelRect(-120,-60,width,height);
        byte[] pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int offset=(y*width+x)*4;
            pixels[offset]=(byte)(x*5);pixels[offset+1]=(byte)(y*7);pixels[offset+2]=(byte)((x+y)*3);pixels[offset+3]=255;
        }
        var capture=new DesktopCapture(bounds,pixels);
        using var process=System.Diagnostics.Process.GetCurrentProcess();
        uint before=Native.GetGuiResources(process.Handle,0);
        int checks=0;
        for(int iteration=0;iteration<3;iteration++)
        {
            using var surface=new OverlaySurface(capture);
            ScreenShot.Core.PixelRect?[] regions=[null,bounds,new(-115,-53,13,11),new(-140,-80,25,26),ScreenShot.Core.PixelRect.FromPoints(-91,-36,-111,-55),new(-84,-32,1,1)];
            foreach(var region in regions)
            {
                surface.Compose(region,false);
                var clipped=region?.Clamp(bounds);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    bool bright=clipped is {} r&&x>=r.X-bounds.X&&x<r.X-bounds.X+r.Width&&y>=r.Y-bounds.Y&&y<r.Y-bounds.Y+r.Height;
                    int offset=(y*width+x)*4,divisor=bright?1:2;
                    uint expected=(uint)((pixels[offset+2]/divisor)|((pixels[offset+1]/divisor)<<8)|((pixels[offset]/divisor)<<16));
                    if(Native.GetPixel(surface.Dc,x,y)!=expected)throw new InvalidOperationException($"Overlay pixel alignment failed at {x},{y}");
                    checks++;
                }
            }
            surface.Compose(new(-115,-53,13,11));
            if(Native.GetPixel(surface.Dc,5,7)!=0x00D77800)throw new InvalidOperationException("Overlay selection border failed");
        }
        uint after=Native.GetGuiResources(process.Handle,0);
        if(before!=after)throw new InvalidOperationException($"Overlay GDI resource leak: {before} -> {after}");
        int incrementalChecks=0;
        using(var incremental=new OverlaySurface(capture))
        using(var reference=new OverlaySurface(capture))
        {
            incremental.Compose(null);
            var random=new Random(42);
            for(int i=0;i<160;i++)
            {
                ScreenShot.Core.PixelRect? region=i%17==0?null:new(bounds.X+random.Next(-10,width+5),bounds.Y+random.Next(-10,height+5),random.Next(1,width+10),random.Next(1,height+10));
                bool border=i%3!=0;
                var changes=incremental.Changes(region,border);
                reference.Compose(region,border);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(Native.GetPixel(incremental.Dc,x,y)!=Native.GetPixel(reference.Dc,x,y)&&!changes.Any(r=>x+bounds.X>=r.X&&x+bounds.X<r.X+r.Width&&y+bounds.Y>=r.Y&&y+bounds.Y<r.Y+r.Height))
                        throw new InvalidOperationException("Overlay dirty regions omitted a changed pixel");
                }
                incremental.UpdateSelection(region,border);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(Native.GetPixel(incremental.Dc,x,y)!=Native.GetPixel(reference.Dc,x,y))throw new InvalidOperationException($"Incremental overlay mismatch at {x},{y}, step {i}");
                    incrementalChecks++;
                }
                if(incremental.Changes(region,border).Count!=0)throw new InvalidOperationException("Unchanged selection requests repaint");
            }
        }
        for(int i=0;i<3;i++)
        {
            var probe=new SelectionOverlay(capture,_=>{});probe.Cancel();probe.Cancel();
        }
        return $"PASS: {checks} GDI pixel assertions (top-down alignment, negative origin, clipping, reverse selection, edge pixel)\nPASS: {incrementalChecks} incremental/reference pixel comparisons and dirty-region coverage\nPASS: selection border and GDI resource release ({before} -> {after})\nPASS: repeated overlay window lifecycle and idempotent cancellation\n"+BenchmarkOverlay();
    }

    private static string BenchmarkOverlay()
    {
        const int width=3840,height=2160,frames=80;
        var capture=new DesktopCapture(new(0,0,width,height),new byte[width*height*4]);
        using var surface=new OverlaySurface(capture);
        var region=new ScreenShot.Core.PixelRect(300,300,2200,1400);
        surface.Compose(region);Native.GdiFlush();
        var next=new ScreenShot.Core.PixelRect(300,300,2201,1401);
        long dirtyArea=surface.Changes(next).Sum(r=>(long)r.Width*r.Height);
        if(dirtyArea>width*(long)height/100)throw new InvalidOperationException("Small drag repaints more than 1% of the 4K desktop");
        var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<frames;i++){surface.Compose(i%2==0?next:region);Native.GdiFlush();}
        double full=timer.Elapsed.TotalMilliseconds/frames;
        surface.Compose(region);Native.GdiFlush();timer.Restart();
        for(int i=0;i<frames;i++){surface.UpdateSelection(i%2==0?next:region);Native.GdiFlush();}
        double incremental=timer.Elapsed.TotalMilliseconds/frames;
        return $"4K synthetic composition benchmark ({frames} frames, GdiFlush): full={full:F3} ms/frame, incremental={incremental:F3} ms/frame; small-drag dirty area={dirtyArea}/{width*(long)height} pixels ({dirtyArea*100d/(width*(long)height):F3}%). Not a desktop frame-rate measurement.\n";
    }
    public static async Task<byte[]> RenderAsync(Microsoft.UI.Xaml.UIElement content)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(content);
        var buffer=await bitmap.GetPixelsAsync();byte[] pixels=new byte[buffer.Length];
        using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(pixels);
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
        using var encoded=new DataReader(stream.GetInputStreamAt(0));await encoded.LoadAsync((uint)stream.Size);byte[] result=new byte[(int)stream.Size];encoded.ReadBytes(result);return result;
    }
    public static async Task<byte[]> FixtureAsync()
    {
        const int width=320,height=200;var pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int offset=(y*width+x)*4;pixels[offset]=(byte)(x*255/width);pixels[offset+1]=(byte)(y*255/height);pixels[offset+2]=180;pixels[offset+3]=255;
        }
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,width,height,96,96,pixels);await encoder.FlushAsync();
        using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);byte[] png=new byte[(int)stream.Size];reader.ReadBytes(png);return png;
    }
}
