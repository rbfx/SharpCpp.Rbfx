using Urho3D.Core;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// A by-value FlagSet<E> crosses the boundary with the integer ABI of the managed enum E.
public sealed class FlagSetMarshallingTests
{
    [Fact]
    public void Generated_attribute_info_constructor_takes_a_flags_value()
    {
        const AttributeMode mode = AttributeMode.AmFile | AttributeMode.AmNoedit | AttributeMode.AmTemporary;
        using var enumNames = new Ea.Vector<Ea.String>();
        using var defaultValue = new Variant(7);

        using var info = new AttributeInfo(VariantType.VarInt, "Flags"u8, null!, enumNames, defaultValue, mode);

        Assert.Equal("Flags", info.Name.ToString());
        Assert.Equal(VariantType.VarInt, info.Type);
        Assert.Equal(mode, info.Mode);

        info.Mode = AttributeMode.AmDefault;
        Assert.Equal(AttributeMode.AmDefault, info.Mode);
    }
}
