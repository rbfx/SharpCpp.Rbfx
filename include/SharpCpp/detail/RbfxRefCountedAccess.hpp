#pragma once

#include <Urho3D/Container/RefCounted.h>

namespace sharpcpp::interop::rbfx
{
#if URHO3D_CSHARP
namespace detail
{
template <class Tag, typename Tag::type Member>
struct PrivateMemberAccess
{
    friend constexpr typename Tag::type GetMember(Tag)
    {
        return Member;
    }
};

struct ScriptObjectMemberTag
{
    using type = void* Urho3D::RefCounted::*;
    friend constexpr type GetMember(ScriptObjectMemberTag);
};

struct ScriptStrongRefMemberTag
{
    using type = bool Urho3D::RefCounted::*;
    friend constexpr type GetMember(ScriptStrongRefMemberTag);
};

template struct PrivateMemberAccess<ScriptObjectMemberTag, &Urho3D::RefCounted::scriptObject_>;
template struct PrivateMemberAccess<ScriptStrongRefMemberTag, &Urho3D::RefCounted::isScriptStrongRef_>;
}

inline void* GetScriptObject(Urho3D::RefCounted* instance) noexcept
{
    const auto member = GetMember(detail::ScriptObjectMemberTag{});
    return instance != nullptr ? instance->*member : nullptr;
}

inline void AssignScriptObject(Urho3D::RefCounted* instance, void* handle, bool isStrong) noexcept
{
    if (instance == nullptr)
    {
        return;
    }

    const auto scriptObjectMember = GetMember(detail::ScriptObjectMemberTag{});
    const auto strongRefMember = GetMember(detail::ScriptStrongRefMemberTag{});
    instance->*scriptObjectMember = handle;
    instance->*strongRefMember = isStrong;
}

inline void ResetScriptObject(Urho3D::RefCounted* instance) noexcept
{
    AssignScriptObject(instance, nullptr, false);
}
inline void* TakeExpiredScriptObject() noexcept { return nullptr; }
#else
// SDKs without rbfx's optional C# runtime have no script fields in RefCounted.
// Store wrapper identity outside the engine, retaining a weak control block so
// native address reuse cannot resurrect a wrapper for an expired object.
void* GetScriptObject(Urho3D::RefCounted* instance) noexcept;
void AssignScriptObject(Urho3D::RefCounted* instance, void* handle, bool isStrong) noexcept;
void ResetScriptObject(Urho3D::RefCounted* instance) noexcept;
void* TakeExpiredScriptObject() noexcept;
#endif
}
