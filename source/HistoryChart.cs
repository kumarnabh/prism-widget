using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Prism;

public sealed class HistoryChart : FrameworkElement
{
    UsagePoint[] points=Array.Empty<UsagePoint>();DateTimeOffset start,end,now;long[] resets=Array.Empty<long>();CapacityEstimate? forecast;
    public void Set(UsagePoint[] values,DateTimeOffset start,DateTimeOffset end,long[]? resets=null,CapacityEstimate? forecast=null,DateTimeOffset? now=null){points=values;this.start=start;this.end=end;this.resets=resets??Array.Empty<long>();this.forecast=forecast;this.now=now??DateTimeOffset.UtcNow;InvalidateVisual();}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);double w=Math.Max(1,ActualWidth-52),h=Math.Max(1,ActualHeight-40),left=38,top=8;
        var muted=new SolidColorBrush(Color.FromRgb(185,205,225));var line=new Pen(new SolidColorBrush(Color.FromRgb(68,88,115)),.6);var accent=new SolidColorBrush(Color.FromRgb(106,245,226));
        void Label(string value,Point location)=>drawing.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,muted,VisualTreeHelper.GetDpi(this).PixelsPerDip),location);
        foreach(int percent in new[]{0,25,50,75,100}){double y=top+h*(1-percent/100d);drawing.DrawLine(line,new(left,y),new(left+w,y));Label(percent.ToString(CultureInfo.CurrentCulture),new(4,y-6));}
        if(end<=start)return;
        Label(start.LocalDateTime.ToString("MMM d HH:mm"),new(left,top+h+10));Label(end.LocalDateTime.ToString("MMM d HH:mm"),new(Math.Max(left+w-82,left),top+h+10));
        double duration=(end-start).TotalSeconds;var geometry=new StreamGeometry();using(var path=geometry.Open()){
            long prior=0;double previous=0;foreach(var p in points){double x=left+w*(p.At-start.ToUnixTimeSeconds())/duration,y=top+h*(1-p.Remaining/100);
                if(prior==0||p.At-prior>3600||p.Remaining>previous+.2||resets.Any(r=>r>prior&&r<=p.At))path.BeginFigure(new(x,y),false,false);else path.LineTo(new(x,y),true,false);prior=p.At;previous=p.Remaining;
            }
        }
        drawing.DrawGeometry(null,new Pen(accent,1.8),geometry);
        var dashed=new Pen(muted,1){DashStyle=DashStyles.Dash};
        foreach(long reset in resets.Where(r=>r>=start.ToUnixTimeSeconds()&&r<=end.ToUnixTimeSeconds())){double x=left+w*(reset-start.ToUnixTimeSeconds())/duration;drawing.DrawLine(dashed,new(x,top),new(x,top+h));}
        if(forecast?.Reliable==true&&forecast.ProjectedRemaining is double remaining&&points.Length>0&&end>now){double x=left+w*(points[^1].At-start.ToUnixTimeSeconds())/duration;drawing.DrawLine(dashed,new(x,top+h*(1-points[^1].Remaining/100)),new(left+w,top+h*(1-remaining/100)));Label(L.T("Estimate"),new(Math.Min(x,left+w-90),top));Label(L.T("Reset"),new(left+w-64,top+16));}
        foreach(var p in points){double x=left+w*(p.At-start.ToUnixTimeSeconds())/duration,y=top+h*(1-p.Remaining/100);drawing.DrawEllipse(accent,null,new(x,y),1.6,1.6);}
        ToolTip=L.T("Remaining allowance (%)")+" · "+points.Length.ToString(CultureInfo.CurrentCulture);
        System.Windows.Automation.AutomationProperties.SetName(this,L.T("Remaining allowance (%)")+" · "+points.Length);
    }
}
