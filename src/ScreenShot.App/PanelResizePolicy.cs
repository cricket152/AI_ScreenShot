namespace ScreenShot.App;

[Flags]
internal enum PanelResizeEdges {None=0,Left=1,Top=2,Right=4,Bottom=8}
internal enum PanelResizePane {Toolbar,Message,Window}
internal enum PanelResizePhase {Begin,Update,End,Cancel}
internal readonly record struct PanelResizeGesture(PanelResizePane Pane,PanelResizeEdges Edges,PanelResizePhase Phase,double DeltaX,double DeltaY,double Scale);
internal readonly record struct PanelResizeLimits(double MinWidth,double MaxWidth,double MinHeight,double MaxHeight);
internal readonly record struct PanelResizeChange(double Width,double Height,double LeftDelta,double TopDelta);

internal static class PanelResizePolicy
{
    internal const double GripThickness=6;

    internal static PanelResizePane ResolvePane(PanelResizePane pane,PanelResizeEdges edges,bool messageVisible)
        =>pane==PanelResizePane.Window||(pane==PanelResizePane.Toolbar&&edges.HasFlag(PanelResizeEdges.Top)&&messageVisible)
            ?messageVisible?PanelResizePane.Message:PanelResizePane.Toolbar:pane;

    internal static PanelResizeEdges HitTest(double x,double y,double width,double height,double thickness=GripThickness)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(width)||!double.IsFinite(height)||!double.IsFinite(thickness)||width<=0||height<=0||thickness<=0||x<0||y<0||x>width||y>height)return PanelResizeEdges.None;
        var edges=PanelResizeEdges.None;
        if(x<=thickness||x>=width-thickness)edges|=x<=width/2?PanelResizeEdges.Left:PanelResizeEdges.Right;
        if(y<=thickness||y>=height-thickness)edges|=y<=height/2?PanelResizeEdges.Top:PanelResizeEdges.Bottom;
        return edges;
    }

    internal static PanelResizeChange Apply(double startWidth,double startHeight,PanelResizeEdges edges,double deltaX,double deltaY,PanelResizeLimits limits)
    {
        if(!double.IsFinite(startWidth)||!double.IsFinite(startHeight)||startWidth<=0||startHeight<=0||!double.IsFinite(deltaX)||!double.IsFinite(deltaY))throw new ArgumentOutOfRangeException(nameof(startWidth));
        if(!double.IsFinite(limits.MinWidth)||!double.IsFinite(limits.MaxWidth)||!double.IsFinite(limits.MinHeight)||!double.IsFinite(limits.MaxHeight)||limits.MinWidth<=0||limits.MinHeight<=0||limits.MaxWidth<limits.MinWidth||limits.MaxHeight<limits.MinHeight)throw new ArgumentOutOfRangeException(nameof(limits));
        if((edges&~(PanelResizeEdges.Left|PanelResizeEdges.Top|PanelResizeEdges.Right|PanelResizeEdges.Bottom))!=0||(edges.HasFlag(PanelResizeEdges.Left)&&edges.HasFlag(PanelResizeEdges.Right))||(edges.HasFlag(PanelResizeEdges.Top)&&edges.HasFlag(PanelResizeEdges.Bottom)))throw new ArgumentOutOfRangeException(nameof(edges));
        double width=startWidth,height=startHeight;
        if(edges.HasFlag(PanelResizeEdges.Left))width=Math.Clamp(startWidth-deltaX,limits.MinWidth,limits.MaxWidth);
        else if(edges.HasFlag(PanelResizeEdges.Right))width=Math.Clamp(startWidth+deltaX,limits.MinWidth,limits.MaxWidth);
        if(edges.HasFlag(PanelResizeEdges.Top))height=Math.Clamp(startHeight-deltaY,limits.MinHeight,limits.MaxHeight);
        else if(edges.HasFlag(PanelResizeEdges.Bottom))height=Math.Clamp(startHeight+deltaY,limits.MinHeight,limits.MaxHeight);
        return new(width,height,edges.HasFlag(PanelResizeEdges.Left)?startWidth-width:0,edges.HasFlag(PanelResizeEdges.Top)?startHeight-height:0);
    }
}
