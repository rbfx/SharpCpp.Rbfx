using System.Runtime.InteropServices.JavaScript;
using Rbfx.Examples.Common;
using Urho3D.Core;

namespace Rbfx.Examples.Wasm;

internal static class Program
{
    private static void Main()
    {
        using var context = new Context();
        using var sample = new SceneSample(context);
        BrowserConsole.Log(sample.DescribeLastNode());
    }
}

internal static partial class BrowserConsole
{
    [JSImport("console.log", "main.js")]
    internal static partial void Log(string message);
}
