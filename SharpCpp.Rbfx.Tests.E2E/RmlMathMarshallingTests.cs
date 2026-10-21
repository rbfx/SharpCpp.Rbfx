using Urho3D.Math;
using Xunit;
using RmlUiBindings = Urho3D.Bindings;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// RbfxRmlMathMarshallers.hpp maps the Rml math types to the Urho3D math types.
/// The engine helpers in Urho3D/RmlUI/RmlUI.h convert between the two types in native code.
/// Each test sends a value to native code as one type and gets it back as the other type.
/// No bound API uses Rml::Colourb, thus the static_asserts in the header test the byte conversion.
/// Urho3D::ToVector2(Rml::Vector2f) and Urho3D::ToVector4(Rml::Vector4f) collide with the ImGui overloads.
/// The generator excludes them, thus the Vector2f and Vector4f tests use only the return direction.
/// No other bound API takes Rml::Vector2f or Rml::Vector4f by value, thus no test covers that parameter direction.
/// </summary>
public sealed class RmlMathMarshallingTests
{
    public static TheoryData<float, float> Vector2Values => new()
    {
        { 0f, 0f },
        { 1.5f, -2.25f },
        { float.MaxValue, float.MinValue },
    };

    [Theory]
    [MemberData(nameof(Vector2Values))]
    public void Vector2f_returns_from_native_helper(float x, float y)
    {
        var input = new Vector2(x, y);

        Vector2 rml = RmlUiBindings.ToRmlUi(input);

        Assert.Equal(input, rml);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1920, -1080)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void Vector2i_roundtrips_through_native_helpers(int x, int y)
    {
        var input = new IntVector2(x, y);

        IntVector2 rml = RmlUiBindings.ToRmlUi(input);
        IntVector2 output = RmlUiBindings.ToIntVector2(rml);

        Assert.Equal(input, rml);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Vector3f_roundtrips_through_native_helpers()
    {
        var input = new Vector3(1.25f, -3.5f, 1e-7f);

        Vector3 rml = RmlUiBindings.ToRmlUi(input);
        Vector3 output = RmlUiBindings.ToVector3(rml);

        Assert.Equal(input, rml);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Vector4f_returns_from_native_helper()
    {
        var input = new Vector4(1f, -2f, 3.5f, -4.75f);

        Vector4 rml = RmlUiBindings.ToRmlUi(input);

        Assert.Equal(input, rml);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(1f, 1f, 1f, 1f)]
    [InlineData(127f / 255f, 1f / 255f, 254f / 255f, 0.5f)]
    [InlineData(-1f, 2f, 0.25f, 0.75f)]
    public void Colourf_roundtrips_through_native_helpers(float r, float g, float b, float a)
    {
        var input = new Color(r, g, b, a);

        Color rml = RmlUiBindings.ToRmlUi(input);
        Color output = RmlUiBindings.ToColor(rml);

        Assert.Equal(input, rml);
        Assert.Equal(input, output);
    }
}
