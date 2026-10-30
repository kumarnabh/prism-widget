using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Prism;

public static class RuntimeSupport
{
    public static bool Ready(string root)
    {
        try{
            if(!File.Exists(Path.Combine(root,".venv","Scripts","python.exe")))return false;
            string receipt=Path.Combine(root,"data","runtime-ready.json");
            if(!File.Exists(receipt)||new FileInfo(receipt).Length>4096)return false;
            using var document=JsonDocument.Parse(File.ReadAllText(receipt));var value=document.RootElement;
            string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"requirements.txt"))));
            return value.GetProperty("schema").GetInt32()==1&&value.GetProperty("terminalDependencies").GetBoolean()
                &&string.Equals(value.GetProperty("requirementsSha256").GetString(),hash,StringComparison.OrdinalIgnoreCase);
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException){return false;}
    }

    public static Window SetupWindow(string root)
    {
        var ink=new SolidColorBrush(Color.FromRgb(238,245,255));
        var window=new Window{Title="Prism · finish setup",Width=490,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Theme.DialogBackground(),Foreground=ink,FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI")};
        var panel=new StackPanel{Margin=new Thickness(28)};window.Content=panel;
        void Text(string text,double size,Thickness margin){panel.Children.Add(new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=margin});}
        Text("Finish setting up Prism",25,new(0,0,0,16));
        Text("This copy needs its local Python dependencies before account readings can work.",14,new(0,0,0,12));
        Text("Run Setup to prepare this folder. Setup also offers OpenCode Console support, without changing your global CLI. Sign in through your provider's own app; Prism sends no model prompts.",13,new(0,0,0,20));
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=ink,Margin=new(0,0,0,10)};panel.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(actions);
        var close=new Button{Content="Close",Padding=new Thickness(15,7,15,7),Margin=new(0,0,10,0)};close.Click+=(_,_)=>window.Close();actions.Children.Add(close);
        var setup=new Button{Content="Run Setup",Padding=new Thickness(18,7,18,7),FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(14,26,42)),BorderBrush=Brushes.Transparent,
            Background=new LinearGradientBrush(Color.FromRgb(106,245,226),Color.FromRgb(169,155,255),0)};actions.Children.Add(setup);
        setup.Click+=(_,_)=>{
            try{Process.Start(new ProcessStartInfo(Path.Combine(root,"Setup.cmd")){UseShellExecute=true,WorkingDirectory=root});window.Close();}
            catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception){status.Text="Setup could not be opened. Extract the full ZIP into a writable folder and run Setup.cmd there.";}
        };
        return window;
    }

    public static string Python(string root)
    {
        string isolated=Path.Combine(root,".venv","Scripts","python.exe");
        return File.Exists(isolated)?isolated:"python";
    }
}
