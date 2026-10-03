using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prism;

// The thumbnail is rendered entirely from cached metrics; DWM requests never collect data.
public sealed class LiveTaskbarPreview:IDisposable
{
    readonly IntPtr handle;readonly HwndSource source;readonly Func<Widget.BarMetric[]> rows;
    public int LastResult {get;private set;}=int.MinValue;
    int clientWidth=1,clientHeight=1;
    public LiveTaskbarPreview(Window window,Func<Widget.BarMetric[]> rows)
    {
        this.rows=rows;handle=new WindowInteropHelper(window).EnsureHandle();source=HwndSource.FromHwnd(handle)!;
        CaptureClient();int enabled=1;if(DwmSetWindowAttribute(handle,7,ref enabled,4)!=0||DwmSetWindowAttribute(handle,10,ref enabled,4)!=0)throw new InvalidOperationException("Taskbar preview unavailable");source.AddHook(Hook);
    }
    void CaptureClient(){if(!IsIconic(handle)&&GetClientRect(handle,out var rect)&&rect.Right>0&&rect.Bottom>0){clientWidth=rect.Right;clientHeight=rect.Bottom;}}
    IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if(message==5){CaptureClient();return IntPtr.Zero;}
        if(message is not (0x323 or 0x326))return IntPtr.Zero;
        CaptureClient();var (width,height)=BitmapSize(message==0x323,(int)((l.ToInt64()>>16)&0xffff),(int)(l.ToInt64()&0xffff),clientWidth,clientHeight);
        IntPtr bitmap=IntPtr.Zero;
        try{var image=Render(rows(),width,height);var info=new BitmapInfo{Size=40,Width=width,Height=-height,Planes=1,Bits=32,ImageSize=(uint)(width*height*4)};
            bitmap=CreateDIBSection(IntPtr.Zero,ref info,0,out var pixels,IntPtr.Zero,0);if(bitmap!=IntPtr.Zero){var bytes=new byte[width*height*4];image.CopyPixels(bytes,width*4,0);Marshal.Copy(bytes,0,pixels,bytes.Length);LastResult=message==0x323?DwmSetIconicThumbnail(hwnd,bitmap,0):DwmSetIconicLivePreviewBitmap(hwnd,bitmap,IntPtr.Zero,0);}
        }catch{}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);}handled=true;return IntPtr.Zero;
    }
    public void Invalidate()=>DwmInvalidateIconicBitmaps(handle);
    public static (int Width,int Height) BitmapSize(bool thumbnail,int requestedWidth,int requestedHeight,int clientWidth,int clientHeight)=>thumbnail?(Math.Clamp(requestedWidth,1,640),Math.Clamp(requestedHeight,1,480)):(Math.Clamp(clientWidth,1,640),Math.Clamp(clientHeight,1,480));
    public void Dispose(){source.RemoveHook(Hook);int disabled=0;DwmSetWindowAttribute(handle,7,ref disabled,4);DwmSetWindowAttribute(handle,10,ref disabled,4);}
    public static RenderTargetBitmap Render(Widget.BarMetric[] metrics,int width,int height)
    {
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){
            Brush Ink=new SolidColorBrush(Color.FromRgb(244,250,255)),muted=new SolidColorBrush(Color.FromRgb(183,204,226));
            drawing.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(27,48,74),Color.FromRgb(16,25,43),80),new Pen(new SolidColorBrush(Color.FromRgb(100,140,175)),1),new Rect(0,0,width,height),12,12);
            void Text(string value,double x,double y,double size,Brush brush,double max){var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,1){MaxTextWidth=Math.Max(1,max),MaxTextHeight=size*1.5,Trimming=TextTrimming.CharacterEllipsis};drawing.DrawText(text,new Point(x,y));}
            if(width>=80&&height>=60){
            Text("P R I S M",14,10,14,Ink,width-28);
            if(metrics.Length==0)Text(L.T("No metrics selected"),14,45,12,muted,width-28);
            int columns=metrics.Length>6&&width>=240?2:1;int maximum=Math.Max(1,(height-42)/32)*columns;int omitted=Math.Max(0,metrics.Length-maximum);metrics=metrics.Take(maximum).ToArray();
            if(omitted>0)Text("P R I S M  · +"+omitted,14,10,12,Ink,width-28);
            int count=(int)Math.Ceiling(metrics.Length/(double)columns);double cell=(width-28d-12*(columns-1))/columns;double row=count==0?30:Math.Min(44,(height-42d)/count);
            for(int i=0;i<metrics.Length;i++){
                var metric=metrics[i];double x=14+(i%columns)*(cell+12),y=34+(i/columns)*row;bool current=metric.Percent.HasValue&&double.IsFinite(metric.Percent.Value)&&metric.Percent.Value is >=0 and <=100;
                var color=new SolidColorBrush(metric.Capacity?Color.FromRgb(184,172,255):Color.FromRgb(110,239,221));
                Text(metric.Label,x,y,Math.Min(12,row*.35),muted,cell*.6);Text(metric.Value,x+cell*.62,y,Math.Min(12,row*.35),Ink,cell*.38);
                double barY=y+Math.Min(20,row*.5);drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(49,67,88)),null,new Rect(x,barY,cell,3),1.5,1.5);
                if(current)drawing.DrawRoundedRectangle(color,null,new Rect(x,barY,cell*metric.Percent!.Value/100,3),1.5,1.5);
                else Text(L.T(metric.State),x,barY+4,Math.Min(10,row*.25),muted,cell);
            }
            }
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);return bitmap;
    }
    [StructLayout(LayoutKind.Sequential)]struct BitmapInfo{public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int X,Y;public uint Used,Important;}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmInvalidateIconicBitmaps(IntPtr window);
    [DllImport("dwmapi.dll")]static extern int DwmSetIconicThumbnail(IntPtr window,IntPtr bitmap,uint flags);
    [DllImport("dwmapi.dll")]static extern int DwmSetIconicLivePreviewBitmap(IntPtr window,IntPtr bitmap,IntPtr point,uint flags);
    [DllImport("gdi32.dll")]static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr bitmap);
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
}
