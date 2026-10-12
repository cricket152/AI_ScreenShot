using System.Numerics;
using System.Text;
using CSharpMath.Atom;
using CSharpMath.Atom.Atoms;
using CSharpMath.Rendering.FrontEnd;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using DrawingColor=System.Drawing.Color;
using MathPath=CSharpMath.Rendering.FrontEnd.Path;
using XamlPath=Microsoft.UI.Xaml.Shapes.Path;

namespace ScreenShot.App;

internal static class NativeMathRenderer
{
    static NativeMathRenderer()
    {
        AddArrow(@"\xrightarrow","⟶");AddArrow(@"\xleftarrow","⟵");
        // Windows supplies the CJK font; it is not bundled or substituted for the math font.
        foreach(string name in new[]{"Deng.ttf","simhei.ttf"})
        {
            string path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),name);
            if(!File.Exists(path))continue;
            try
            {
                using var stream=File.OpenRead(path);
                var typeface=new Typography.OpenFont.OpenFontReader().Read(stream);
                if(typeface!=null){CSharpMath.Settings.GlobalTypefaces.AddSupplement(typeface);break;}
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException){ }
        }
        Commands=LaTeXSettings.Commands.Select(pair=>pair.Key).ToHashSet(StringComparer.Ordinal);
    }

    private static readonly HashSet<string> Commands;

    private static void AddArrow(string command,string symbol)
    {
        LaTeXSettings.Commands.Add(command,(parser,_,_)=>parser.ReadArgumentOptional().Bind(below=>
            parser.ReadArgument().Bind(above=>
            {
                var arrow=new LargeOperator(symbol,true);arrow.Superscript.Append(above);
                if(below!=null)arrow.Subscript.Append(below);return LaTeXSettings.Ok(arrow);
            })));
    }

    internal static MathSurface? TryRender(string latex,bool display,Brush foreground,double fontSize)
    {
        if(!WithinLimits(latex))return null;
        try
        {
            // Do not replace unsupported professional characters with the library's missing-glyph square.
            if(latex.EnumerateRunes().Any(rune=>rune.Value>127&&!CSharpMath.Settings.GlobalTypefaces.Any(font=>font.GetGlyphIndex(rune.Value)!=0)))return null;
            // Typography uses 72pt/inch, whereas WinUI sizes are 96 logical pixels/inch.
            var painter=new NativePainter{LaTeX=NormalizeCommandBoundaries(latex),FontSize=(float)(fontSize*72/96),LineStyle=display?LineStyle.Display:LineStyle.Text,DisplayErrorInline=false};
            if(painter.ErrorMessage!=null||painter.Content==null)return null;
            var bounds=painter.Measure();
            if(!float.IsFinite(bounds.Width)||!float.IsFinite(bounds.Height)||bounds.Width<=0||bounds.Height<=0||bounds.Width>8192||bounds.Height>4096)return null;
            var canvas=new NativeCanvas(bounds.Width+4,bounds.Height+4,bounds.Height+bounds.Top+2,foreground);
            painter.Draw(canvas,2,2-bounds.Top);return canvas.Surface;
        }
        // Formula parsing is an optional presentation step: malformed external output must retain its source.
        catch(Exception error) when(error is not OutOfMemoryException)
        {return null;}
    }

    private static string NormalizeCommandBoundaries(string latex)
    {
        var normalized=new StringBuilder(latex.Length);
        for(int index=0;index<latex.Length;index++)
        {
            char value=latex[index];normalized.Append(value);
            if(value!='\\'||index+1==latex.Length)continue;
            int start=index;
            while(index+1<latex.Length&&char.IsAsciiLetter(latex[index+1]))normalized.Append(latex[++index]);
            if(index==start){normalized.Append(latex[++index]);continue;}
            // CSharpMath 0.5.1 includes =, * and ' in command names; TeX normally ends at the last letter.
            // Preserve real starred/accent commands and only separate an otherwise invalid suffix.
            if(index+1<latex.Length&&latex[index+1] is '=' or '*' or '\''&&
                Commands.Contains(latex[start..(index+1)])&&!Commands.Contains(latex[start..(index+2)]))normalized.Append(' ');
        }
        return normalized.ToString();
    }

    private static bool WithinLimits(string latex)
    {
        if(latex.Length>16384)return false;
        int depth=0,operations=0;
        for(int index=0;index<latex.Length;index++)
        {
            char value=latex[index];
            if(value is '\\' or '{' or '^' or '_'&&++operations>256)return false;
            if(value=='\\'){index++;continue;}
            if(value=='{'&&++depth>48)return false;
            if(value=='}'&&--depth<0)return false;
        }
        return depth==0;
    }

    private sealed class NativePainter:MathPainter<NativeCanvas,DrawingColor>
    {
        public override DrawingColor WrapColor(DrawingColor color)=>color;
        public override DrawingColor UnwrapColor(DrawingColor color)=>color;
        public override ICanvas WrapCanvas(NativeCanvas canvas)=>canvas;
    }

    internal sealed class MathSurface:Canvas
    {
        internal double BaselineDescent {get;init;}
    }

    private sealed class NativeCanvas(float width,float height,double baselineDescent,Brush foreground):ICanvas
    {
        internal MathSurface Surface {get;}=new(){Width=width,Height=height,BaselineDescent=baselineDescent,IsHitTestVisible=false};
        private Matrix3x2 transform=Matrix3x2.Identity;
        private readonly Stack<Matrix3x2> saved=[];
        public float Width=>width;
        public float Height=>height;
        public DrawingColor DefaultColor {get;set;}
        public DrawingColor? CurrentColor {get;set;}
        public PaintStyle CurrentStyle {get;set;}
        public MathPath StartNewPath()=>new NativePath(this,transform);
        public void Save()=>saved.Push(transform);
        public void Restore()=>transform=saved.Pop();
        public void Translate(float dx,float dy)=>transform=Matrix3x2.CreateTranslation(dx,dy)*transform;
        public void Scale(float sx,float sy)=>transform=Matrix3x2.CreateScale(sx,sy)*transform;
        public void DrawLine(float x1,float y1,float x2,float y2,float lineThickness)
        {
            var vector=new Vector2(x2-x1,y2-y1);float length=vector.Length();if(length==0)return;
            var normal=new Vector2(-vector.Y,vector.X)*(lineThickness/(2*length));
            using var path=StartNewPath();path.MoveTo(x1+normal.X,y1+normal.Y);path.LineTo(x2+normal.X,y2+normal.Y);
            path.LineTo(x2-normal.X,y2-normal.Y);path.LineTo(x1-normal.X,y1-normal.Y);path.CloseContour();
        }
        public void StrokeRect(float left,float top,float rectWidth,float rectHeight)
        {DrawLine(left,top,left+rectWidth,top,0.5f);DrawLine(left,top,left,top+rectHeight,0.5f);DrawLine(left+rectWidth,top,left+rectWidth,top+rectHeight,0.5f);DrawLine(left,top+rectHeight,left+rectWidth,top+rectHeight,0.5f);}
        public void FillRect(float left,float top,float rectWidth,float rectHeight)
        {
            if(CurrentColor?.A==0)return;
            using var path=StartNewPath();path.MoveTo(left,top);path.LineTo(left+rectWidth,top);path.LineTo(left+rectWidth,top+rectHeight);path.LineTo(left,top+rectHeight);path.CloseContour();
        }
        internal void Add(PathGeometry geometry,DrawingColor? explicitColor)
        {
            if(Surface.Children.Count>=8192)throw new InvalidOperationException("Formula geometry limit reached");
            DrawingColor color=explicitColor??CurrentColor??DefaultColor;
            Brush brush=color==DefaultColor?foreground:new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(color.A,color.R,color.G,color.B));
            Surface.Children.Add(new XamlPath{Data=geometry,Fill=CurrentStyle==PaintStyle.Fill?brush:null,Stroke=CurrentStyle==PaintStyle.Fill?null:brush,StrokeThickness=0.5,IsHitTestVisible=false});
        }
    }

    private sealed class NativePath(NativeCanvas canvas,Matrix3x2 transform):MathPath
    {
        private readonly PathGeometry geometry=new(){FillRule=FillRule.Nonzero};
        private PathFigure? figure;
        private bool disposed;
        public override DrawingColor? Foreground {get;set;}
        private Point Convert(float x,float y){var point=Vector2.Transform(new(x,y),transform);return new(point.X,point.Y);}
        public override void MoveTo(float x,float y){figure=new PathFigure{StartPoint=Convert(x,y),IsFilled=true};geometry.Figures.Add(figure);}
        public override void LineTo(float x,float y)=>figure!.Segments.Add(new LineSegment{Point=Convert(x,y)});
        public override void Curve3(float x1,float y1,float x2,float y2)=>figure!.Segments.Add(new QuadraticBezierSegment{Point1=Convert(x1,y1),Point2=Convert(x2,y2)});
        public override void Curve4(float x1,float y1,float x2,float y2,float x3,float y3)=>figure!.Segments.Add(new BezierSegment{Point1=Convert(x1,y1),Point2=Convert(x2,y2),Point3=Convert(x3,y3)});
        public override void CloseContour(){if(figure!=null)figure.IsClosed=true;}
        public override void Dispose(){if(disposed)return;disposed=true;if(geometry.Figures.Count>0)canvas.Add(geometry,Foreground);}
    }
}
