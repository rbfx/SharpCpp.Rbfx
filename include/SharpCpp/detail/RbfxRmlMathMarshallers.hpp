#pragma once

#include <SharpCpp/Marshaller.hpp>
#include "RbfxMathMarshallers.hpp"

#include <RmlUi/Core/Types.h>

#include <type_traits>

// The RmlUi math types cross the ABI as the Urho3D math types.
// The managed code uses Urho3D.Math structs. No Rml math wrapper class is emitted.
// The marshallers name the canonical template instantiations, not the Rml aliases.
// Rml::Matrix4f stays outside this mapping.

namespace SharpCpp
{

namespace detail
{
/// Converts one float colour channel to one byte channel.
/// The value is clamped to [0, 1]. Then it is multiplied by 255 and rounded to the nearest byte.
/// NaN gives 0.
constexpr Rml::byte ToRmlColourByte(float value) noexcept
{
    if (!(value > 0.0f))
        return 0;
    if (value >= 1.0f)
        return 255;
    return static_cast<Rml::byte>(value * 255.0f + 0.5f);
}

/// Converts one byte colour channel to one float channel in [0, 1].
constexpr float FromRmlColourByte(Rml::byte value) noexcept
{
    return static_cast<float>(value) / 255.0f;
}

/// True when each byte value gives the same byte after the conversion to float and back.
constexpr bool RmlColourByteRoundTripsAll() noexcept
{
    for (int value = 0; value <= 255; ++value)
    {
        const auto channel = static_cast<Rml::byte>(value);
        if (ToRmlColourByte(FromRmlColourByte(channel)) != channel)
            return false;
    }
    return true;
}

static_assert(RmlColourByteRoundTripsAll(), "Each Rml::Colourb channel must survive the round trip through Urho3D::Color");
static_assert(ToRmlColourByte(0.0f) == 0 && ToRmlColourByte(1.0f / 255.0f) == 1, "Rml::Colourb channel 0 or 1 is wrong");
static_assert(ToRmlColourByte(127.0f / 255.0f) == 127 && ToRmlColourByte(254.0f / 255.0f) == 254, "Rml::Colourb channel 127 or 254 is wrong");
static_assert(ToRmlColourByte(1.0f) == 255, "Rml::Colourb channel 255 is wrong");
static_assert(ToRmlColourByte(-1.0f) == 0 && ToRmlColourByte(2.0f) == 255, "Rml::Colourb channels must clamp");
static_assert(ToRmlColourByte(127.4f / 255.0f) == 127 && ToRmlColourByte(127.6f / 255.0f) == 128, "Rml::Colourb channels must round");
} // namespace detail

inline Vector2Abi ToAbiRmlVector2f(const Rml::Vector2<float>& v) { return {v.x, v.y}; }
inline Rml::Vector2<float> ToCppRmlVector2f(const Vector2Abi& v) { return {v.X, v.Y}; }
inline IntVector2Abi ToAbiRmlVector2i(const Rml::Vector2<int>& v) { return {v.x, v.y}; }
inline Rml::Vector2<int> ToCppRmlVector2i(const IntVector2Abi& v) { return {v.X, v.Y}; }
inline Vector3Abi ToAbiRmlVector3f(const Rml::Vector3<float>& v) { return {v.x, v.y, v.z}; }
inline Rml::Vector3<float> ToCppRmlVector3f(const Vector3Abi& v) { return {v.X, v.Y, v.Z}; }
inline Vector4Abi ToAbiRmlVector4f(const Rml::Vector4<float>& v) { return {v.x, v.y, v.z, v.w}; }
inline Rml::Vector4<float> ToCppRmlVector4f(const Vector4Abi& v) { return {v.X, v.Y, v.Z, v.W}; }
inline ColorAbi ToAbiRmlColourf(const Rml::Colour<float, 1>& v) { return {v.red, v.green, v.blue, v.alpha}; }
inline Rml::Colour<float, 1> ToCppRmlColourf(const ColorAbi& v) { return {v.R, v.G, v.B, v.A}; }

inline ColorAbi ToAbiRmlColourb(const Rml::Colour<Rml::byte, 255>& v)
{
    return {
        detail::FromRmlColourByte(v.red),
        detail::FromRmlColourByte(v.green),
        detail::FromRmlColourByte(v.blue),
        detail::FromRmlColourByte(v.alpha)};
}

inline Rml::Colour<Rml::byte, 255> ToCppRmlColourb(const ColorAbi& v)
{
    return {
        detail::ToRmlColourByte(v.R),
        detail::ToRmlColourByte(v.G),
        detail::ToRmlColourByte(v.B),
        detail::ToRmlColourByte(v.A)};
}

// ═════════════════════════════════════════════════════════════════════
// Same layout as the Urho3D type.
// The by-value ABI is the trivial ABI struct, as for ImVec2. Rml::Vector2 and the other Rml math types
// have user-provided constructors. Some compilers (MSVC) pass such a type in another way than a
// trivial struct of the same size. A managed caller passes the plain struct.
// The reference ABI is a pointer to the Rml value.
// ═════════════════════════════════════════════════════════════════════

static_assert(sizeof(Rml::Vector2<float>) == sizeof(Vector2Abi), "Rml::Vector2f layout differs from Vector2Abi");
static_assert(sizeof(Rml::Vector2<int>) == sizeof(IntVector2Abi), "Rml::Vector2i layout differs from IntVector2Abi");
static_assert(sizeof(Rml::Vector3<float>) == sizeof(Vector3Abi), "Rml::Vector3f layout differs from Vector3Abi");
static_assert(sizeof(Rml::Vector4<float>) == sizeof(Vector4Abi), "Rml::Vector4f layout differs from Vector4Abi");
static_assert(sizeof(Rml::Colour<float, 1>) == sizeof(ColorAbi), "Rml::Colourf layout differs from ColorAbi");

static_assert(std::is_trivially_copyable_v<Vector2Abi>, "Vector2Abi must be trivially copyable");
static_assert(std::is_trivially_copyable_v<IntVector2Abi>, "IntVector2Abi must be trivially copyable");
static_assert(std::is_trivially_copyable_v<Vector3Abi>, "Vector3Abi must be trivially copyable");
static_assert(std::is_trivially_copyable_v<Vector4Abi>, "Vector4Abi must be trivially copyable");
static_assert(std::is_trivially_copyable_v<ColorAbi>, "ColorAbi must be trivially copyable");

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector2",
    marshaller_type = "global::Urho3D.Math.Vector2",
    suppress_emission = true
)]]
Marshaller<Rml::Vector2<float>>
{
    using cpp_type = Rml::Vector2<float>;
    using abi_type = Vector2Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector2f(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCppRmlVector2f(v); }
};

