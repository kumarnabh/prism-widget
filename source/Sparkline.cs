using System.Windows;
using System.Windows.Media;

namespace Prism;

/// <summary>A bounded, non-overshooting time series with a soft area and current-value marker.</summary>
public sealed class Sparkline : FrameworkElement
{
    readonly Color accent;
    double[] samples=Array.Empty<double>();
    public Sparkline(string color){accent=(Color)ColorConverter.ConvertFromString(color);ClipToBounds=true;IsHitTestVisible=false;}
    public void SetSamples(IEnumerable<double> values){samples=values.TakeLast(36).Select(v=>Math.Clamp(v,0,100)).ToArray();InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double width=ActualWidth,height=ActualHeight;
        if(width<4||height<4)return;
        var gridPen=new Pen(new SolidColorBrush(Color.FromArgb(22,170,194,215)),.6);
        foreach(double fraction in new[]{.25,.75})dc.DrawLine(gridPen,new Point(0,height*fraction),new Point(width,height*fraction));
        if(samples.Length==0)return;
        double pad=2,usableHeight=height-2*pad;
        var points=samples.Select((v,i)=>new Point(pad+(35-samples.Length+1+i)*(width-2*pad)/35,pad+(1-v/100)*usableHeight)).ToArray();
        var stroke=new StreamGeometry();using(var context=stroke.Open()){
            context.BeginFigure(points[0],false,false);
            for(int i=1;i<points.Length;i++){double middle=(points[i-1].X+points[i].X)/2;context.BezierTo(new Point(middle,points[i-1].Y),new Point(middle,points[i].Y),points[i],true,false);}
        }
        var area=new StreamGeometry();using(var context=area.Open()){
            context.BeginFigure(new Point(points[0].X,height),true,true);context.LineTo(points[0],true,false);
            for(int i=1;i<points.Length;i++){double middle=(points[i-1].X+points[i].X)/2;context.BezierTo(new Point(middle,points[i-1].Y),new Point(middle,points[i].Y),points[i],true,false);}
            context.LineTo(new Point(points[^1].X,height),true,false);
        }
        var fill=new LinearGradientBrush(Color.FromArgb(55,accent.R,accent.G,accent.B),Color.FromArgb(0,accent.R,accent.G,accent.B),90);
        dc.DrawGeometry(fill,null,area);dc.DrawGeometry(null,new Pen(new SolidColorBrush(accent),1.6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},stroke);
        dc.DrawEllipse(new SolidColorBrush(accent),null,points[^1],2,2);
    }
}
