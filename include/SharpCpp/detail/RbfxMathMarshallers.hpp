#pragma once

#include <Urho3D/Math/Vector2.h>
#include <Urho3D/Math/Vector3.h>
#include <Urho3D/Math/Vector4.h>
#include <Urho3D/Math/Quaternion.h>
#include <Urho3D/Math/StringHash.h>
#include <Urho3D/Math/Color.h>
#include <Urho3D/Math/Matrix2.h>
#include <Urho3D/Math/Matrix3.h>
#include <Urho3D/Math/Matrix3x4.h>
#include <Urho3D/Math/Matrix4.h>
#include <Urho3D/Math/BoundingBox.h>
#include <Urho3D/Math/Plane.h>
#include <Urho3D/Math/Ray.h>
#include <Urho3D/Math/Sphere.h>
#include <Urho3D/Math/Rect.h>
#include <Urho3D/Math/Transform.h>

namespace SharpCpp
{

// ─── Vector2 ─────────────────────────────────────────────────────────

struct Vector2Abi { float X; float Y; };
inline Urho3D::Vector2 ToCpp(const Vector2Abi& v) { return {v.X, v.Y}; }
inline Vector2Abi ToAbi(const Urho3D::Vector2& v) { return {v.x_, v.y_}; }

// ─── Vector3 ─────────────────────────────────────────────────────────

struct Vector3Abi { float X; float Y; float Z; };
inline Urho3D::Vector3 ToCpp(const Vector3Abi& v) { return {v.X, v.Y, v.Z}; }
inline Vector3Abi ToAbi(const Urho3D::Vector3& v) { return {v.x_, v.y_, v.z_}; }

// ─── Vector4 ─────────────────────────────────────────────────────────

struct Vector4Abi { float X; float Y; float Z; float W; };
inline Urho3D::Vector4 ToCpp(const Vector4Abi& v) { return {v.X, v.Y, v.Z, v.W}; }
inline Vector4Abi ToAbi(const Urho3D::Vector4& v) { return {v.x_, v.y_, v.z_, v.w_}; }

// ─── IntVector2 ──────────────────────────────────────────────────────

struct IntVector2Abi { int X; int Y; };
inline Urho3D::IntVector2 ToCpp(const IntVector2Abi& v) { return {v.X, v.Y}; }
inline IntVector2Abi ToAbi(const Urho3D::IntVector2& v) { return {v.x_, v.y_}; }

// ─── IntVector3 ──────────────────────────────────────────────────────

struct IntVector3Abi { int X; int Y; int Z; };
inline Urho3D::IntVector3 ToCpp(const IntVector3Abi& v) { return {v.X, v.Y, v.Z}; }
inline IntVector3Abi ToAbi(const Urho3D::IntVector3& v) { return {v.x_, v.y_, v.z_}; }

// ─── DoubleVector2 ───────────────────────────────────────────────────

struct DoubleVector2Abi { double X; double Y; };
inline Urho3D::DoubleVector2 ToCpp(const DoubleVector2Abi& v) { return {v.X, v.Y}; }
inline DoubleVector2Abi ToAbi(const Urho3D::DoubleVector2& v) { return {v.x_, v.y_}; }

// ─── DoubleVector3 ───────────────────────────────────────────────────

struct DoubleVector3Abi { double X; double Y; double Z; };
inline Urho3D::DoubleVector3 ToCpp(const DoubleVector3Abi& v) { return {v.X, v.Y, v.Z}; }
inline DoubleVector3Abi ToAbi(const Urho3D::DoubleVector3& v) { return {v.x_, v.y_, v.z_}; }

// ─── Quaternion ──────────────────────────────────────────────────────

struct QuaternionAbi { float W; float X; float Y; float Z; };
inline Urho3D::Quaternion ToCpp(const QuaternionAbi& v) { return {v.W, v.X, v.Y, v.Z}; }
inline QuaternionAbi ToAbi(const Urho3D::Quaternion& v) { return {v.w_, v.x_, v.y_, v.z_}; }

// ─── BoundingBox ─────────────────────────────────────────────────────

// BoundingBox has a user-provided copy constructor, so it returns through sret. The layout matches
// Urho3D::BoundingBox and the managed struct: Min, one padding float, Max, one padding float.
struct BoundingBoxAbi
{
    float MinX; float MinY; float MinZ; float MinPad;
    float MaxX; float MaxY; float MaxZ; float MaxPad;
};
static_assert(sizeof(BoundingBoxAbi) == 8 * sizeof(float), "BoundingBoxAbi must hold 8 floats");
static_assert(sizeof(Urho3D::BoundingBox) == sizeof(BoundingBoxAbi), "BoundingBox layout differs from BoundingBoxAbi");
inline Urho3D::BoundingBox ToCpp(const BoundingBoxAbi& v)
{
    return Urho3D::BoundingBox(Urho3D::Vector3(v.MinX, v.MinY, v.MinZ), Urho3D::Vector3(v.MaxX, v.MaxY, v.MaxZ));
}
inline BoundingBoxAbi ToAbi(const Urho3D::BoundingBox& v)
{
    return {v.min_.x_, v.min_.y_, v.min_.z_, 0.0f, v.max_.x_, v.max_.y_, v.max_.z_, 0.0f};
}

// ─── Matrix4 ─────────────────────────────────────────────────────────

// Matrix4 has a user-provided copy constructor, so it returns through sret. 16 floats, row-major.
struct Matrix4Abi
{
    float M00; float M01; float M02; float M03;
    float M10; float M11; float M12; float M13;
    float M20; float M21; float M22; float M23;
    float M30; float M31; float M32; float M33;
};
static_assert(sizeof(Matrix4Abi) == 16 * sizeof(float), "Matrix4Abi must hold 16 floats");
static_assert(sizeof(Urho3D::Matrix4) == sizeof(Matrix4Abi), "Matrix4 layout differs from Matrix4Abi");
inline Urho3D::Matrix4 ToCpp(const Matrix4Abi& v)
{
    return Urho3D::Matrix4(
        v.M00, v.M01, v.M02, v.M03,
        v.M10, v.M11, v.M12, v.M13,
        v.M20, v.M21, v.M22, v.M23,
        v.M30, v.M31, v.M32, v.M33);
}
inline Matrix4Abi ToAbi(const Urho3D::Matrix4& v)
{
    return {
        v.m00_, v.m01_, v.m02_, v.m03_,
        v.m10_, v.m11_, v.m12_, v.m13_,
        v.m20_, v.m21_, v.m22_, v.m23_,
        v.m30_, v.m31_, v.m32_, v.m33_};
}

// ─── Color ───────────────────────────────────────────────────────────

struct ColorAbi { float R; float G; float B; float A; };
inline Urho3D::Color ToCpp(const ColorAbi& v) { return {v.R, v.G, v.B, v.A}; }
inline ColorAbi ToAbi(const Urho3D::Color& v) { return {v.r_, v.g_, v.b_, v.a_}; }

// ─── StringHash ──────────────────────────────────────────────────────

// StringHash is a simple unsigned-int wrapper — no ABI struct needed.
using StringHashAbi = unsigned int;
inline Urho3D::StringHash ToCpp(StringHashAbi v) { return Urho3D::StringHash(v); }
inline StringHashAbi ToAbi(const Urho3D::StringHash& v) { return v.Value(); }

} // namespace SharpCpp
