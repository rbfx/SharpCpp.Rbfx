using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class WeakPtrReturnTests
{
    [Fact]
    public void Weak_ptr_field_getter_returns_a_correctly_counted_reference()
    {
        using var context = new Context();
        using var app = new ContextReadingApplication(context);

        for (var index = 0; index < 8; ++index)
        {
            var first = app.ReadContext();
            var second = app.ReadContext();
            Assert.True(first is not null);
            Assert.True(ReferenceEquals(first, second));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var node = new Node(context);
        Assert.True(node is not null);
        Assert.True(ReferenceEquals(context, app.ReadContext()));

        for (var index = 0; index < 8; ++index)
        {
            _ = app.ReadContext();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var otherNode = new Node(context);
        otherNode.Name = "after-gc";
        Assert.Equal("after-gc", otherNode.Name);
    }
}

public partial class ContextReadingApplication : Application
{
    public ContextReadingApplication(Context context)
        : base(context)
    {
    }

    public Context ReadContext() => Context;
}
