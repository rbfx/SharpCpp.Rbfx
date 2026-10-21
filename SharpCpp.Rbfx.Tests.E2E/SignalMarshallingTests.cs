using Urho3D.RenderPipeline;
using Urho3D.Scene;
using Urho3D.Utility;
using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// An Urho3D::Signal field is a borrowed handle to a flattened wrapper class. The wrapper can
/// subscribe managed handlers, unsubscribe a receiver and invoke the signal.
/// The handlers do not capture the receiver wrapper. The signal keeps the receiver only as a weak reference.
///
/// Coverage gap for PrioritySignal: the only engine PrioritySignal is Input::OnRawInput. Its argument type
/// SDL_Event is not bound, so the wrapper has no Subscribe, SubscribeWithSender or Invoke.
/// The E2E project has no native test helper that could add a signal with bound arguments.
/// The test PrioritySignalHasSubscriptionsUnsubscribe covers only HasSubscriptions and Unsubscribe.
/// The core fixture covers the full PrioritySignal wrapper.
/// </summary>
public sealed class SignalMarshallingTests
{
    [Fact]
    public void VoidSignalSubscribeInvoke()
    {
        using var context = new global::Urho3D.Core.Context();
        using var pipeline = new RenderPipeline(context);
        using var receiver = new Node(context);
        var calls = 0;

        var signal = pipeline.OnParametersChanged;
        Assert.False(signal.HasSubscriptions());

        signal.Subscribe(receiver, () => ++calls);
        Assert.True(signal.HasSubscriptions());

        signal.Invoke(null!);
        signal.Invoke(null!);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ConstStructArgument()
    {
        using var context = new global::Urho3D.Core.Context();
        using var pipeline = new RenderPipeline(context);
        using var receiver = new Node(context);
        using var settings = new RenderPipelineSettings { DrawDebugGeometry = false };
        bool? received = null;

        pipeline.OnSettingsChanged.Subscribe(receiver, value => received = value.DrawDebugGeometry);
        pipeline.OnSettingsChanged.Invoke(null!, settings);
        Assert.False(received);

        settings.DrawDebugGeometry = true;
        pipeline.OnSettingsChanged.Invoke(null!, settings);
        Assert.True(received);
    }

    [Fact]
    public void WithSenderPassesSender()
    {
        using var context = new global::Urho3D.Core.Context();
        using var selection = new SceneSelection();
        using var receiver = new Node(context);
        var senderHandle = nint.Zero;
        var calls = 0;

        selection.OnChanged.SubscribeWithSender(receiver, sender =>
        {
            ++calls;
            senderHandle = sender.DangerousGetHandle();
        });
        selection.OnChanged.Invoke(selection);

        Assert.Equal(1, calls);
        Assert.Equal(selection.DangerousGetHandle(), senderHandle);
    }

    [Fact]
    public void UnsubscribeStopsDelivery()
    {
        using var context = new global::Urho3D.Core.Context();
        using var pipeline = new RenderPipeline(context);
        using var receiver = new Node(context);
        using var other = new Node(context);
        var calls = 0;
        var otherCalls = 0;

        var signal = pipeline.OnParametersChanged;
        signal.Subscribe(receiver, () => ++calls);
        signal.Subscribe(other, () => ++otherCalls);
        signal.Invoke(null!);

        signal.Unsubscribe(receiver);
        signal.Invoke(null!);
        Assert.Equal(1, calls);
        Assert.Equal(2, otherCalls);

        signal.Unsubscribe(other);
        Assert.False(signal.HasSubscriptions());
    }

    [Fact]
    public void DisposedReceiverIsNotCalled()
    {
        using var context = new global::Urho3D.Core.Context();
        using var pipeline = new RenderPipeline(context);
        var receiver = new Node(context);
        var calls = 0;

        var signal = pipeline.OnParametersChanged;
        signal.Subscribe(receiver, () => ++calls);
        signal.Invoke(null!);
        Assert.Equal(1, calls);

        // The signal holds the receiver as a weak reference, so the engine destroys the receiver here.
        receiver.Dispose();

        signal.Invoke(null!);
        signal.Invoke(null!);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void PrioritySignalHasSubscriptionsUnsubscribe()
    {
        using var context = new global::Urho3D.Core.Context();
        using var input = new global::Urho3D.Input.Input(context);
        using var receiver = new Node(context);

        // The constructor of Input subscribes the input object to OnRawInput.
        var signal = input.OnRawInput;
        Assert.True(signal.HasSubscriptions());

        // An unknown receiver changes nothing.
        signal.Unsubscribe(receiver);
        Assert.True(signal.HasSubscriptions());

        signal.Unsubscribe(input);
        Assert.False(signal.HasSubscriptions());
    }

    [Fact]
    public void BoolRefWritesBack()
    {
        using var context = new global::Urho3D.Core.Context();
        using var resource = new SceneResource(context);
        using var receiver = new Node(context);

        resource.OnReloadBegin.Subscribe(receiver, (ref bool cancelReload) => cancelReload = true);

        var cancel = false;
        resource.OnReloadBegin.Invoke(null!, ref cancel);
        Assert.True(cancel);
    }

    [Fact]
    public void ReentrantInvokeThrows()
    {
        using var context = new global::Urho3D.Core.Context();
        using var resource = new SceneResource(context);
        using var receiver = new Node(context);
        var signal = resource.OnReloadEnd;
        var calls = 0;
        Exception? reentrant = null;

        signal.Subscribe(receiver, success =>
        {
            ++calls;
            try
            {
                signal.Invoke(null!, success);
            }
            catch (Exception exception)
            {
                reentrant = exception;
            }
        });
        signal.Invoke(null!, true);

        // The nested call does not reach the engine assert and calls no handler.
        Assert.Equal(1, calls);
        Assert.NotNull(reentrant);

        // The outer invocation is complete, so a new invocation succeeds.
        signal.Invoke(null!, false);
        Assert.Equal(2, calls);
        Assert.NotNull(reentrant);
    }
}
