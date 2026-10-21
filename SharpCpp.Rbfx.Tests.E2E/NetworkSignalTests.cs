using System;
using System.Text;
using Urho3D.Network;
using Urho3D.Scene;
using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// The NetworkConnection signals carry a ConstByteSpan (eastl::span&lt;const unsigned char&gt;) and an
/// ea::string_view. A handler gets each of them as a ReadOnlySpan&lt;byte&gt; view of the native memory.
/// The view is valid only during the handler. A DataChannelConnection needs no network connection
/// for these tests. The tests invoke the signals through the bound Invoke.
/// </summary>
public sealed class NetworkSignalTests
{
    [Fact]
    public void OnDataReceivedGivesByteView()
    {
        using var context = new global::Urho3D.Core.Context();
        using var connection = new DataChannelConnection(context);
        using var receiver = new Node(context);
        byte[]? received = null;

        connection.OnDataReceived.Subscribe(receiver, (ReadOnlySpan<byte> data, ref bool isHandled) =>
        {
            received = data.ToArray();
            isHandled = true;
        });

        var handled = false;
        connection.OnDataReceived.Invoke(connection, new byte[] { 1, 2, 3 }, ref handled);

        Assert.True(handled);
        Assert.Equal(new byte[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void OnMessageReceivedWithSender()
    {
        using var context = new global::Urho3D.Core.Context();
        using var connection = new DataChannelConnection(context);
        using var receiver = new Node(context);
        var senderHandle = nint.Zero;
        var messageId = (NetworkMessageId)0;
        var length = -1;

        connection.OnMessageReceived.SubscribeWithSender(receiver,
            (NetworkConnection sender, NetworkMessageId id, ReadOnlySpan<byte> payload, ref bool isHandled) =>
            {
                senderHandle = sender.DangerousGetHandle();
                messageId = id;
                length = payload.Length;
            });

        var handled = false;
        connection.OnMessageReceived.Invoke(connection, (NetworkMessageId)7, ReadOnlySpan<byte>.Empty, ref handled);

        Assert.False(handled);
        Assert.Equal(connection.DangerousGetHandle(), senderHandle);
        Assert.Equal((NetworkMessageId)7, messageId);
        Assert.Equal(0, length);
    }

    [Fact]
    public void OnSendDataGivesFlags()
    {
        using var context = new global::Urho3D.Core.Context();
        using var connection = new DataChannelConnection(context);
        using var receiver = new Node(context);
        PacketType? flags = null;
        var sum = 0;

        connection.OnSendData.Subscribe(receiver, (ReadOnlySpan<byte> data, PacketType type, ref bool isHandled) =>
        {
            flags = type;
            foreach (var value in data)
                sum += value;
        });

        var handled = false;
        connection.OnSendData.Invoke(connection, new byte[] { 4, 5 }, PacketType.ReliableOrdered, ref handled);

        Assert.Equal(PacketType.ReliableOrdered, flags);
        Assert.Equal(9, sum);
    }

    [Fact]
    public void OnSendMessageGivesStringView()
    {
        using var context = new global::Urho3D.Core.Context();
        using var connection = new DataChannelConnection(context);
        using var receiver = new Node(context);
        string? debugText = null;
        byte[]? payloadBytes = null;

        connection.OnSendMessage.Subscribe(receiver,
            (NetworkMessageId id, ReadOnlySpan<byte> payload, PacketType type, ReadOnlySpan<byte> debugInfo, ref bool isHandled) =>
            {
                payloadBytes = payload.ToArray();
                debugText = Encoding.UTF8.GetString(debugInfo);
                isHandled = type == PacketType.Reliable;
            });

        var handled = false;
        connection.OnSendMessage.Invoke(connection, (NetworkMessageId)3, new byte[] { 9 }, PacketType.Reliable, "hello"u8, ref handled);

        Assert.True(handled);
        Assert.Equal(new byte[] { 9 }, payloadBytes);
        Assert.Equal("hello", debugText);
    }
}
