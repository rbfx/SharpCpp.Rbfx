using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// Rml::String is eastl::string in rbfx. Rml.Variant.Assign and Get must pass the string handle
/// itself to native code. The lengths cover the empty string and the edges of the inline buffer.
/// </summary>
public sealed class RmlVariantStringTests
{
    public static TheoryData<string> Texts => new()
    {
        string.Empty,
        new string('a', 14),
        new string('b', 15),
        new string('c', 22),
        new string('d', 23),
        new string('e', 100),
        "Grüße, 世界 — ✓",
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void Variant_string_assign_and_get_roundtrip(string text)
    {
        using var variant = new Rml.Variant();
        using var input = new Ea.String(text);
        variant.Assign(input);

        using var value = variant.Get<Ea.String>();
        Assert.Equal(text, value.ToString());

        using var fallback = new Ea.String("fallback");
        using var valueWithDefault = variant.Get(fallback);
        Assert.Equal(text, valueWithDefault.ToString());
    }

    [Fact]
    public void Variant_generic_string_assign_roundtrips_many_times()
    {
        using var variant = new Rml.Variant();
        for (var index = 0; index < 200; index++)
        {
            var text = new string((char)('a' + index % 26), index % 64) + index;
            using var input = new Ea.String(text);
            variant.Assign<Ea.String>(input);
            using var value = variant.Get<Ea.String>();
            Assert.Equal(text, value.ToString());
        }
    }
}
