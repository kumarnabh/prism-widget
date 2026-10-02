using System.Windows;
using System.Windows.Media;

namespace Prism;

/// <summary>A quiet, rounded meter shared by system and quota cards.</summary>
public sealed class Meter : FrameworkElement
{
    double value;
    public double Value { get=>value; set {this.value=double.IsFinite(value)?Math.Clamp(value,0,100):0;InvalidateVisual();} }
    public Brush Foreground {get;set;}=Brushes.White;
    public Brush Background {get;set;}=Brushes.Transparent;
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        double width=ActualWidth,height=ActualHeight;
        if(width<=0||height<=0)return;
        drawing.DrawRoundedRectangle(Background,null,new Rect(0,0,width,height),height/2,height/2);
        if(value>0)drawing.DrawRoundedRectangle(Foreground,null,new Rect(0,0,width*value/100,height),height/2,height/2);
    }
}

public sealed class UsageGauge : FrameworkElement
{
    readonly Brush accent;
    double? value;
    public UsageGauge(string color){accent=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));Width=Height=44;IsHitTestVisible=false;}
    public void Set(double? value){this.value=value is double n&&double.IsFinite(n)?Math.Clamp(n,0,100):null;InvalidateVisual();}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var center=new Point(ActualWidth/2,ActualHeight/2);double radius=Math.Max(0,Math.Min(ActualWidth,ActualHeight)/2-4);
        var track=new Pen(new SolidColorBrush(Color.FromArgb(38,166,190,219)),3);
        drawing.DrawEllipse(null,track,center,radius,radius);
        if(value is not double n||n<=0)return;
        var pen=new Pen(accent,3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        if(n>=100){drawing.DrawEllipse(null,pen,center,radius,radius);return;}
        double angle=n/100*Math.PI*2;
        var arc=new StreamGeometry();using(var path=arc.Open()){
            path.BeginFigure(new Point(center.X,center.Y-radius),false,false);
            path.ArcTo(new Point(center.X+Math.Sin(angle)*radius,center.Y-Math.Cos(angle)*radius),new Size(radius,radius),0,n>50,SweepDirection.Clockwise,true,false);
        }
        drawing.DrawGeometry(null,pen,arc);
    }
}
