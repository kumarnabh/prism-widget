using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Prism;

// Windows Credential Manager owns the optional PAT. Never put it in JSON or a process argument.
public static class GitHubCredential
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct Credential {public uint Flags,Type;public string TargetName;public string? Comment;public long LastWritten;public uint CredentialBlobSize;public IntPtr CredentialBlob;public uint Persist,AttributeCount;public IntPtr Attributes;public string? TargetAlias,UserName;}
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Write(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Delete(string target,uint type,uint flags);
    public static string Target(string root)=>"Prism/GitHub/"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.TrimEnd('\\','/').ToUpperInvariant()))).ToLowerInvariant()[..32];
    public static void Save(string root,SecureString secret)
    {
        if(secret.Length is <16 or >4096)throw new InvalidOperationException("Enter a valid fine-grained token");
        IntPtr blob=Marshal.SecureStringToCoTaskMemUnicode(secret);
        try{var value=new Credential{Type=1,TargetName=Target(root),CredentialBlob=blob,CredentialBlobSize=(uint)(secret.Length*2),Persist=2,UserName="Prism"};if(!Write(ref value,0))throw new InvalidOperationException("Windows Credential Manager could not save the credential");}
        finally{Marshal.ZeroFreeCoTaskMemUnicode(blob);}
    }
    public static void Remove(string root){if(!Delete(Target(root),1,0)&&Marshal.GetLastWin32Error()!=1168)throw new InvalidOperationException("Windows Credential Manager could not remove the credential");}
}
