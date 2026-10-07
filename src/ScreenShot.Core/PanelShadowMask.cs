namespace ScreenShot.Core;

public readonly record struct ShadowShape(double Left,double Top,double Width,double Height,double Radius,double Opacity=1);

public static class PanelShadowMask
{
    // Premultiplied BGRA for a native layered window; never darken translucent pane interiors.
    public static byte[] Render(int width,int height,IReadOnlyList<ShadowShape> shapes,double spread,double opacity)
    {
        if(width<=0||height<=0||width>16384||height>16384)throw new ArgumentOutOfRangeException(nameof(width));
        var pixels=new byte[checked(width*height*4)];
        if(spread<=0||opacity<=0||!double.IsFinite(spread)||!double.IsFinite(opacity))return pixels;
        var falloff=new byte[(int)Math.Ceiling(spread*8)+2];
        for(int index=0;index<falloff.Length;index++)falloff[index]=(byte)Math.Round(Math.Clamp(opacity,0,1)*255*Math.Exp(-2*Math.Pow(index/(4*spread),2)));
        foreach(var shape in shapes)
        {
            if(shape.Width<=0||shape.Height<=0)continue;
            double radius=Math.Clamp(shape.Radius,0,Math.Min(shape.Width,shape.Height)/2);
            double cx=shape.Left+shape.Width/2,cy=shape.Top+shape.Height/2;
            var qx=new double[width];for(int x=0;x<width;x++)qx[x]=Math.Abs(x+0.5-cx)-(shape.Width/2-radius);
            int xStart=Math.Max(0,(int)Math.Floor(shape.Left-spread*2)),xEnd=Math.Min(width-1,(int)Math.Ceiling(shape.Left+shape.Width+spread*2));
            int yStart=Math.Max(0,(int)Math.Floor(shape.Top-spread*2)),yEnd=Math.Min(height-1,(int)Math.Ceiling(shape.Top+shape.Height+spread*2));
            double strength=Math.Clamp(shape.Opacity,0,1);
            for(int y=yStart;y<=yEnd;y++)
            {
                var inside=Interior(shape,radius,y);
                double qy=Math.Abs(y+0.5-cy)-(shape.Height/2-radius),dy=Math.Max(qy,0);
                void Band(int start,int end)
                {
                    for(int x=start;x<=end;x++)
                    {
                        double dx=Math.Max(qx[x],0);
                        double distance=(dx==0?dy:dy==0?dx:Math.Sqrt(dx*dx+dy*dy))+Math.Min(Math.Max(qx[x],qy),0)-radius;
                        int index=(int)Math.Round(Math.Max(0,distance)*4);
                        if(index>=falloff.Length)continue;
                        int offset=(y*width+x)*4+3;
                        pixels[offset]=Math.Max(pixels[offset],(byte)Math.Round(falloff[index]*strength));
                    }
                }
                if(inside.Left>inside.Right)Band(xStart,xEnd);
                else{Band(xStart,Math.Min(xEnd,inside.Left-1));Band(Math.Max(xStart,inside.Right+1),xEnd);}
            }
        }
        // Clear the union of interiors in a separate pass, including overlapping shadows.
        foreach(var shape in shapes)
        {
            if(shape.Width<=0||shape.Height<=0)continue;
            double radius=Math.Clamp(shape.Radius,0,Math.Min(shape.Width,shape.Height)/2);
            for(int y=Math.Max(0,(int)Math.Floor(shape.Top));y<Math.Min(height,(int)Math.Ceiling(shape.Top+shape.Height));y++)
            {
                var inside=Interior(shape,radius,y);int left=Math.Max(0,inside.Left),right=Math.Min(width-1,inside.Right);
                if(right>=left)pixels.AsSpan((y*width+left)*4,(right-left+1)*4).Clear();
            }
        }
        return pixels;
    }
    private static (int Left,int Right) Interior(ShadowShape shape,double radius,int y)
    {
        double dy=Math.Abs(y+0.5-shape.Top-shape.Height/2),halfWidth=shape.Width/2;
        if(dy>shape.Height/2)return (1,0);
        if(dy>shape.Height/2-radius)
        {double cornerY=dy-(shape.Height/2-radius);halfWidth=shape.Width/2-radius+Math.Sqrt(Math.Max(0,radius*radius-cornerY*cornerY));}
        double center=shape.Left+shape.Width/2;
        return ((int)Math.Ceiling(center-halfWidth-0.5),(int)Math.Floor(center+halfWidth-0.5));
    }
}
