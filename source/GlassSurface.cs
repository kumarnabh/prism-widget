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
        drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(11,29,54),Color.FromRgb(31,20,65),65),null,bounds);
        void Light(Color color,Point origin,double radius){
            var glow=new RadialGradientBrush(color,Color.FromArgb(0,color.R,color.G,color.B)){
                Center=origin,GradientOrigin=origin,RadiusX=radius,RadiusY=radius};
            drawing.DrawRectangle(glow,null,bounds);
        }
        Light(Color.FromArgb(130,18,224,220),new Point(0,0),.85);
        Light(Color.FromArgb(100,110,75,255),new Point(1,.45),.85);
        Light(Color.FromArgb(86,153,69,236),new Point(.9,1),.8);
        Light(Color.FromArgb(42,36,146,255),new Point(0,.65),.7);
        drawing.DrawRectangle(new LinearGradientBrush(Color.FromArgb(24,255,255,255),Colors.Transparent,90),null,new Rect(0,0,ActualWidth,Math.Min(110,ActualHeight/2)));
        drawing.Pop();
    }
}
