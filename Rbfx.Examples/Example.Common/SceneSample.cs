using System.Globalization;
using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;

namespace Rbfx.Examples.Common;

public sealed class SceneSample : IDisposable
{
    private uint _nextNodeId;

    public Scene Scene { get; }
    public Node PrimaryNode { get; }
    public Node LastNode { get; private set; } = null!;
    public uint NodeCount => _nextNodeId;

    /// <summary>Description of the user code component on the primary node, or null if there is none.</summary>
    public string? UserCodeDescription { get; }

    public SceneSample(Context context)
    {
        Scene = new Scene(context);
        PrimaryNode = AddNode();
        UserCodeDescription = UserCodeSupport.AddRotatingComponent(PrimaryNode);
    }

    public Node AddNode()
    {
        var id = _nextNodeId++;
        LastNode = Scene.CreateChild(id);
        LastNode.Position = new Vector3(3 + id, 4 + 2 * id, 0);
        return LastNode;
    }

    public string DescribeLastNode()
    {
        var position = LastNode.Position;
        return string.Format(CultureInfo.InvariantCulture,
            "Nodes: {0}\nLast node position: ({1:F0}, {2:F0}, {3:F0})\nDistance from origin: {4:F2}",
            NodeCount, position.X, position.Y, position.Z, position.Length);
    }

    public void Dispose() => Scene.Dispose();
}
