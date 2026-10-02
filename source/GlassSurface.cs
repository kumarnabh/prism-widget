using System.Windows;
using System.Windows.Media;

namespace Prism;

/// <summary>Resolution-independent lighting kept behind the text and controls.</summary>
public sealed class GlassSurface : FrameworkElement
{
    public GlassSurface(){IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if(ActualWidth<=0||ActualHeight<=0)return;
        var bounds=new Rect(0,0,ActualWidth,ActualHeight);
        drawing.PushClip(new RectangleGeometry(bounds,25,25));
        drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(18,38,59),Color.FromRgb(30,25,57),65),null,bounds);
        void Light(Color color,Point origin,double radius){
            var glow=new RadialGradientBrush(color,Color.FromArgb(0,color.R,color.G,color.B)){
                Center=origin,GradientOrigin=origin,RadiusX=radius,RadiusY=radius};
            drawing.DrawRectangle(glow,null,bounds);
        }
        Light(Color.FromArgb(90,31,205,204),new Point(0,0),.9);
        Light(Color.FromArgb(70,132,99,249),new Point(1,.5),.8);
        Light(Color.FromArgb(28,135,191,255),new Point(.35,1),.6);
        drawing.DrawRectangle(new LinearGradientBrush(Color.FromArgb(16,255,255,255),Colors.Transparent,90),null,new Rect(0,0,ActualWidth,Math.Min(110,ActualHeight/2)));
        drawing.Pop();
    }
}
