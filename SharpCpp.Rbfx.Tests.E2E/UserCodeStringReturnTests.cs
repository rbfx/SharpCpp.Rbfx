using RbfxExamples;
using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// Phase-2 user code methods that return ea::string by value. TextTools is in a second header of
/// Example.UserCode, so the generator reads the headers through the umbrella header.
/// </summary>
public sealed class UserCodeStringReturnTests
{
    [Fact]
    public void Describe_returns_ea_string_by_value()
    {
        using var context = new global::Urho3D.Core.Context();
        using var component = new RotatingComponent(context);
        component.SetRotationSpeed(12.5f);

        for (var index = 0; index < 500; index++)
        {
            using var text = component.Describe();
            Assert.Equal("RotatingComponent speed=12.5 updates=0", text.ToString());
        }
    }

    [Theory]
    [InlineData("", 10, "")]
    [InlineData("ab", 0, "")]
    [InlineData("ab", 1, "a")]
    [InlineData("xyz", 15, "xyzxyzxyzxyzxyz")]
    [InlineData("0123456789", 23, "01234567890123456789012")]
    [InlineData("é", 6, "ééé")] // length counts UTF-8 bytes
    public void RepeatText_returns_ea_string_by_value(string pattern, int length, string expected)
    {
        using var input = new Ea.String(pattern);

        using var text = TextTools.RepeatText(input, length);
        Assert.Equal(expected, text.ToString());
    }

    [Fact]
    public void RepeatText_returns_long_strings_many_times()
    {
        using var input = new Ea.String("long-text/");

        for (var length = 0; length < 400; length += 7)
        {
            using var text = TextTools.RepeatText(input, length);
            var value = text.ToString();
            Assert.Equal(length, value.Length);
            Assert.All(value.Select((character, index) => (character, index)), item => Assert.Equal("long-text/"[item.index % 10], item.character));
        }
    }
}
