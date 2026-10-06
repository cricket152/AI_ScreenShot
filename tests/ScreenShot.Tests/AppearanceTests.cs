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
        var configured=new Settings{PanelAppearance=new(){Base=new(SurfaceMaterial.Transparent,0),Toolbar=new(SurfaceMaterial.AcrylicThin,0.25),Messages=new(SurfaceMaterial.Acrylic,1)}};
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(configured))!;
        Check(restored.PanelAppearance==configured.PanelAppearance,"Three independent materials and opacity endpoints survive JSON persistence");
        Check(legacy.Browser=="Chrome"&&legacy.DefaultPrompt=="保留提示词"&&legacy.HotkeyKey==65,"Material migration preserves existing unrelated settings");
        var malformed=JsonSerializer.Deserialize<Settings>("{\"PanelAppearance\":{\"Base\":null,\"Toolbar\":{\"Material\":999,\"Opacity\":-4},\"Messages\":{\"Opacity\":8}},\"Browser\":\"Chrome\"}")!;
        Check(malformed.PanelAppearance.Base==new PanelAppearance().Base,"Null surfaces use their own default");
        Check(malformed.PanelAppearance.Toolbar.Material==SurfaceMaterial.Acrylic&&malformed.PanelAppearance.Toolbar.Opacity==0,"Unknown material and negative opacity are normalized");
        Check(malformed.PanelAppearance.Messages.Opacity==1&&malformed.Browser=="Chrome","Oversized opacity cannot discard unrelated settings");
        Check(JsonSerializer.Deserialize<Settings>("{\"PanelAppearance\":null}")!.PanelAppearance==new PanelAppearance(),"Null appearance keeps defaults");
        Check(new SurfaceAppearance(SurfaceMaterial.Transparent,double.NaN).Normalize().Opacity==0.65,"Nonfinite opacity cannot reach native controllers");
        Check(!legacy.PanelAppearance.HideWindowBase&&legacy.PanelAppearance.EnableAnimations,"Old settings show the base and enable motion by default");
        var options=configured.PanelAppearance with{HideWindowBase=true,EnableAnimations=false};
        var optionsRestored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{PanelAppearance=options}))!;
        Check(optionsRestored.PanelAppearance==options,"Hide-base and reduced-motion preferences survive normalization and persistence");
        Check(legacy.PanelAppearance.Theme==AppThemeMode.System&&legacy.PanelAppearance.AccentMode==AccentColorMode.System,"Old settings follow Windows theme and accent without changes");
        var themed=options with{Theme=AppThemeMode.Dark,AccentMode=AccentColorMode.Custom,CustomAccentColor=" #aBc123 "};
        var themedRestored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{PanelAppearance=themed}))!;
        Check(themedRestored.PanelAppearance.Theme==AppThemeMode.Dark&&themedRestored.PanelAppearance.AccentMode==AccentColorMode.Custom&&themedRestored.PanelAppearance.CustomAccentColor=="#ABC123"&&themedRestored.PanelAppearance.HideWindowBase&&!themedRestored.PanelAppearance.EnableAnimations,"Theme/accent normalization and persistence preserve other appearance preferences");
        var invalidTheme=new PanelAppearance{Theme=(AppThemeMode)999,AccentMode=(AccentColorMode)999,CustomAccentColor="#GGGGGG"}.Normalize();
        Check(invalidTheme.Theme==AppThemeMode.System&&invalidTheme.AccentMode==AccentColorMode.System&&invalidTheme.CustomAccentColor==PanelAppearance.DefaultCustomAccent,"Invalid theme modes and color use independent defaults");
        Check(new[]{null,"","#FFF","#AAFFFFFF","#12345Z","red"}.All(value=>PanelAppearance.NormalizeAccentColor(value)==PanelAppearance.DefaultCustomAccent),"Malformed and alpha colors cannot reach native accent resources");
        Check(PanelAppearance.NormalizeAccentColor("#000000")=="#000000"&&PanelAppearance.NormalizeAccentColor("#ffffff")=="#FFFFFF","Opaque black and white accents remain valid user choices");
        Check(legacy.PanelAppearance.ShowBorders&&legacy.PanelAppearance.BorderThickness==1,"Old settings retain a thin visible panel outline");
        var bordered=themed with{ShowBorders=false,BorderThickness=2.5};
        Check(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{PanelAppearance=bordered}))!.PanelAppearance==bordered.Normalize(),"Border visibility and width persist independently with theme and materials");
        Check(new PanelAppearance{BorderThickness=-10}.Normalize().BorderThickness==0.5&&new PanelAppearance{BorderThickness=100}.Normalize().BorderThickness==3,"Border width is bounded for native rendering");
        Check(new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}.All(width=>new PanelAppearance{BorderThickness=width}.Normalize().BorderThickness==1),"Nonfinite border widths restore the safe default");
        return checks;
    }
}
