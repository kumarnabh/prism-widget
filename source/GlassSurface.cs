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
        var basis=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
        basis.GradientStops.Add(new GradientStop(Color.FromRgb(9,27,50),0));
        basis.GradientStops.Add(new GradientStop(Color.FromRgb(17,22,56),.55));
        basis.GradientStops.Add(new GradientStop(Color.FromRgb(33,19,68),1));
        drawing.DrawRectangle(basis,null,bounds);
        void Light(Color color,Point origin,double radius){
            var glow=new RadialGradientBrush(color,Color.FromArgb(0,color.R,color.G,color.B)){
                Center=origin,GradientOrigin=origin,RadiusX=radius,RadiusY=radius};
            drawing.DrawRectangle(glow,null,bounds);
        }
        Light(Color.FromArgb(124,18,224,220),new Point(0,0),.85);
        Light(Color.FromArgb(96,110,75,255),new Point(1,.45),.85);
        Light(Color.FromArgb(84,153,69,236),new Point(.9,1),.8);
        Light(Color.FromArgb(40,36,146,255),new Point(0,.65),.7);
        // Top sheen and a soft floor give the pane depth without competing with content.
        drawing.DrawRectangle(new LinearGradientBrush(Color.FromArgb(28,255,255,255),Colors.Transparent,90),null,new Rect(0,0,ActualWidth,Math.Min(120,ActualHeight/2)));
        drawing.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Color.FromArgb(56,4,6,20),90),null,new Rect(0,ActualHeight*.55,ActualWidth,ActualHeight*.45));
        drawing.DrawLine(new Pen(new LinearGradientBrush(Color.FromArgb(0,255,255,255),Color.FromArgb(70,255,255,255),0){SpreadMethod=GradientSpreadMethod.Reflect,EndPoint=new Point(.5,0)},1),new Point(28,1.5),new Point(ActualWidth-28,1.5));
        drawing.Pop();
    }
}
