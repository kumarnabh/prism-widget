using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Prism;

public sealed class HistoryChart : FrameworkElement
{
    UsagePoint[] points=Array.Empty<UsagePoint>();DateTimeOffset start,end;
    public void Set(UsagePoint[] values,DateTimeOffset start,DateTimeOffset end){points=values;this.start=start;this.end=end;InvalidateVisual();}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);double w=Math.Max(1,ActualWidth-52),h=Math.Max(1,ActualHeight-40),left=38,top=8;
        var muted=new SolidColorBrush(Color.FromRgb(185,205,225));var line=new Pen(new SolidColorBrush(Color.FromRgb(68,88,115)),.6);var accent=new SolidColorBrush(Color.FromRgb(106,245,226));
        void Label(string value,Point location)=>drawing.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,muted,VisualTreeHelper.GetDpi(this).PixelsPerDip),location);
        foreach(int percent in new[]{0,25,50,75,100}){double y=top+h*(1-percent/100d);drawing.DrawLine(line,new(left,y),new(left+w,y));Label(percent.ToString(CultureInfo.CurrentCulture),new(4,y-6));}
        if(end<=start)return;
        Label(start.LocalDateTime.ToString("MMM d HH:mm"),new(left,top+h+10));Label(end.LocalDateTime.ToString("MMM d HH:mm"),new(Math.Max(left+w-82,left),top+h+10));
        double duration=(end-start).TotalSeconds;var geometry=new StreamGeometry();using(var path=geometry.Open()){
            long prior=0;foreach(var p in points){double x=left+w*(p.At-start.ToUnixTimeSeconds())/duration,y=top+h*(1-p.Remaining/100);
                if(prior==0||p.At-prior>3600)path.BeginFigure(new(x,y),false,false);else path.LineTo(new(x,y),true,false);prior=p.At;
            }
        }
        drawing.DrawGeometry(null,new Pen(accent,1.8),geometry);
        foreach(var p in points){double x=left+w*(p.At-start.ToUnixTimeSeconds())/duration,y=top+h*(1-p.Remaining/100);drawing.DrawEllipse(accent,null,new(x,y),1.6,1.6);}
        ToolTip=L.T("Remaining allowance (%)")+" · "+points.Length.ToString(CultureInfo.CurrentCulture);
        System.Windows.Automation.AutomationProperties.SetName(this,L.T("Remaining allowance (%)")+" · "+points.Length);
    }
}
