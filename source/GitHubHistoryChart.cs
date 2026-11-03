using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Prism;

public sealed class GitHubHistoryChart:FrameworkElement
{
    GitHubSample[] points=Array.Empty<GitHubSample>();string title="",unit="";DateTimeOffset start,end;
    public void Set(GitHubSample[] values,GitHubMetricDefinition definition,DateTimeOffset from,DateTimeOffset to){points=values;title=definition.Name;unit=definition.Unit;start=from;end=to;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(ActualWidth<140||ActualHeight<100)return;
        var ink=Theme.Solid(Color.FromRgb(185,205,225));var accent=Theme.Solid(Color.FromRgb(169,210,255));
        void Label(string text,double x,double y)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
        Label(title+" · "+unit,0,0);Label(start.LocalDateTime.ToString("g"),55,ActualHeight-20);Label("Time (local)",ActualWidth-100,ActualHeight-20);
        if(points.Length==0){Label("No observations for this account and range.",55,50);return;}
        double max=Math.Max(1,points.Max(p=>p.Value)*1.1),left=75,top=25,width=ActualWidth-85,height=ActualHeight-55;
        dc.DrawLine(new Pen(ink,.5),new Point(left,top),new Point(left,top+height));dc.DrawLine(new Pen(ink,.5),new Point(left,top+height),new Point(left+width,top+height));Label(max.ToString("0.##"),0,top);Label("0",40,top+height-12);
        Point? previous=null;GitHubSample? last=null;foreach(var value in points){var point=new Point(left+(value.At-start.ToUnixTimeSeconds())/(end-start).TotalSeconds*width,top+height-value.Value/max*height);if(previous is Point before&&last is not null&&value.Period==last.Period&&value.At-last.At<=7200&&value.Value>=last.Value)dc.DrawLine(new Pen(accent,1.5),before,point);dc.DrawEllipse(accent,null,point,2,2);previous=point;last=value;}
    }
}
