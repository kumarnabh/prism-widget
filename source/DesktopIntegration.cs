using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Prism;

public static class StartupRegistration
{
    const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run",Name="PrismWidget";
    static string Command=>"\""+Environment.ProcessPath+"\" --tray";
    public static bool Enabled{get{try{using var key=Registry.CurrentUser.OpenSubKey(Key);return string.Equals(key?.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase);}catch{return false;}}}
    public static void Set(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(Key,true);
        string? existing=key.GetValue(Name) as string;
        if(existing is not null&&!string.Equals(existing,Command,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Startup belongs to another Prism installation. Disable it there first.");
        if(enabled)key.SetValue(Name,Command,RegistryValueKind.String);else if(existing is not null)key.DeleteValue(Name,false);
    }
}

public sealed class TrayIcon : IDisposable
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct IconData
    {
        public uint Size;public IntPtr Window;public uint Id,Flags,Callback;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    IconData data;readonly HwndSource source;readonly Action show,menu;readonly uint taskbarCreated;bool active;
    public TrayIcon(Window window,string iconPath,Action show,Action menu)
    {
        this.show=show;this.menu=menu;var handle=new WindowInteropHelper(window).EnsureHandle();source=HwndSource.FromHwnd(handle)!;
        taskbarCreated=RegisterWindowMessage("TaskbarCreated");
        data=new(){Size=(uint)Marshal.SizeOf<IconData>(),Window=handle,Id=1,Flags=7,Callback=0x8001,Icon=LoadImage(IntPtr.Zero,iconPath,1,16,16,0x10),Tip="Prism",Info="",Title=""};
        if(data.Icon==IntPtr.Zero)throw new InvalidOperationException("Tray icon could not be loaded.");
        source.AddHook(Hook);active=Shell_NotifyIcon(0,ref data);
        if(!active){source.RemoveHook(Hook);DestroyIcon(data.Icon);throw new InvalidOperationException("Windows tray is unavailable.");}
    }
    IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if((uint)message==taskbarCreated){active=Shell_NotifyIcon(0,ref data);}
        if(message==0x8001){int action=lParam.ToInt32();if(action==0x202||action==0x203)show();else if(action==0x205)menu();handled=true;}
        return IntPtr.Zero;
    }
    public bool Notify(string title,string message)
    {
        if(!active)return false;data.Flags=7|16;data.Title=title.Length>63?title[..63]:title;data.Info=message.Length>255?message[..255]:message;data.InfoFlags=1;data.Timeout=10000;
        bool sent=Shell_NotifyIcon(1,ref data);data.Flags=7;data.Title=data.Info="";return sent;
    }
    public void Dispose(){if(active)Shell_NotifyIcon(2,ref data);active=false;source.RemoveHook(Hook);if(data.Icon!=IntPtr.Zero){DestroyIcon(data.Icon);data.Icon=IntPtr.Zero;}}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern bool Shell_NotifyIcon(uint message,ref IconData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
}

public sealed record DisplayArea(string Id,Rect Work);
public static class DesktopLayout
{
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    delegate bool MonitorCallback(IntPtr monitor,IntPtr hdc,IntPtr rect,IntPtr data);
    public static List<DisplayArea> Displays(Window window)
    {
        var matrix=PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice??System.Windows.Media.Matrix.Identity;var items=new List<DisplayArea>();
        MonitorCallback callback=(monitor,_,_,_)=>{var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=""};if(GetMonitorInfo(monitor,ref info)){var r=info.Work;items.Add(new(info.Device,Rect.Transform(new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top),matrix)));}return true;};
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero);GC.KeepAlive(callback);return items;
    }
    public static DisplayArea Current(Window window)
    {
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=""};var handle=MonitorFromWindow(new WindowInteropHelper(window).Handle,2);
        if(GetMonitorInfo(handle,ref info)){var found=Displays(window).FirstOrDefault(d=>d.Id==info.Device);if(found is not null)return found;}
        return new("",SystemParameters.WorkArea);
    }
    public static Rect Constrain(Rect window,Rect work,bool snap)
    {
        double width=Math.Min(window.Width,work.Width),height=Math.Min(window.Height,work.Height);
        double x=Math.Clamp(window.Left,work.Left,work.Right-width),y=Math.Clamp(window.Top,work.Top,work.Bottom-height);
        if(snap){if(Math.Abs(x-work.Left)<=16)x=work.Left;if(Math.Abs(work.Right-(x+width))<=16)x=work.Right-width;if(Math.Abs(y-work.Top)<=16)y=work.Top;if(Math.Abs(work.Bottom-(y+height))<=16)y=work.Bottom-height;}
        return new(x,y,width,height);
    }
    [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr hdc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
