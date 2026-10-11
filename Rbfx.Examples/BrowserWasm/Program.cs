using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;
using System.Runtime.InteropServices.JavaScript;

namespace Rbfx.Examples.BrowserWasm;

internal static class Program
{
    private static void Main()
    {
        using var context = new Context();
        using var scene = new Scene(context);
        var node = scene.CreateChild(0u);
        node.Position = new Vector3(3, 4, 0);
        BrowserConsole.Log($"Created an rbfx scene node at {node.Position}; distance from origin: {node.Position.Length:F1}.");
    }
}

internal static partial class BrowserConsole
{
    [JSImport("console.log", "main.js")]
    internal static partial void Log(string message);
}
