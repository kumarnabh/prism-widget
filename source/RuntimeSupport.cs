using System.IO;

namespace Prism;

public static class RuntimeSupport
{
    public static string Python(string root)
    {
        string isolated=Path.Combine(root,".venv","Scripts","python.exe");
        return File.Exists(isolated)?isolated:"python";
    }
}