template <>
struct Marshaller<const Rml::Vector2<float>&>
{
    using cpp_type = const Rml::Vector2<float>&;
    using abi_type = const Rml::Vector2<float>*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Rml::Vector2<float>>
{
    using cpp_type = Rml::Vector2<float>;
    using abi_type = Vector2Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector2f(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCppRmlVector2f(v); }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.IntVector2",
    marshaller_type = "global::Urho3D.Math.IntVector2",
    suppress_emission = true
)]]
Marshaller<Rml::Vector2<int>>
{
    using cpp_type = Rml::Vector2<int>;
    using abi_type = IntVector2Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector2i(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCppRmlVector2i(v); }
};

template <>
struct Marshaller<const Rml::Vector2<int>&>
{
    using cpp_type = const Rml::Vector2<int>&;
    using abi_type = const Rml::Vector2<int>*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Rml::Vector2<int>>
{
    using cpp_type = Rml::Vector2<int>;
    using abi_type = IntVector2Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector2i(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCppRmlVector2i(v); }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector3",
    marshaller_type = "global::Urho3D.Math.Vector3",
    suppress_emission = true
)]]
Marshaller<Rml::Vector3<float>>
{
    using cpp_type = Rml::Vector3<float>;
    using abi_type = Vector3Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector3f(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCppRmlVector3f(v); }
};

template <>
struct Marshaller<const Rml::Vector3<float>&>
{
    using cpp_type = const Rml::Vector3<float>&;
    using abi_type = const Rml::Vector3<float>*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Rml::Vector3<float>>
{
    using cpp_type = Rml::Vector3<float>;
    using abi_type = Vector3Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector3f(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCppRmlVector3f(v); }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector4",
    marshaller_type = "global::Urho3D.Math.Vector4",
    suppress_emission = true
)]]
Marshaller<Rml::Vector4<float>>
{
    using cpp_type = Rml::Vector4<float>;
    using abi_type = Vector4Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector4f(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCppRmlVector4f(v); }
};

template <>
struct Marshaller<const Rml::Vector4<float>&>
{
    using cpp_type = const Rml::Vector4<float>&;
    using abi_type = const Rml::Vector4<float>*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Rml::Vector4<float>>
{
    using cpp_type = Rml::Vector4<float>;
    using abi_type = Vector4Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlVector4f(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCppRmlVector4f(v); }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Color",
    marshaller_type = "global::Urho3D.Math.Color",
    suppress_emission = true
)]]
Marshaller<Rml::Colour<float, 1>>
{
    using cpp_type = Rml::Colour<float, 1>;
    using abi_type = ColorAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlColourf(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCppRmlColourf(v); }
};

template <>
struct Marshaller<const Rml::Colour<float, 1>&>
{
    using cpp_type = const Rml::Colour<float, 1>&;
    using abi_type = const Rml::Colour<float, 1>*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Rml::Colour<float, 1>>
{
    using cpp_type = Rml::Colour<float, 1>;
    using abi_type = ColorAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlColourf(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCppRmlColourf(v); }
};

// ═════════════════════════════════════════════════════════════════════
// Rml::Colourb has 4 bytes. Urho3D::Color has 4 floats. The ABI is ColorAbi.
// References copy through the projected value bases, so the layouts can differ.
// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Color",
    marshaller_type = "global::Urho3D.Math.Color",
    suppress_emission = true
)]]
Marshaller<Rml::Colour<Rml::byte, 255>>
{
    using cpp_type = Rml::Colour<Rml::byte, 255>;
    using abi_type = ColorAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlColourb(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCppRmlColourb(v); }
};

template <>
struct Marshaller<const Rml::Colour<Rml::byte, 255>&>
    : ProjectedValueConstReferenceMarshallerBase<Rml::Colour<Rml::byte, 255>> {};

template <>
struct Marshaller<Rml::Colour<Rml::byte, 255>&>
    : ProjectedValueReferenceMarshallerBase<Rml::Colour<Rml::byte, 255>> {};

template <>
struct ReturnMarshaller<Rml::Colour<Rml::byte, 255>>
{
    using cpp_type = Rml::Colour<Rml::byte, 255>;
    using abi_type = ColorAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbiRmlColourb(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCppRmlColourb(v); }
};

} // namespace SharpCpp
