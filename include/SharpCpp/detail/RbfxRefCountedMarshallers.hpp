#pragma once

#include <SharpCpp/Marshaller.hpp>

#include <Urho3D/Container/Ptr.h>
#include <Urho3D/Core/Object.h>
#include <Urho3D/Container/RefCounted.h>

#include <type_traits>

namespace SharpCpp::rbfx::detail
{
struct SharedPtrAbi
{
    void* pointer{};
    void* sharedPtr{};
};

struct ExternalSharedPtrHolderBase
{
    virtual ~ExternalSharedPtrHolderBase() = default;
};

template <class TCpp>
struct ExternalSharedPtrHolder final : ExternalSharedPtrHolderBase
{
    explicit ExternalSharedPtrHolder(const Urho3D::SharedPtr<TCpp, Urho3D::RefCounted>& value)
        : value_(value)
    {
    }

    Urho3D::SharedPtr<TCpp, Urho3D::RefCounted> value_;
};

template <class TCpp>
inline Urho3D::SharedPtr<TCpp, Urho3D::RefCounted> MakeExternalSharedPtrFromAbi(SharedPtrAbi value)
{
    static_assert(!std::is_base_of_v<Urho3D::RefCounted, TCpp>,
        "The external-owner marshaller is only for interface types that do not inherit RefCounted");

    if (value.sharedPtr == nullptr)
    {
        return {};
    }

    return static_cast<ExternalSharedPtrHolder<TCpp>*>(value.sharedPtr)->value_;
}

template <class TCpp>
inline SharedPtrAbi MakeExternalSharedPtrAbi(
    const Urho3D::SharedPtr<TCpp, Urho3D::RefCounted>& value)
{
    static_assert(!std::is_base_of_v<Urho3D::RefCounted, TCpp>,
        "The external-owner marshaller is only for interface types that do not inherit RefCounted");

    if (value.Get() == nullptr)
    {
        return {};
    }

    auto* holder = new ExternalSharedPtrHolder<TCpp>(value);
    return {value.Get(), holder};
}

template <class TCpp>
inline Urho3D::WeakPtr<TCpp, Urho3D::RefCounted> MakeExternalWeakPtrFromAbi(SharedPtrAbi value) noexcept
{
    auto shared = MakeExternalSharedPtrFromAbi<TCpp>(value);
    return Urho3D::WeakPtr<TCpp, Urho3D::RefCounted>{shared};
}

template <class TCpp>
inline SharedPtrAbi MakeExternalSharedPtrAbiFromWeak(
    const Urho3D::WeakPtr<TCpp, Urho3D::RefCounted>& value)
{
    auto shared = value.Lock();
    return MakeExternalSharedPtrAbi<TCpp>(shared);
}

template <class TCpp, class TRefCounted>
inline TRefCounted* ResolveRefCountedPointer(TCpp* value) noexcept
{
    if (value == nullptr)
    {
        return nullptr;
    }

    if constexpr (std::is_convertible_v<TCpp*, TRefCounted*>)
    {
        return static_cast<TRefCounted*>(value);
    }

    return dynamic_cast<TRefCounted*>(value);
}

template <class TCpp, class TRefCounted>
inline Urho3D::SharedPtr<TCpp, TRefCounted> MakeSharedPtrFromAbi(TCpp* value) noexcept
{
    return value == nullptr
        ? Urho3D::SharedPtr<TCpp, TRefCounted>{}
        : Urho3D::SharedPtr<TCpp, TRefCounted>(value, ResolveRefCountedPointer<TCpp, TRefCounted>(value));
}

template <class TCpp, class TRefCounted>
inline Urho3D::WeakPtr<TCpp, TRefCounted> MakeWeakPtrFromRefCounted(TCpp* value, TRefCounted* refCounted) noexcept
{
    return refCounted == nullptr
        ? Urho3D::WeakPtr<TCpp, TRefCounted>{}
        : Urho3D::WeakPtr<TCpp, TRefCounted>(refCounted->RefCountPtr(), value, refCounted);
}

template <class TCpp, class TRefCounted>
inline Urho3D::WeakPtr<TCpp, TRefCounted> MakeWeakPtrFromAbi(TCpp* value) noexcept
{
    return value == nullptr
        ? Urho3D::WeakPtr<TCpp, TRefCounted>{}
        : MakeWeakPtrFromRefCounted<TCpp, TRefCounted>(value, ResolveRefCountedPointer<TCpp, TRefCounted>(value));
}
}

