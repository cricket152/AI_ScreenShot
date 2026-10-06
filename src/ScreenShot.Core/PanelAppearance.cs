namespace ScreenShot.Core;

public enum SurfaceMaterial { Acrylic, AcrylicThin, Transparent }
public enum AppThemeMode { System, Light, Dark }
public enum AccentColorMode { System, Custom }

public sealed record SurfaceAppearance(SurfaceMaterial Material=SurfaceMaterial.Acrylic,double Opacity=0.65)
{
    public SurfaceAppearance Normalize()=>new(Enum.IsDefined(Material)?Material:SurfaceMaterial.Acrylic,
        double.IsFinite(Opacity)?Math.Clamp(Opacity,0,1):0.65);
}

public sealed record PanelAppearance
{
    public const string DefaultCustomAccent="#4F6BED";
    public AppThemeMode Theme {get;init;}=AppThemeMode.System;
    public AccentColorMode AccentMode {get;init;}=AccentColorMode.System;
    public string CustomAccentColor {get;init;}=DefaultCustomAccent;
    public bool HideWindowBase {get;init;}
    public bool EnableAnimations {get;init;}=true;
    public bool ShowBorders {get;init;}=true;
    public double BorderThickness {get;init;}=1;
    public SurfaceAppearance Base {get;init;}=new(SurfaceMaterial.AcrylicThin,0.32);
    public SurfaceAppearance Toolbar {get;init;}=new(SurfaceMaterial.AcrylicThin,0.42);
    public SurfaceAppearance Messages {get;init;}=new(SurfaceMaterial.Acrylic,0.82);

    public PanelAppearance Normalize()=>new()
    {
        Theme=Enum.IsDefined(Theme)?Theme:AppThemeMode.System,
        AccentMode=Enum.IsDefined(AccentMode)?AccentMode:AccentColorMode.System,
        CustomAccentColor=NormalizeAccentColor(CustomAccentColor),
        HideWindowBase=HideWindowBase,EnableAnimations=EnableAnimations,
        ShowBorders=ShowBorders,BorderThickness=double.IsFinite(BorderThickness)?Math.Clamp(BorderThickness,0.5,3):1,
        Base=(Base??new(SurfaceMaterial.AcrylicThin,0.32)).Normalize(),
        Toolbar=(Toolbar??new(SurfaceMaterial.AcrylicThin,0.42)).Normalize(),
        Messages=(Messages??new(SurfaceMaterial.Acrylic,0.82)).Normalize()
    };

    public static string NormalizeAccentColor(string? value)
    {
        value=value?.Trim();
        return value is {Length:7}&&value[0]=='#'&&value.AsSpan(1).ToArray().All(Uri.IsHexDigit)?value.ToUpperInvariant():DefaultCustomAccent;
    }
}
