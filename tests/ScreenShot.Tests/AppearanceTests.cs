using System.Text.Json;
using ScreenShot.Core;

internal static class AppearanceTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var legacy=JsonSerializer.Deserialize<Settings>("{\"Browser\":\"Chrome\",\"DefaultPrompt\":\"保留提示词\",\"HotkeyKey\":65}")!;
        Check(legacy.PanelAppearance==new PanelAppearance(),"Existing settings acquire readable material defaults");
        var configured=new Settings{PanelAppearance=new(){Toolbar=new(SurfaceMaterial.AcrylicThin,0.25),Messages=new(SurfaceMaterial.Acrylic,1)}};
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(configured))!;
        Check(restored.PanelAppearance==configured.PanelAppearance,"Two independent materials and opacity endpoints survive JSON persistence");
        Check(legacy.DefaultPrompt=="保留提示词"&&legacy.HotkeyKey==65,"Material migration preserves existing unrelated settings");
        var malformed=JsonSerializer.Deserialize<Settings>("{\"PanelAppearance\":{\"Base\":null,\"Toolbar\":{\"Material\":999,\"Opacity\":-4},\"Messages\":{\"Opacity\":8}},\"Browser\":\"Chrome\"}")!;
        Check(!JsonSerializer.Serialize(malformed).Contains("\"Base\""),"Retired base fields are ignored and omitted when saving");
        Check(malformed.PanelAppearance.Toolbar.Material==SurfaceMaterial.Acrylic&&malformed.PanelAppearance.Toolbar.Opacity==0,"Unknown material and negative opacity are normalized");
        Check(malformed.PanelAppearance.Messages.Opacity==1&&!JsonSerializer.Serialize(malformed).Contains("Browser"),"Oversized opacity cannot discard unrelated settings");
        Check(JsonSerializer.Deserialize<Settings>("{\"PanelAppearance\":null}")!.PanelAppearance==new PanelAppearance(),"Null appearance keeps defaults");
        Check(new SurfaceAppearance(SurfaceMaterial.Transparent,double.NaN).Normalize().Opacity==0.65,"Nonfinite opacity cannot reach native controllers");
        var retired=JsonSerializer.Deserialize<Settings>("{\"PanelAppearance\":{\"HideWindowBase\":false,\"Base\":{\"Material\":0,\"Opacity\":1},\"EnableAnimations\":false,\"Toolbar\":{\"Material\":2,\"Opacity\":0.23}},\"DefaultPrompt\":\"保留提示词\"}")!;
        Check(retired.PanelAppearance.Toolbar==new SurfaceAppearance(SurfaceMaterial.Transparent,0.23)&&!retired.PanelAppearance.EnableAnimations&&retired.DefaultPrompt=="保留提示词"&&!JsonSerializer.Serialize(retired).Contains("HideWindowBase"),"Removing the base migrates legacy JSON without changing pane or business preferences");
        var options=configured.PanelAppearance with{EnableAnimations=false};
        var optionsRestored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{PanelAppearance=options}))!;
        Check(optionsRestored.PanelAppearance==options,"Pane and reduced-motion preferences survive normalization and persistence");
        Check(legacy.PanelAppearance.Theme==AppThemeMode.System&&legacy.PanelAppearance.AccentMode==AccentColorMode.System,"Old settings follow Windows theme and accent without changes");
        var themed=options with{Theme=AppThemeMode.Dark,AccentMode=AccentColorMode.Custom,CustomAccentColor=" #aBc123 "};
        var themedRestored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{PanelAppearance=themed}))!;
        Check(themedRestored.PanelAppearance.Theme==AppThemeMode.Dark&&themedRestored.PanelAppearance.AccentMode==AccentColorMode.Custom&&themedRestored.PanelAppearance.CustomAccentColor=="#ABC123"&&!themedRestored.PanelAppearance.EnableAnimations,"Theme/accent normalization and persistence preserve other appearance preferences");
        var invalidTheme=new PanelAppearance{Theme=(AppThemeMode)999,AccentMode=(AccentColorMode)999,CustomAccentColor="#GGGGGG"}.Normalize();
        Check(invalidTheme.Theme==AppThemeMode.System&&invalidTheme.AccentMode==AccentColorMode.System&&invalidTheme.CustomAccentColor==PanelAppearance.DefaultCustomAccent,"Invalid theme modes and color use independent defaults");
        Check(new[]{null,"","#FFF","#AAFFFFFF","#12345Z","red"}.All(value=>PanelAppearance.NormalizeAccentColor(value)==PanelAppearance.DefaultCustomAccent),"Malformed and alpha colors cannot reach native accent resources");
        Check(PanelAppearance.NormalizeAccentColor("#000000")=="#000000"&&PanelAppearance.NormalizeAccentColor("#ffffff")=="#FFFFFF","Opaque black and white accents remain valid user choices");
        Check(legacy.PanelAppearance.ShowBorders&&legacy.PanelAppearance.BorderThickness==1,"Old settings retain a thin visible panel outline");
        var bordered=themed with{ShowBorders=false,BorderThickness=2.5};
        Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{PanelAppearance=bordered}))!.PanelAppearance==bordered.Normalize(),"Border visibility and width persist independently with theme and materials");
        Check(new PanelAppearance{BorderThickness=-10}.Normalize().BorderThickness==0.5&&new PanelAppearance{BorderThickness=100}.Normalize().BorderThickness==3,"Border width is bounded for native rendering");
        Check(new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}.All(width=>new PanelAppearance{BorderThickness=width}.Normalize().BorderThickness==1),"Nonfinite border widths restore the safe default");
        var effects=bordered with{ShowShadow=false,ShadowSpread=24,ShadowOpacity=0.32,AccentMix=0.4};
        Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{PanelAppearance=effects}))!.PanelAppearance==effects.Normalize(),"Shadow and material mixing survive persistence without losing existing preferences");
        var invalidEffects=new PanelAppearance{ShadowSpread=999,ShadowOpacity=-1,AccentMix=double.NaN}.Normalize();
        Check(invalidEffects.ShadowSpread==32&&invalidEffects.ShadowOpacity==0&&invalidEffects.AccentMix==0.18,"Effect limits and nonfinite mixing use safe normalized values");
        var shape=new ShadowShape(20,20,40,40,8);
        byte[] soft=PanelShadowMask.Render(80,80,[shape],8,0.3),wide=PanelShadowMask.Render(80,80,[shape],16,0.3),faint=PanelShadowMask.Render(80,80,[shape],8,0.1);
        byte Alpha(byte[] pixels,int x,int y)=>pixels[(y*80+x)*4+3];
        Check(Alpha(soft,40,40)==0&&Alpha(soft,40,20)==0,"Native shadow leaves translucent pane interiors clear");
        Check(Alpha(soft,40,18)>Alpha(soft,40,10)&&Alpha(soft,40,10)>Alpha(soft,40,0),"Shadow smoothly decays beyond the boundary");
        Check(Alpha(wide,40,10)>Alpha(soft,40,10)&&Alpha(faint,40,18)<Alpha(soft,40,18),"Spread and opacity affect independent aspects of the shadow");
        Check(PanelShadowMask.Render(80,80,[shape],8,0).All(value=>value==0),"Zero opacity produces no shadow pixels");
        var overlap=PanelShadowMask.Render(80,80,[shape,new(0,5,30,50,5)],8,0.3);
        Check(Alpha(overlap,10,30)==0,"One pane's shadow never fills another pane's translucent interior");
        return checks;
    }
}
