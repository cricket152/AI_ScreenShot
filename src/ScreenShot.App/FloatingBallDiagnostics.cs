using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenShot.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ScreenShot.App;

internal static class FloatingBallDiagnostics
{
    internal static async Task<string> VerifyAsync(FloatingBallWindow ball,string output)
    {
        var app=(App)Application.Current;var original=app.Themes.Appearance;
        var root=(FrameworkElement)ball.Content;var button=ball.EntranceButton;
        bool originalActive=ball.Visual.Active;var originalBounds=ball.GetBounds();nint foreground=Native.GetForegroundWindow();int checks=0;
        void Check(bool condition,string error){if(!condition)throw new InvalidOperationException(error);checks++;}
        try
        {
            button.ApplyTemplate();root.UpdateLayout();
            Check(ball.SystemBackdrop is TransparentPanelBackdrop{Alpha:0},"Ball HWND backdrop is not transparent");
            Check(Native.GetClientRect(ball.Handle,out var client)&&client.Right-client.Left==originalBounds.Right-originalBounds.Left&&client.Bottom-client.Top==originalBounds.Bottom-originalBounds.Top,"Ball retained non-client framing");
            Check(button.BorderThickness==new Thickness(0)&&button.ActualWidth==48&&button.ActualHeight==48,"Ball gained a ring or changed its hit target");
            Check(ball.Visual.Icon.Data.Bounds.Width==32&&ball.Visual.Icon.Data.Bounds.Height==32,"Vector artwork has transparent padding");
            Check(ball.Visual.Rim.StrokeThickness<=0.75&&ball.Visual.Highlight.Fill is RadialGradientBrush,"Glass gloss or hairline rim is missing");
            Check(AutomationProperties.GetName(button)=="AI ScreenShot 悬浮入口"&&button.ContextFlyout is MenuFlyout{Items.Count:2},"Ball lost accessibility or its context menu");
            foreach(var theme in new[]{AppThemeMode.Light,AppThemeMode.Dark})
            foreach(string accent in new[]{"#37B99A","#4F6BED","#EDA8BA","#FF0000","#777777","#000000","#FFFFFF"})
            {
                app.Themes.Apply(original with{Theme=theme,AccentMode=AccentColorMode.Custom,CustomAccentColor=accent,ShowShadow=true,ShadowSpread=12,ShadowOpacity=0.16});
                ball.Visual.Active=false;ball.Visual.Update(app.Themes,root);await Task.Delay(25);
                foreach(string state in new[]{"Normal","PointerOver","Pressed","Disabled"})
                {
                    button.IsEnabled=state!="Disabled";
                    Check(VisualStateManager.GoToState(button,state,false),"Ball lost native state "+state);
                    root.UpdateLayout();
                    var presenter=FindPresenter(button)??throw new InvalidOperationException("Ball lacks a native ContentPresenter");
                    var glyph=(SolidColorBrush)ball.Visual.Icon.Foreground;
                    Check(glyph.Color==Microsoft.UI.Colors.White,"Ball icon is not pure white in "+state);
                    Check(presenter.Background is LinearGradientBrush,"Native state replaced the ball gradient");
                    var gradient=(LinearGradientBrush)presenter.Background;
                    Check(gradient.GradientStops.All(stop=>LitContrast(stop.Color)>=3),"White ball icon contrast failed for "+theme+"/"+accent+"/"+state);
                    if(accent=="#37B99A")await File.WriteAllBytesAsync(Path.Combine(output,$"ball-{theme}-{state}.png"),await RuntimeDiagnostics.RenderAsync(root));
                }
                button.IsEnabled=true;VisualStateManager.GoToState(button,"Normal",false);
                var normal=button.Background;ball.Visual.Active=true;ball.Visual.Update(app.Themes,root);
                Check(!ReferenceEquals(normal,button.Background),"Activation has no themed visual feedback");
                var active=(LinearGradientBrush)button.Background;
                Check(active.GradientStops.All(stop=>LitContrast(stop.Color)>=3),"Active white ball icon is unreadable");
                Check(Native.IsWindowVisible(ball.ShadowWindow.Handle),"Ball external shadow did not follow theme preview");
                Check(ball.ShadowWindow.LastPixels.Where((_,index)=>index%4!=3).Any(channel=>channel>0)||accent=="#000000","Ball shadow lost its theme tint");
                if(accent=="#37B99A")await File.WriteAllBytesAsync(Path.Combine(output,$"ball-{theme}-Active.png"),await RuntimeDiagnostics.RenderAsync(root));
                if(accent=="#37B99A"||accent=="#4F6BED"||accent=="#EDA8BA")
                    await SavePreviewAsync(ball,Path.Combine(output,$"ball-glass-{theme}-{accent[1..]}.png"));
            }
            app.Themes.Apply(original with{AccentMode=AccentColorMode.System});await Task.Delay(40);
            Check(app.Themes.AccentColor==new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent),"Ball theme detached from Windows accent");
            // Render only this generated control at common DPI scales; no desktop capture.
            foreach(int size in new[]{52,65,78,104})
            {
                var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(root,size,size);
                var buffer=await bitmap.GetPixelsAsync();byte[] pixels=new byte[buffer.Length];using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(pixels);
                int width=bitmap.PixelWidth,height=bitmap.PixelHeight;
                Check(width==size&&height==size,"Ball scale render has the wrong dimensions");
                Check(pixels[3]==0&&pixels[(width-1)*4+3]==0&&pixels[((height-1)*width)*4+3]==0,"Ball retained an opaque window corner");
                Check(pixels[(height/2*width)*4+3]==0&&pixels[(height/2*width+width-1)*4+3]==0,"Ball retained an outer backdrop ring");
                Check(pixels[(height*3/4*width+width/2)*4+3] is >240 and <255,"Ball glass body lost its restrained transparency");
            }
            var shadow=ball.ShadowWindow;var shadowPixels=shadow.LastPixels;
            Check(shadow.FallbackCount==0&&shadowPixels.Length>0,"Ball shadow encountered a native fallback");
            Native.GetWindowRect(shadow.Handle,out var shadowBounds);var ballBounds=ball.GetBounds();
            int shadowWidth=shadowBounds.Right-shadowBounds.Left,shadowHeight=shadowBounds.Bottom-shadowBounds.Top;
            int center=((ballBounds.Top+ballBounds.Bottom)/2-shadowBounds.Top)*shadowWidth+(ballBounds.Left+ballBounds.Right)/2-shadowBounds.Left;
            Check(shadowPixels.AsSpan(center*4,4).SequenceEqual(new byte[4]),"Shadow darkens the translucent ball interior");
            Check(shadowBounds.Left<ballBounds.Left&&shadowBounds.Right>ballBounds.Right&&shadowBounds.Top<ballBounds.Top&&shadowBounds.Bottom>ballBounds.Bottom,"External shadow is clipped to the ball HWND");
            Check(shadowPixels.Where((_,index)=>index%4==3).Max()<64&&shadowPixels.Take(shadowWidth*4).All(value=>value==0),"Shadow has a hard dark edge");
            Check(shadowPixels.Where((value,index)=>index%4!=3&&value>shadowPixels[index-index%4+3]).Count()==0,"Tinted shadow is not premultiplied");
            Check((Native.GetWindowLongPtr(shadow.Handle,-20)&0x080800a0)==0x080800a0&&Native.SendMessage(shadow.Handle,0x84,0,0)==-1&&Native.SendMessage(shadow.Handle,0x21,0,0)==3,"Ball shadow intercepts input or activates");
            int maskRenders=shadow.MaskRenderCount;
            ball.MoveForDiagnostic(ballBounds.Left-16,ballBounds.Top+12);
            Check(shadow.MaskRenderCount==maskRenders,"Moving the ball re-created its shadow mask");
            Native.GetWindowRect(shadow.Handle,out var movedShadow);
            var movedBall=ball.GetBounds();
            Check(movedBall.Left!=ballBounds.Left&&movedShadow.Left-shadowBounds.Left==movedBall.Left-ballBounds.Left&&movedShadow.Top-shadowBounds.Top==movedBall.Top-ballBounds.Top,"Shadow failed to follow the ball's actual bounds after toolbar alignment");
            ball.MoveForDiagnostic(ballBounds.Left,ballBounds.Top);
            ball.AppWindow.Hide();
            Check(!Native.IsWindowVisible(shadow.Handle),"Hidden ball left its shadow visible");
            ball.AppWindow.Show(false);
            Check(Native.IsWindowVisible(shadow.Handle),"Showing the ball did not restore its shadow");
            app.Themes.Apply(original with{ShowShadow=false});
            Check(!Native.IsWindowVisible(shadow.Handle),"Ball ignored the existing shadow opt-out");
            app.Themes.Apply(original with{ShowShadow=true,ShadowSpread=12,ShadowOpacity=0.16});
            Check(Native.IsWindowVisible(shadow.Handle)&&Native.GetForegroundWindow()==foreground,"Ball visual updates changed foreground focus");
            var bounds=ball.GetBounds();
            Check(bounds.Left==originalBounds.Left&&bounds.Top==originalBounds.Top&&bounds.Right==originalBounds.Right&&bounds.Bottom==originalBounds.Bottom,"Appearance changed HWND placement or dimensions");
            string result=$"PASS: {checks} floating-ball checks; pure white rounded vector glyph; native default/hover/pressed/active/disabled; Light/Dark and seven dynamic accents; >=3:1 icon contrast including glass alpha/gloss over a white desktop; hairline rim; transparent perimeter; 100/125/150/200% control renders.\nPASS: tinted premultiplied external shadow is outside-only, soft, nonactivating and click-through; cached mask follows ball move/hide/show and existing shadow opt-out; HWND placement and foreground remain unchanged.\n";
            await File.WriteAllTextAsync(Path.Combine(output,"floating-ball-test.txt"),result);return result;
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(output,"floating-ball-test.txt"),"FAIL: "+error.Message+"\n");throw;}
        finally{ball.MoveForDiagnostic(originalBounds.Left,originalBounds.Top);ball.AppWindow.Show(false);button.IsEnabled=true;ball.Visual.Active=originalActive;app.Themes.Apply(original);VisualStateManager.GoToState(button,"Normal",false);}
    }
    private static double LitContrast(Windows.UI.Color color)
    {
        var onWhite=AppThemeController.Mix(color,Microsoft.UI.Colors.White,1-color.A/255d);
        return AppThemeController.Contrast(AppThemeController.Mix(onWhite,Microsoft.UI.Colors.White,26/255d),Microsoft.UI.Colors.White);
    }

    private static async Task SavePreviewAsync(FloatingBallWindow ball,string path)
    {
        // Compose only generated control pixels and the same native shadow mask; never capture the desktop.
        const int scale=5,inset=70,width=400;
        var themes=((App)Application.Current).Themes;
        var previewButton=new Button{Width=48*scale,Height=48*scale,CornerRadius=new CornerRadius(24*scale),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        UiDesign.Apply(previewButton,"SS.BallButtonStyle");
        var previewVisual=new FloatingBallVisual(previewButton,scale);
        var previewRoot=new Grid{Width=52*scale,Height=52*scale,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
        previewRoot.Children.Add(previewButton);
        var previewWindow=new Window{Content=previewRoot};
        themes.Track(previewWindow);previewVisual.Update(themes,previewRoot);previewWindow.AppWindow.Resize(new(320,320));previewWindow.AppWindow.Show(false);
        var bitmap=new RenderTargetBitmap();
        try{await Task.Delay(25);previewRoot.UpdateLayout();await bitmap.RenderAsync(previewRoot);}
        finally{previewWindow.Close();}
        var buffer=await bitmap.GetPixelsAsync();var body=new byte[buffer.Length];using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(body);
        var settings=((App)Application.Current).Themes.Appearance;
        var pixels=PanelShadowMask.Render(width,width,[new(inset+2*scale,inset+2*scale,48*scale,48*scale,24*scale)],Math.Min(6,settings.ShadowSpread/2)*scale,Math.Min(0.22,settings.ShadowOpacity*1.1));
        var tint=ball.Visual.ShadowColor;
        for(int index=0;index<pixels.Length;index+=4)
        {byte alpha=pixels[index+3];pixels[index]=(byte)((tint.B*alpha+127)/255);pixels[index+1]=(byte)((tint.G*alpha+127)/255);pixels[index+2]=(byte)((tint.R*alpha+127)/255);}
        for(int y=0;y<Math.Min(52*scale,bitmap.PixelHeight);y++)for(int x=0;x<Math.Min(52*scale,bitmap.PixelWidth);x++)
        {
            int source=(y*bitmap.PixelWidth+x)*4,destination=((y+inset)*width+x+inset)*4;int inverse=255-body[source+3];
            for(int channel=0;channel<4;channel++)pixels[destination+channel]=(byte)Math.Min(255,body[source+channel]+(pixels[destination+channel]*inverse+127)/255);
        }
        // A theme-matched matte makes the enlarged preview readable; the ball-* renders retain alpha.
        var matte=themes.SystemBrush("SolidBackgroundFillColorBaseBrush",previewRoot.ActualTheme).Color;
        for(int index=0;index<pixels.Length;index+=4)
        {
            int inverse=255-pixels[index+3];
            pixels[index]=(byte)Math.Min(255,pixels[index]+(matte.B*inverse+127)/255);
            pixels[index+1]=(byte)Math.Min(255,pixels[index+1]+(matte.G*inverse+127)/255);
            pixels[index+2]=(byte)Math.Min(255,pixels[index+2]+(matte.R*inverse+127)/255);pixels[index+3]=255;
        }
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,width,width,96,96,pixels);await encoder.FlushAsync();
        using var encoded=new DataReader(stream.GetInputStreamAt(0));await encoded.LoadAsync((uint)stream.Size);var result=new byte[(int)stream.Size];encoded.ReadBytes(result);await File.WriteAllBytesAsync(path,result);
    }
    private static ContentPresenter? FindPresenter(DependencyObject node)
    {
        if(node is ContentPresenter presenter)return presenter;
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)if(FindPresenter(VisualTreeHelper.GetChild(node,index)) is {} child)return child;
        return null;
    }
}
