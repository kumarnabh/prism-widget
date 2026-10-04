using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Prism;

// One process and deadline per adapter: slow sources cannot suppress unrelated results.
public static class ProviderRunner
{
    public static async Task<JsonElement> Collect(string root,string id,CancellationToken cancellation)
    {
        _=ProviderCatalog.Get(id);
        var start=new ProcessStartInfo{FileName=RuntimeSupport.Python(root),WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(root,"providers.py"));start.ArgumentList.Add("--providers");start.ArgumentList.Add(id);
        return await Run(start,id,cancellation,TimeSpan.FromSeconds(45));
    }
    public static async Task<JsonElement> Run(ProcessStartInfo start,string id,CancellationToken cancellation,TimeSpan timeout)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(timeout);
        using var process=Process.Start(start)??throw new IOException("Provider worker unavailable");
        void Stop(){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
        using var terminate=deadline.Token.Register(Stop); // Synchronous on window-close cancellation, even if the dispatcher exits.
        try{
            var output=ReadBounded(process.StandardOutput,262144,deadline.Token);
            var errors=ReadBounded(process.StandardError,16384,deadline.Token);
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token),output,errors);
            if(process.ExitCode!=0)throw new IOException("Provider check failed");
            using var document=JsonDocument.Parse(await output);
            if(document.RootElement.ValueKind!=JsonValueKind.Object||!document.RootElement.TryGetProperty(id,out var result)||result.ValueKind!=JsonValueKind.Object||!result.TryGetProperty("provider_id",out var provider)||provider.GetString()!=id||!result.TryGetProperty("schema_version",out var schema)||!schema.TryGetInt32(out var version)||version!=1)throw new IOException("Provider contract unavailable");
            return result.Clone();
        }finally{Stop();}
    }
    static async Task<string> ReadBounded(StreamReader stream,int limit,CancellationToken token)
    {
        var result=new StringBuilder();var buffer=new char[2048];int length;
        while((length=await stream.ReadAsync(buffer.AsMemory(),token))>0){if(result.Length+length>limit)throw new IOException("Provider output exceeded its boundary");result.Append(buffer,0,length);}
        return result.ToString();
    }
}