namespace SharpCpp
{

template <>
struct [[sharpcpp::managed_type(
    public_type = "RefCounted",
    managed_base_type = "global::Urho3D.Interop.RefCountedNativeObject",
    managed_instance_cache_factory = "global::Urho3D.Interop.RefCountedInstanceCache.Shared",
    managed_instance_cache_type = "global::SharpCpp.Support.INativeInstanceCache",
    release_strategy = "base"
)]] Marshaller<Urho3D::RefCounted*>
{
    using cpp_type = Urho3D::RefCounted*;
    using abi_type = Urho3D::RefCounted*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(
    managed_static_interfaces = "global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<{managed_type}>",
    managed_static_interfaces_abstract = true
)]] Marshaller<Urho3D::Object*>
{
    using cpp_type = Urho3D::Object*;
    using abi_type = Urho3D::Object*;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <class TCpp, class TRefCounted>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<Urho3D::SharedPtr<TCpp, TRefCounted>>
{
    using cpp_type = Urho3D::SharedPtr<TCpp, TRefCounted>;
    using abi_type = TCpp*;

    // An lvalue is owned by someone else, for example a native field. Keep its
    // reference. The managed side adds its own reference.
    abi_type ConvertToAbi(cpp_type& value) noexcept
    {
        return value.Get();
    }

    // Detach gives up the reference of a temporary without a delete. The managed side
    // then adds its reference. A Detach on a copy is not correct: the source still
    // releases its reference later and can delete the object.
    abi_type ConvertToAbi(cpp_type&& value) noexcept
    {
        return value.Detach();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return rbfx::detail::MakeSharedPtrFromAbi<TCpp, TRefCounted>(value);
    }
};

template <class TCpp, class TRefCounted>
struct ReturnMarshaller<Urho3D::SharedPtr<TCpp, TRefCounted>>
    : Marshaller<Urho3D::SharedPtr<TCpp, TRefCounted>>
{
};

template <class TCpp, class TRefCounted>
struct Marshaller<const Urho3D::SharedPtr<TCpp, TRefCounted>&>
{
    using cpp_type = const Urho3D::SharedPtr<TCpp, TRefCounted>&;
    using abi_type = TCpp*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value.Get();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = rbfx::detail::MakeSharedPtrFromAbi<TCpp, TRefCounted>(value);
        return storage_;
    }

private:
    mutable Urho3D::SharedPtr<TCpp, TRefCounted> storage_{};
};

// A WeakPtr does not own its object. A WeakPtr return or field read gives a borrowed
// wrapper, as a raw pointer does. An owning wrapper would keep the object alive.
template <class TCpp, class TRefCounted>
struct [[sharpcpp::managed_type(
    public_type = "{0}",
    projection = true
)]] Marshaller<Urho3D::WeakPtr<TCpp, TRefCounted>>
{
    using cpp_type = Urho3D::WeakPtr<TCpp, TRefCounted>;
    using abi_type = TCpp*;

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return value.Get();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return rbfx::detail::MakeWeakPtrFromAbi<TCpp, TRefCounted>(value);
    }
};

template <class TCpp, class TRefCounted>
struct ReturnMarshaller<Urho3D::WeakPtr<TCpp, TRefCounted>>
    : Marshaller<Urho3D::WeakPtr<TCpp, TRefCounted>>
{
};

