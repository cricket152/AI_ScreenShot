namespace ScreenShot.Core;

public enum AnimationStyle { Off, Minimal, Smooth, Elastic, Lively }
public enum AnimationArea { Toolbar, Panel, Tabs, Messages, Settings }

public sealed record AnimationSettings
{
    public AnimationStyle Style {get;init;}=AnimationStyle.Smooth;
    public double Speed {get;init;}=1;
    public bool Toolbar {get;init;}=true;
    public bool Panel {get;init;}=true;
    public bool Tabs {get;init;}=true;
    public bool Messages {get;init;}=true;
    public AnimationSettings Normalize()=>this with
    {
        Style=Enum.IsDefined(Style)?Style:AnimationStyle.Smooth,
        Speed=double.IsFinite(Speed)?Math.Clamp(Speed,0.5,2):1
    };
    public bool IsEnabled(AnimationArea area)=>Style!=AnimationStyle.Off&&(area switch
    {AnimationArea.Toolbar=>Toolbar,AnimationArea.Panel=>Panel,AnimationArea.Tabs=>Tabs,AnimationArea.Messages=>Messages,_=>true});
}

// Pure policy shared by Storyboard and Composition; all distances are logical pixels.
public readonly record struct MotionProfile(bool Enabled,double DurationMs,float Distance,float EnterScale,float HoverScale,float PressedScale,bool Spring,float Damping)
{
    // The spring period is one oscillation, not the opacity timeline's duration.
    public double SpringPeriodMs {get;init;}=60;
    public float TranslationDamping=>Math.Min(1,Damping+0.05f);
    public static MotionProfile Resolve(PanelAppearance appearance,AnimationArea area,bool systemAnimations=true,bool feedback=false)
    {
        var settings=appearance.Motion.Normalize();
        bool enabled=appearance.EnableAnimations&&systemAnimations&&settings.IsEnabled(area);
        var style=settings.Style;
        double duration=feedback?100:area is AnimationArea.Tabs or AnimationArea.Messages?160:180;
        float distance=style switch{AnimationStyle.Smooth=>6,AnimationStyle.Elastic=>8,AnimationStyle.Lively=>12,_=>0};
        float scale=style switch{AnimationStyle.Smooth=>0.985f,AnimationStyle.Elastic=>0.97f,AnimationStyle.Lively=>0.95f,_=>1};
        return new(enabled,duration/settings.Speed,distance,scale,distance==0?1:style==AnimationStyle.Lively?1.025f:1.012f,
            distance==0?1:style==AnimationStyle.Lively?0.96f:0.98f,style==AnimationStyle.Elastic,0.62f)
            {SpringPeriodMs=(feedback?50:60)/settings.Speed};
    }
}
