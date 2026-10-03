using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Prism;

public sealed record ReleaseUpdate(Version Version,string FileName,Uri Download,string Sha256,long Size,Uri Page);
public sealed class ReleaseUpdates : IDisposable
{
    const string Repository="https://github.com/kumarnabh/prism-widget";
    const long MaxDownload=250_000_000;
    readonly HttpClient client;
    readonly TimeSpan operationTimeout;
    public ReleaseUpdates(HttpMessageHandler? handler=null,TimeSpan? timeout=null)
    {
        operationTimeout=timeout??TimeSpan.FromMinutes(3);
        client=new(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(3)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PrismWidget/2.0.0");
    }
    async Task<HttpResponseMessage> Get(Uri url,CancellationToken token)
    {
        for(int redirect=0;redirect<=4;redirect++){
            if(url.Scheme!="https"||!new[]{"api.github.com","github.com","release-assets.githubusercontent.com","objects.githubusercontent.com"}.Contains(url.Host)||!string.IsNullOrEmpty(url.UserInfo)||!url.IsDefaultPort)throw new HttpRequestException("Untrusted download location.");
            var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308){var location=response.Headers.Location;response.Dispose();if(location is null)throw new HttpRequestException("Missing redirect.");url=location.IsAbsoluteUri?location:new Uri(url,location);continue;}
            try{response.EnsureSuccessStatusCode();return response;}catch{response.Dispose();throw;}
        }
        throw new HttpRequestException("Too many redirects.");
    }
    async Task<byte[]> Small(Uri url,int max,CancellationToken token)
    {
        using var response=await Get(url,token);if(response.Content.Headers.ContentLength>max)throw new IOException("Response too large.");
        await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16_384];
        int count;while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>max)throw new IOException("Response too large.");output.Write(buffer,0,count);}return output.ToArray();
    }
    public async Task<ReleaseUpdate> Check(CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(operationTimeout);token=deadline.Token;
        using var json=JsonDocument.Parse(await Small(new Uri("https://api.github.com/repos/kumarnabh/prism-widget/releases/latest"),2_000_000,token));
        var root=json.RootElement;string tag=root.GetProperty("tag_name").GetString()??"";
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean()||!System.Text.RegularExpressions.Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$")||!Version.TryParse(tag[1..],out var version))throw new IOException("Unsupported release metadata.");
        string file=$"Prism-{version}-win-x64.zip",prefix=$"{Repository}/releases/download/{tag}/";
        var asset=root.GetProperty("assets").EnumerateArray().Single(a=>a.GetProperty("name").GetString()==file);
        string url=asset.GetProperty("browser_download_url").GetString()??"";long size=asset.GetProperty("size").GetInt64();
        if(url!=prefix+file||size<=0||size>MaxDownload)throw new IOException("Unexpected release asset.");
        string digest=asset.TryGetProperty("digest",out var value)?value.GetString()??"":"";
        string hash=digest.StartsWith("sha256:",StringComparison.Ordinal)?digest[7..]:"";
        if(!ValidHash(hash)){
            var checksum=root.GetProperty("assets").EnumerateArray().Single(a=>a.GetProperty("name").GetString()=="SHA256SUMS.txt");
            if(checksum.GetProperty("browser_download_url").GetString()!=prefix+"SHA256SUMS.txt")throw new IOException("Unexpected checksum location.");
            string lines=System.Text.Encoding.UTF8.GetString(await Small(new Uri(prefix+"SHA256SUMS.txt"),8192,token));
            var match=lines.Split('\n').Select(line=>line.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).Single(parts=>parts.Length==2&&parts[1]==file);
            hash=match[0];if(!ValidHash(hash))throw new IOException("Missing SHA-256 checksum.");
        }
        return new(version,file,new Uri(url),hash.ToLowerInvariant(),size,new Uri($"{Repository}/releases/tag/{tag}"));
    }
    static bool ValidHash(string hash)=>hash.Length==64&&hash.All(Uri.IsHexDigit);
    public async Task<string> Download(ReleaseUpdate release,string directory,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(operationTimeout);token=deadline.Token;
        if(release.FileName!=$"Prism-{release.Version}-win-x64.zip"||release.Download.AbsoluteUri!=$"{Repository}/releases/download/v{release.Version}/{release.FileName}"||!ValidHash(release.Sha256)||release.Size<=0||release.Size>MaxDownload)throw new IOException("Invalid release.");
        Directory.CreateDirectory(directory);string target=Path.Combine(directory,release.FileName),temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        if(File.Exists(target)){
            await using var prior=File.OpenRead(target);string previous=Convert.ToHexString(await SHA256.HashDataAsync(prior,token));
            if(prior.Length==release.Size&&string.Equals(previous,release.Sha256,StringComparison.OrdinalIgnoreCase))return target;
            throw new IOException("A different file already exists. Move it before downloading again.");
        }
        try{
            using var response=await Get(release.Download,token);if(response.Content.Headers.ContentLength is long declared&&declared!=release.Size)throw new IOException("Download size mismatch.");
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                await using var input=await response.Content.ReadAsStreamAsync(token);var buffer=new byte[81_920];int count;long total=0;
                while((count=await input.ReadAsync(buffer,token))>0){total+=count;if(total>release.Size||total>MaxDownload)throw new IOException("Download exceeds expected size.");hash.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),token);}
                if(total!=release.Size||!string.Equals(Convert.ToHexString(hash.GetHashAndReset()),release.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Checksum mismatch. Download rejected.");
            }
            File.Move(temp,target);return target;
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Dispose()=>client.Dispose();
}