template <class TCpp, class TRefCounted>
struct Marshaller<const Urho3D::WeakPtr<TCpp, TRefCounted>&>
{
    using cpp_type = const Urho3D::WeakPtr<TCpp, TRefCounted>&;
    using abi_type = TCpp*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value.Get();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = rbfx::detail::MakeWeakPtrFromAbi<TCpp, TRefCounted>(value);
        return storage_;
    }

private:
    mutable Urho3D::WeakPtr<TCpp, TRefCounted> storage_{};
};

template <class TCpp>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Interop.SharedPtr<{0}, {1}>",
    managed_marshaller = "global::Urho3D.Interop.SharedPtrAbiMarshaller<{0}, {1}>",
    return_ownership = "owning"
)]] Marshaller<Urho3D::SharedPtr<TCpp, Urho3D::RefCounted>>
{
    using cpp_type = Urho3D::SharedPtr<TCpp, Urho3D::RefCounted>;
    using abi_type = rbfx::detail::SharedPtrAbi;

    abi_type ConvertToAbi(cpp_type value)
    {
        return rbfx::detail::MakeExternalSharedPtrAbi<TCpp>(value);
    }

    cpp_type ConvertToCpp(abi_type value)
    {
        return rbfx::detail::MakeExternalSharedPtrFromAbi<TCpp>(value);
    }
};

template <class TCpp>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Interop.SharedPtr<{0}, {1}>",
    managed_marshaller = "global::Urho3D.Interop.SharedPtrAbiMarshaller<{0}, {1}>"
)]] Marshaller<const Urho3D::SharedPtr<TCpp, Urho3D::RefCounted>&>
{
    using cpp_type = const Urho3D::SharedPtr<TCpp, Urho3D::RefCounted>&;
    using abi_type = rbfx::detail::SharedPtrAbi;

    abi_type ConvertToAbi(cpp_type value)
    {
        return rbfx::detail::MakeExternalSharedPtrAbi<TCpp>(Urho3D::SharedPtr<TCpp, Urho3D::RefCounted>(value));
    }

    cpp_type ConvertToCpp(abi_type value)
    {
        storage_ = rbfx::detail::MakeExternalSharedPtrFromAbi<TCpp>(value);
        return storage_;
    }

private:
    mutable Urho3D::SharedPtr<TCpp, Urho3D::RefCounted> storage_{};
};

template <class TCpp>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Interop.SharedPtr<{0}, {1}>",
    managed_marshaller = "global::Urho3D.Interop.SharedPtrAbiMarshaller<{0}, {1}>",
    return_ownership = "owning"
)]] Marshaller<Urho3D::WeakPtr<TCpp, Urho3D::RefCounted>>
{
    using cpp_type = Urho3D::WeakPtr<TCpp, Urho3D::RefCounted>;
    using abi_type = rbfx::detail::SharedPtrAbi;

    abi_type ConvertToAbi(const cpp_type& value)
    {
        return rbfx::detail::MakeExternalSharedPtrAbiFromWeak<TCpp>(value);
    }

    cpp_type ConvertToCpp(abi_type value)
    {
        return rbfx::detail::MakeExternalWeakPtrFromAbi<TCpp>(value);
    }
};

template <class TCpp>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Interop.SharedPtr<{0}, {1}>",
    managed_marshaller = "global::Urho3D.Interop.SharedPtrAbiMarshaller<{0}, {1}>"
)]] Marshaller<const Urho3D::WeakPtr<TCpp, Urho3D::RefCounted>&>
{
    using cpp_type = const Urho3D::WeakPtr<TCpp, Urho3D::RefCounted>&;
    using abi_type = rbfx::detail::SharedPtrAbi;

    abi_type ConvertToAbi(cpp_type value)
    {
        return rbfx::detail::MakeExternalSharedPtrAbiFromWeak<TCpp>(value);
    }

    cpp_type ConvertToCpp(abi_type value)
    {
        storage_ = rbfx::detail::MakeExternalWeakPtrFromAbi<TCpp>(value);
        return storage_;
    }

private:
    mutable Urho3D::WeakPtr<TCpp, Urho3D::RefCounted> storage_{};
};

