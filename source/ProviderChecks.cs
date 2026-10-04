using System.Diagnostics;
using System.Text.Json;
using System.IO;

namespace Prism;

public static class ProviderChecks
{
    static ProcessStartInfo Fixture(string command)
    {
        var start=new ProcessStartInfo{FileName=System.IO.Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-Command");start.ArgumentList.Add(command);return start;
    }
    public static async Task<bool> Verify()
    {
        // Fixed synthetic commands. No provider login or environment credential is inspected.
        string folder=Path.Combine(AppContext.BaseDirectory,"build","test-tmp");Directory.CreateDirectory(folder);
        string ChildCommand(string file)=>"$p=Start-Process -FilePath '"+Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe").Replace("'","''")+"' -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 30' -WindowStyle Hidden -PassThru; [IO.File]::WriteAllText('"+file.Replace("'","''")+"',[string]$p.Id); Start-Sleep -Seconds 30";
        void RequireChildExited(string file){if(!File.Exists(file))throw new InvalidOperationException("Child fixture did not start");int id=int.Parse(File.ReadAllText(file));try{using var child=Process.GetProcessById(id);if(!child.WaitForExit(3000))throw new InvalidOperationException("Provider child survived cancellation");}catch(ArgumentException){}}
        string timeoutPid=Path.Combine(folder,"timeout-"+Guid.NewGuid().ToString("N")+".txt");
        var slow=ProviderRunner.Run(Fixture(ChildCommand(timeoutPid)),"claude",CancellationToken.None,TimeSpan.FromSeconds(8));
        var fast=ProviderRunner.Run(Fixture("[Console]::WriteLine('{\"codex\":{\"provider_id\":\"codex\",\"schema_version\":1,\"status\":\"Connect\"}}')"),"codex",CancellationToken.None,TimeSpan.FromSeconds(15));
        try{var result=await fast;if(result.GetProperty("provider_id").GetString()!="codex"||slow.IsCompletedSuccessfully)throw new InvalidOperationException("Independent provider result");}
        finally{try{await slow;throw new InvalidOperationException("Hung provider escaped deadline");}catch(OperationCanceledException){}}
        RequireChildExited(timeoutPid);
        string closePid=Path.Combine(folder,"close-"+Guid.NewGuid().ToString("N")+".txt");using var shutdown=new CancellationTokenSource();
        var closing=ProviderRunner.Run(Fixture(ChildCommand(closePid)),"claude",shutdown.Token,TimeSpan.FromSeconds(15));
        var startup=Stopwatch.StartNew();while(!File.Exists(closePid)&&startup.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(20);
        shutdown.Cancel(); // Must terminate synchronously without awaiting a dispatcher continuation.
        RequireChildExited(closePid);
        try{await closing;throw new InvalidOperationException("Provider ignored shutdown");}catch(OperationCanceledException){}
        try{await ProviderRunner.Run(Fixture("[Console]::WriteLine('{\"claude\":{\"provider_id\":\"claude\",\"schema_version\":1}}')"),"codex",CancellationToken.None,TimeSpan.FromSeconds(15));throw new InvalidOperationException("Cross-provider response accepted");}catch(System.IO.IOException){}
        var preferences=new WidgetPreferences();preferences.Normalize();if(!preferences.DisabledProviders.Contains("openrouter")||!preferences.VisibleMetrics.SequenceEqual(WidgetPreferences.BaseMetricIds)||preferences.RefreshSeconds["openrouter"]!=300)throw new InvalidOperationException("Provider migration defaults");
        return true;
    }
}
