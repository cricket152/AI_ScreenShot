namespace ScreenShot.App;

internal enum PanelMovePhase {Begin,Update,End,Cancel}
internal readonly record struct PanelMoveGesture(PanelMovePhase Phase,int DeltaX,int DeltaY,bool Moved,int ScreenX=0,int ScreenY=0);
internal readonly record struct PanelMovePosition(int X,int Y);

internal static class PanelMovePolicy
{
    internal const double Threshold=4;

    internal static bool ReachedThreshold(int deltaX,int deltaY,double scale)
        =>double.IsFinite(scale)&&scale>0&&
          (Math.Abs((long)deltaX)>=Threshold*scale||Math.Abs((long)deltaY)>=Threshold*scale);

    internal static PanelMovePosition Move(int startX,int startY,int width,int height,int deltaX,int deltaY,int workLeft,int workTop,int workRight,int workBottom)
    {
        long maxX=Math.Max((long)workLeft,(long)workRight-Math.Max(1,width));
        long maxY=Math.Max((long)workTop,(long)workBottom-Math.Max(1,height));
        return new((int)Math.Clamp((long)startX+deltaX,workLeft,maxX),(int)Math.Clamp((long)startY+deltaY,workTop,maxY));
    }
}