// Preserve the ordinary one-type SharedPtr<RefCounted> projection. The external-owner
// specialization above is for interfaces that are not themselves RefCounted.
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Container.RefCounted",
    projection = true,
    return_ownership = "owning"
)]] Marshaller<Urho3D::SharedPtr<Urho3D::RefCounted, Urho3D::RefCounted>>
{
    using cpp_type = Urho3D::SharedPtr<Urho3D::RefCounted, Urho3D::RefCounted>;
    using abi_type = Urho3D::RefCounted*;

    // An lvalue is owned by someone else, for example a native field. Keep its
    // reference. The managed side adds its own reference.
    abi_type ConvertToAbi(cpp_type& value) noexcept
    {
        return value.Get();
    }

    // Detach gives up the reference of a temporary without a delete. The managed side
    // then adds its reference. A Detach on a copy is not correct: the source still
    // releases its reference later and can delete the object.
    abi_type ConvertToAbi(cpp_type&& value) noexcept
    {
        return value.Detach();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return rbfx::detail::MakeSharedPtrFromAbi<Urho3D::RefCounted, Urho3D::RefCounted>(value);
    }
};

template <>
struct Marshaller<const Urho3D::SharedPtr<Urho3D::RefCounted, Urho3D::RefCounted>&>
{
    using cpp_type = const Urho3D::SharedPtr<Urho3D::RefCounted, Urho3D::RefCounted>&;
    using abi_type = Urho3D::RefCounted*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value.Get();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = rbfx::detail::MakeSharedPtrFromAbi<Urho3D::RefCounted, Urho3D::RefCounted>(value);
        return storage_;
    }

private:
    mutable Urho3D::SharedPtr<Urho3D::RefCounted, Urho3D::RefCounted> storage_{};
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Container.RefCounted",
    projection = true
)]] Marshaller<Urho3D::WeakPtr<Urho3D::RefCounted, Urho3D::RefCounted>>
{
    using cpp_type = Urho3D::WeakPtr<Urho3D::RefCounted, Urho3D::RefCounted>;
    using abi_type = Urho3D::RefCounted*;

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return value.Get();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return rbfx::detail::MakeWeakPtrFromAbi<Urho3D::RefCounted, Urho3D::RefCounted>(value);
    }
};

template <>
struct Marshaller<const Urho3D::WeakPtr<Urho3D::RefCounted, Urho3D::RefCounted>&>
{
    using cpp_type = const Urho3D::WeakPtr<Urho3D::RefCounted, Urho3D::RefCounted>&;
    using abi_type = Urho3D::RefCounted*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value.Get();
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        storage_ = rbfx::detail::MakeWeakPtrFromAbi<Urho3D::RefCounted, Urho3D::RefCounted>(value);
        return storage_;
    }

private:
    mutable Urho3D::WeakPtr<Urho3D::RefCounted, Urho3D::RefCounted> storage_{};
};

struct ExternalOwnerPointerMarshallerProbe
{
};
static_assert(std::is_same_v<ret_t<Urho3D::SharedPtr<Urho3D::RefCounted>>, Urho3D::RefCounted*>);
static_assert(std::is_same_v<ret_t<Urho3D::WeakPtr<Urho3D::RefCounted>>, Urho3D::RefCounted*>);
static_assert(std::is_same_v<ret_t<Urho3D::SharedPtr<ExternalOwnerPointerMarshallerProbe, Urho3D::RefCounted>>,
    rbfx::detail::SharedPtrAbi>);
static_assert(std::is_same_v<ret_t<Urho3D::WeakPtr<ExternalOwnerPointerMarshallerProbe, Urho3D::RefCounted>>,
    rbfx::detail::SharedPtrAbi>);

}
