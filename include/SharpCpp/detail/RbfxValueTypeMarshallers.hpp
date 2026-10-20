#pragma once

#include <cstring>

#include <SharpCpp/Marshaller.hpp>
#include "RbfxRefCountedMarshallers.hpp"
#include "RbfxMathMarshallers.hpp"

#include <EASTL/array.h>
#include <EASTL/functional.h>
#include <EASTL/list.h>
#include <EASTL/map.h>
#include <EASTL/optional.h>
#include <EASTL/shared_array.h>
#include <EASTL/span.h>
#include <EASTL/string.h>
#include <EASTL/string_view.h>
#include <EASTL/unordered_map.h>
#include <EASTL/vector.h>
#include <Urho3D/Container/FlagSet.h>
#include <Urho3D/Core/Variant.h>

namespace SharpCpp
{

// The generator lowers a by-value FlagSet<E> to the managed enum E.
// The native side must use the ABI of E. The default marshaller passes a class by pointer.
template <class E>
struct Marshaller<Urho3D::FlagSet<E>>
{
    using cpp_type = Urho3D::FlagSet<E>;
    using abi_type = detail::DefaultValueAbiType<E>;

    constexpr abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return static_cast<abi_type>(static_cast<typename cpp_type::Integer>(value));
    }

    constexpr cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return cpp_type{static_cast<typename cpp_type::Integer>(value)};
    }
};

template <class E>
struct Marshaller<const Urho3D::FlagSet<E>> : Marshaller<Urho3D::FlagSet<E>> {};

template <class TArray>
struct SharedArrayMarshallerBase
{
    using cpp_type = TArray;
    using abi_type = TArray*;

    abi_type ConvertToAbi(const cpp_type& value) noexcept
    {
        return new TArray(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value == nullptr ? cpp_type{} : *value;
    }
};

template <class TArray>
struct SharedArrayReferenceMarshallerBase
{
    using cpp_type = TArray&;
    using abi_type = TArray*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return new TArray(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
        {
            // SharpCpp shared array reference cannot be null.
            std::terminate();
        }

        return *value;
    }
};

template <class TArray>
struct SharedArrayConstReferenceMarshallerBase
{
    using cpp_type = const TArray&;
    using abi_type = TArray*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return new TArray(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        if (value == nullptr)
        {
            // SharpCpp shared array reference cannot be null.
            std::terminate();
        }

        return *value;
    }
};

template <class TArray>
struct SharedArrayPointerMarshallerBase
{
    using cpp_type = TArray*;
    using abi_type = TArray*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return value;
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <class TArray>
struct SharedArrayConstPointerMarshallerBase
{
    using cpp_type = const TArray*;
    using abi_type = TArray*;

    abi_type ConvertToAbi(cpp_type value) noexcept
    {
        return const_cast<TArray*>(value);
    }

    cpp_type ConvertToCpp(abi_type value) noexcept
    {
        return value;
    }
};

template <>
struct [[sharpcpp::managed_type(
    managed_base_type = "global::SharpCpp.Support.StringBase",
    public_type = "Ea.String",
    managed_namespace = "Ea",
    managed_wrapper_assembly = "SharpCpp.Ea",
    managed_wrapper_provider_kind = "string",
    managed_wrapper_copyable = true
)]]
Marshaller<eastl::basic_string<char>> : OwnedStringMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<eastl::basic_string<char>&> : OwnedStringReferenceMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<const eastl::basic_string<char>&> : OwnedStringConstReferenceMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<const eastl::basic_string<char>*> : OwnedStringConstPointerMarshallerBase<eastl::basic_string<char>> {};

template <>
struct Marshaller<eastl::basic_string<char>*> : OwnedStringPointerMarshallerBase<eastl::basic_string<char>> {};

template <>
struct ReturnMarshaller<eastl::basic_string<char>> : OwnedStringReturnMarshallerBase<eastl::basic_string<char>> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiMarshaller",
    undefaultable = true
)]]
Marshaller<ea::string_view> : StringViewMarshallerBase<ea::string_view> {};

template <>
struct Marshaller<const ea::string_view&> : StringViewConstReferenceMarshallerBase<ea::string_view> {};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    managed_marshaller = "global::SharpCpp.Support.Utf8SpanAbiReturnMarshaller"
)]] ReturnMarshaller<ea::string_view> : Marshaller<ea::string_view> {};

template <class TSignature>
struct [[sharpcpp::managed_type(callback = true)]] Marshaller<eastl::function<TSignature>>
    : ManagedCallbackMarshallerBase<eastl::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<const eastl::function<TSignature>>
    : ManagedCallbackMarshallerBase<eastl::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<eastl::function<TSignature>&&>
    : ManagedCallbackMarshallerBase<eastl::function<TSignature>, TSignature>
{
};

template <class TSignature>
struct Marshaller<const eastl::function<TSignature>&>
    : ManagedCallbackConstReferenceMarshallerBase<eastl::function<TSignature>, TSignature>
{
};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Ea.Vector<{0}>",
    managed_base_type = "global::SharpCpp.Support.VectorBase<{0}>",
    managed_namespace = "Ea",
    managed_wrapper_assembly = "SharpCpp.Ea",
    managed_wrapper_provider_kind = "vector",
    managed_wrapper_copyable = true
)]]
Marshaller<eastl::vector<T, TAllocator>> : SequenceCollectionMarshallerBase<eastl::vector<T, TAllocator>, T> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::vector<T, TAllocator>&> : OwnedHandleReferenceMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::vector<T, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::vector<T, TAllocator>*> : OwnedHandlePointerMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::vector<T, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct ReturnMarshaller<eastl::vector<T, TAllocator>> : OwnedHandleReturnMarshallerBase<eastl::vector<T, TAllocator>> {};

template <typename T>
    requires is_optional_value_abi_v<T>
struct [[sharpcpp::managed_type(
    public_type = "{0}?",
    managed_marshaller = "global::SharpCpp.Support.OptionalAbiMarshaller<{0}>"
)]]
Marshaller<eastl::optional<T>> : OptionalValueMarshallerBase<eastl::optional<T>, T> {};

template <typename T>
    requires is_optional_value_abi_v<T>
struct Marshaller<const eastl::optional<T>&> : OptionalValueConstReferenceMarshallerBase<eastl::optional<T>, T> {};

template <typename T>
    requires is_optional_handle_abi_v<T>
struct [[sharpcpp::managed_type(
    public_type = "{0}?",
    projection = true
)]]
Marshaller<eastl::optional<T>> : OptionalHandleMarshallerBase<eastl::optional<T>, T> {};

template <typename T>
    requires is_optional_handle_abi_v<T>
struct Marshaller<const eastl::optional<T>&> : OptionalHandleConstReferenceMarshallerBase<eastl::optional<T>, T> {};

template <typename T>
    requires is_optional_handle_abi_v<T>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<eastl::optional<T>> : OptionalHandleReturnMarshallerBase<eastl::optional<T>, T> {};

// eastl::span uses eastl::dynamic_extent as its dynamic-extent value.
template <typename T, size_t Extent>
inline constexpr std::size_t span_dynamic_extent_v<eastl::span<T, Extent>> = eastl::dynamic_extent;

// eastl::span crosses the ABI with no copy, the same as std::span. See SpanMarshallerBase.
template <typename T, size_t Extent>
    requires(!std::is_const_v<T> && is_span_element_abi_v<T>)
struct [[sharpcpp::managed_type(
    public_type = "global::System.Span<{0}>",
    managed_marshaller = "global::SharpCpp.Support.SpanAbiMarshaller<>",
    undefaultable = true
)]]
Marshaller<eastl::span<T, Extent>> : SpanMarshallerBase<eastl::span<T, Extent>> {};

template <typename T, size_t Extent>
    requires is_span_element_abi_v<T>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<{0}>",
    managed_marshaller = "global::SharpCpp.Support.ReadOnlySpanAbiMarshaller<>",
    undefaultable = true
)]]
Marshaller<eastl::span<const T, Extent>> : ConstSpanMarshallerBase<eastl::span<const T, Extent>> {};

template <typename T, size_t Extent>
    requires(!is_span_element_abi_v<T>)
struct Marshaller<eastl::span<T, Extent>> : UnsupportedSpanMarshallerBase<eastl::span<T, Extent>> {};

template <typename T, size_t Extent>
    requires(!std::is_const_v<T> && is_span_element_abi_v<T>)
struct Marshaller<const eastl::span<T, Extent>&>
    : SpanConstReferenceMarshallerBase<eastl::span<T, Extent>, Marshaller<eastl::span<T, Extent>>> {};

template <typename T, size_t Extent>
    requires is_span_element_abi_v<T>
struct Marshaller<const eastl::span<const T, Extent>&>
    : SpanConstReferenceMarshallerBase<eastl::span<const T, Extent>, Marshaller<eastl::span<const T, Extent>>> {};

template <typename T, size_t Extent>
    requires(!std::is_const_v<T> && is_span_element_abi_v<T>)
struct [[sharpcpp::managed_type(
    public_type = "global::System.Span<{0}>",
    managed_marshaller = "global::SharpCpp.Support.SpanAbiReturnMarshaller<>"
)]]
ReturnMarshaller<eastl::span<T, Extent>> : Marshaller<eastl::span<T, Extent>> {};

template <typename T, size_t Extent>
    requires is_span_element_abi_v<T>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<{0}>",
    managed_marshaller = "global::SharpCpp.Support.ReadOnlySpanAbiReturnMarshaller<>"
)]]
ReturnMarshaller<eastl::span<const T, Extent>> : Marshaller<eastl::span<const T, Extent>> {};

template <typename T, eastl_size_t N>
struct [[sharpcpp::managed_type(
    public_type = "Ea.Array<{0}>",
    managed_base_type = "global::SharpCpp.Support.ArrayBase<{0}>",
    managed_namespace = "Ea",
    managed_wrapper_assembly = "SharpCpp.Ea",
    managed_wrapper_provider_kind = "array",
    managed_wrapper_copyable = true,
    fixed_array = true
)]]
Marshaller<eastl::array<T, N>> : FixedArrayValueMarshallerBase<eastl::array<T, N>, N> {};

template <typename T, eastl_size_t N>
struct Marshaller<eastl::array<T, N>&> : FixedArrayReferenceMarshallerBase<eastl::array<T, N>, N> {};

template <typename T, eastl_size_t N>
struct Marshaller<const eastl::array<T, N>&> : FixedArrayConstReferenceMarshallerBase<eastl::array<T, N>, N> {};

template <typename T, eastl_size_t N>
struct Marshaller<eastl::array<T, N>*> : FixedArrayPointerMarshallerBase<eastl::array<T, N>, N> {};

template <typename T, eastl_size_t N>
struct Marshaller<const eastl::array<T, N>*> : FixedArrayConstPointerMarshallerBase<eastl::array<T, N>, N> {};

template <typename T, eastl_size_t N>
struct ReturnMarshaller<eastl::array<T, N>> : FixedArrayReturnMarshallerBase<eastl::array<T, N>, N> {};

template <typename T, typename TAllocator, typename TDeleter>
struct [[sharpcpp::managed_type(
    public_type = "Ea.SharedArray<{0}>",
    managed_base_type = "global::SharpCpp.Support.SharedArrayBase<{0}>",
    managed_namespace = "Ea",
    managed_wrapper_assembly = "SharpCpp.Ea",
    managed_wrapper_provider_kind = "dynamic_array",
    dynamic_array = true,
    return_ownership = "owning"
)]]
Marshaller<eastl::shared_array<T, TAllocator, TDeleter>> : SharedArrayMarshallerBase<eastl::shared_array<T, TAllocator, TDeleter>> {};

template <typename T, typename TAllocator, typename TDeleter>
struct Marshaller<eastl::shared_array<T, TAllocator, TDeleter>&> : SharedArrayReferenceMarshallerBase<eastl::shared_array<T, TAllocator, TDeleter>> {};

template <typename T, typename TAllocator, typename TDeleter>
struct Marshaller<const eastl::shared_array<T, TAllocator, TDeleter>&> : SharedArrayConstReferenceMarshallerBase<eastl::shared_array<T, TAllocator, TDeleter>> {};

template <typename T, typename TAllocator, typename TDeleter>
struct Marshaller<eastl::shared_array<T, TAllocator, TDeleter>*> : SharedArrayPointerMarshallerBase<eastl::shared_array<T, TAllocator, TDeleter>> {};

template <typename T, typename TAllocator, typename TDeleter>
struct Marshaller<const eastl::shared_array<T, TAllocator, TDeleter>*> : SharedArrayConstPointerMarshallerBase<eastl::shared_array<T, TAllocator, TDeleter>> {};

template <typename T, typename TAllocator, typename TDeleter>
struct ReturnMarshaller<eastl::shared_array<T, TAllocator, TDeleter>> : SharedArrayMarshallerBase<eastl::shared_array<T, TAllocator, TDeleter>> {};

template <typename T, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Ea.List<{0}>",
    managed_base_type = "global::SharpCpp.Support.ListBase<{0}>",
    managed_namespace = "Ea",
    managed_wrapper_assembly = "SharpCpp.Ea",
    managed_wrapper_provider_kind = "list",
    managed_wrapper_copyable = true
)]]
Marshaller<eastl::list<T, TAllocator>> : ListCollectionMarshallerBase<eastl::list<T, TAllocator>, T> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::list<T, TAllocator>&> : OwnedHandleReferenceMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::list<T, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<eastl::list<T, TAllocator>*> : OwnedHandlePointerMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct Marshaller<const eastl::list<T, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename T, typename TAllocator>
struct ReturnMarshaller<eastl::list<T, TAllocator>> : OwnedHandleReturnMarshallerBase<eastl::list<T, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct [[sharpcpp::managed_type(
    public_type = "Ea.Map<{0}, {1}>",
    managed_base_type = "global::SharpCpp.Support.MapBase<{0}, {1}>",
    managed_base_type_constraints = "where {0} : notnull",
    managed_namespace = "Ea",
    managed_wrapper_assembly = "SharpCpp.Ea",
    managed_wrapper_provider_kind = "map",
    managed_wrapper_copyable = true
)]]
Marshaller<eastl::map<TKey, TValue, TCompare, TAllocator>> : MapCollectionMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>, TKey, TValue> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<eastl::map<TKey, TValue, TCompare, TAllocator>&> : OwnedHandleReferenceMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<const eastl::map<TKey, TValue, TCompare, TAllocator>&> : OwnedHandleConstReferenceMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<eastl::map<TKey, TValue, TCompare, TAllocator>*> : OwnedHandlePointerMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct Marshaller<const eastl::map<TKey, TValue, TCompare, TAllocator>*> : OwnedHandleConstPointerMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename TCompare, typename TAllocator>
struct ReturnMarshaller<eastl::map<TKey, TValue, TCompare, TAllocator>> : OwnedHandleReturnMarshallerBase<eastl::map<TKey, TValue, TCompare, TAllocator>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct [[sharpcpp::managed_type(
    public_type = "Ea.UnorderedMap<{0}, {1}>",
    managed_base_type = "global::SharpCpp.Support.UnorderedMapBase<{0}, {1}>",
    managed_base_type_constraints = "where {0} : notnull",
    managed_namespace = "Ea",
    managed_wrapper_assembly = "SharpCpp.Ea",
    managed_wrapper_provider_kind = "unordered_map",
    managed_wrapper_copyable = true
)]]
Marshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> : MapCollectionMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>, TKey, TValue> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>&> : OwnedHandleReferenceMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<const eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>&> : OwnedHandleConstReferenceMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>*> : OwnedHandlePointerMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct Marshaller<const eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>*> : OwnedHandleConstPointerMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

template <typename TKey, typename TValue, typename THash, typename TPredicate, typename TAllocator, bool CacheHashCode>
struct ReturnMarshaller<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> : OwnedHandleReturnMarshallerBase<eastl::unordered_map<TKey, TValue, THash, TPredicate, TAllocator, CacheHashCode>> {};

// ═════════════════════════════════════════════════════════════════════
// ResourceRef is exposed as an owned handle wrapper even though the C++ type
// is commonly passed and returned by value.
// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(owned_handle = true)]]
Marshaller<Urho3D::ResourceRef> : OwnedHandleMarshallerBase<Urho3D::ResourceRef> {};

template <>
struct Marshaller<Urho3D::ResourceRef&> : OwnedHandleReferenceMarshallerBase<Urho3D::ResourceRef> {};

template <>
struct Marshaller<const Urho3D::ResourceRef&> : OwnedHandleConstReferenceMarshallerBase<Urho3D::ResourceRef> {};

template <>
struct Marshaller<Urho3D::ResourceRef*> : OwnedHandlePointerMarshallerBase<Urho3D::ResourceRef> {};

template <>
struct Marshaller<const Urho3D::ResourceRef*> : OwnedHandleConstPointerMarshallerBase<Urho3D::ResourceRef> {};

template <>
struct [[sharpcpp::managed_type(return_ownership = "owning")]]
ReturnMarshaller<Urho3D::ResourceRef> : OwnedHandleReturnMarshallerBase<Urho3D::ResourceRef> {};

// ═════════════════════════════════════════════════════════════════════
// Parameter ABI: pointer-to-original-type via Marshaller<const T&> specialization.
//   The generic Marshaller<const T&> dereferences through Marshaller<T>::abi_type,
//   which would double-indirect if Marshaller<T>::abi_type is also a pointer.
//   Keeping Marshaller<T>::abi_type = T (value) avoids this.
// Return ABI: ABI struct via ReturnMarshaller<T> specialization.
// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector2",
    marshaller_type = "global::Urho3D.Math.Vector2",
    suppress_emission = true
)]]
Marshaller<Urho3D::Vector2>
{
    using cpp_type = Urho3D::Vector2;
    using abi_type = Urho3D::Vector2;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::Vector2&>
{
    using cpp_type = const Urho3D::Vector2&;
    using abi_type = const Urho3D::Vector2*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Vector2>
{
    using cpp_type = Urho3D::Vector2;
    using abi_type = Vector2Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCpp(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector3",
    marshaller_type = "global::Urho3D.Math.Vector3",
    suppress_emission = true
)]]
Marshaller<Urho3D::Vector3>
{
    using cpp_type = Urho3D::Vector3;
    using abi_type = Urho3D::Vector3;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::Vector3&>
{
    using cpp_type = const Urho3D::Vector3&;
    using abi_type = const Urho3D::Vector3*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Vector3>
{
    using cpp_type = Urho3D::Vector3;
    using abi_type = Vector3Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCpp(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Vector4",
    marshaller_type = "global::Urho3D.Math.Vector4",
    suppress_emission = true
)]]
Marshaller<Urho3D::Vector4>
{
    using cpp_type = Urho3D::Vector4;
    using abi_type = Urho3D::Vector4;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::Vector4&>
{
    using cpp_type = const Urho3D::Vector4&;
    using abi_type = const Urho3D::Vector4*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Vector4>
{
    using cpp_type = Urho3D::Vector4;
    using abi_type = Vector4Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCpp(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.IntVector2",
    marshaller_type = "global::Urho3D.Math.IntVector2",
    suppress_emission = true
)]]
Marshaller<Urho3D::IntVector2>
{
    using cpp_type = Urho3D::IntVector2;
    using abi_type = Urho3D::IntVector2;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::IntVector2&>
{
    using cpp_type = const Urho3D::IntVector2&;
    using abi_type = const Urho3D::IntVector2*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.IntVector3",
    marshaller_type = "global::Urho3D.Math.IntVector3",
    suppress_emission = true
)]]
Marshaller<Urho3D::IntVector3>
{
    using cpp_type = Urho3D::IntVector3;
    using abi_type = Urho3D::IntVector3;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::IntVector3&>
{
    using cpp_type = const Urho3D::IntVector3&;
    using abi_type = const Urho3D::IntVector3*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.DoubleVector2",
    marshaller_type = "global::Urho3D.Math.DoubleVector2",
    suppress_emission = true
)]]
Marshaller<Urho3D::DoubleVector2>
{
    using cpp_type = Urho3D::DoubleVector2;
    using abi_type = Urho3D::DoubleVector2;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::DoubleVector2&>
{
    using cpp_type = const Urho3D::DoubleVector2&;
    using abi_type = const Urho3D::DoubleVector2*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.DoubleVector3",
    marshaller_type = "global::Urho3D.Math.DoubleVector3",
    suppress_emission = true
)]]
Marshaller<Urho3D::DoubleVector3>
{
    using cpp_type = Urho3D::DoubleVector3;
    using abi_type = Urho3D::DoubleVector3;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct Marshaller<const Urho3D::DoubleVector3&>
{
    using cpp_type = const Urho3D::DoubleVector3&;
    using abi_type = const Urho3D::DoubleVector3*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Quaternion",
    marshaller_type = "global::Urho3D.Math.Quaternion",
    suppress_emission = true
)]]
Marshaller<Urho3D::Quaternion>
{
    using cpp_type = Urho3D::Quaternion;
    // Quaternion has a user-provided copy constructor, so the Itanium ABI passes and returns it
    // in memory. Cross the boundary as a trivial struct that matches the managed layout.
    using abi_type = QuaternionAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCpp(v); }
};

template <>
struct Marshaller<Urho3D::Quaternion&> : ProjectedValueReferenceMarshallerBase<Urho3D::Quaternion> {};

template <>
struct Marshaller<const Urho3D::Quaternion&>
{
    using cpp_type = const Urho3D::Quaternion&;
    using abi_type = const Urho3D::Quaternion*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct ReturnMarshaller<Urho3D::Quaternion>
{
    using cpp_type = Urho3D::Quaternion;
    using abi_type = QuaternionAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCpp(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Color",
    marshaller_type = "global::Urho3D.Math.Color",
    suppress_emission = true
)]]
Marshaller<Urho3D::Color>
{
    using cpp_type = Urho3D::Color;
    using abi_type = Urho3D::Color;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.StringHash",
    marshaller_type = "global::Urho3D.Math.StringHash",
    suppress_emission = true
)]]
Marshaller<Urho3D::StringHash>
{
    using cpp_type = Urho3D::StringHash;
    using abi_type = unsigned int;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v.Value(); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return static_cast<cpp_type>(v); }
};

static_assert(std::is_same_v<ret_t<Urho3D::StringHash>, unsigned int>);

template <>
struct Marshaller<const Urho3D::StringHash&>
{
    using cpp_type = const Urho3D::StringHash&;
    using abi_type = const unsigned int*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return reinterpret_cast<abi_type>(&v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *reinterpret_cast<const Urho3D::StringHash*>(v); }
};

// Generic Marshaller<T&> produces abi_type = Marshaller<T>::abi_type* and
// ConvertToCpp(abi_type) → return *value.  Since StringHash's abi_type is
// unsigned int, that would try to return an unsigned int as
// Urho3D::StringHash&, which fails.  StringHash wraps unsigned int so
// pointer reinterpretation is safe — no mutable state needed.
template <>
struct Marshaller<Urho3D::StringHash&>
{
    using cpp_type = Urho3D::StringHash&;
    using abi_type = unsigned int*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return reinterpret_cast<abi_type>(&v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *reinterpret_cast<Urho3D::StringHash*>(v); }
};

// ═════════════════════════════════════════════════════════════════════

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.BoundingBox",
    marshaller_type = "global::Urho3D.Math.BoundingBox",
    suppress_emission = true
)]]
Marshaller<Urho3D::BoundingBox>
{
    using cpp_type = Urho3D::BoundingBox;
    // BoundingBox has a user-provided copy constructor, so the Itanium ABI passes and returns it
    // in memory. Cross the boundary as a trivial struct that matches the managed layout.
    using abi_type = BoundingBoxAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCpp(v); }
};

template <>
struct Marshaller<Urho3D::BoundingBox&> : ProjectedValueReferenceMarshallerBase<Urho3D::BoundingBox> {};

template <>
struct Marshaller<const Urho3D::BoundingBox&>
{
    using cpp_type = const Urho3D::BoundingBox&;
    using abi_type = const Urho3D::BoundingBox*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct Marshaller<Urho3D::BoundingBox*> : ProjectedValuePointerMarshallerBase<Urho3D::BoundingBox> {};

template <>
struct Marshaller<const Urho3D::BoundingBox*> : ProjectedValueConstPointerMarshallerBase<Urho3D::BoundingBox> {};

template <>
struct ReturnMarshaller<Urho3D::BoundingBox>
{
    using cpp_type = Urho3D::BoundingBox;
    using abi_type = BoundingBoxAbi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCpp(v); }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix2",
    marshaller_type = "global::Urho3D.Math.Matrix2",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix2>
{
    using cpp_type = Urho3D::Matrix2;
    using abi_type = Urho3D::Matrix2;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix3",
    marshaller_type = "global::Urho3D.Math.Matrix3",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix3>
{
    using cpp_type = Urho3D::Matrix3;
    using abi_type = Urho3D::Matrix3;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix3x4",
    marshaller_type = "global::Urho3D.Math.Matrix3x4",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix3x4>
{
    using cpp_type = Urho3D::Matrix3x4;
    using abi_type = Urho3D::Matrix3x4;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Matrix4",
    marshaller_type = "global::Urho3D.Math.Matrix4",
    suppress_emission = true
)]]
Marshaller<Urho3D::Matrix4>
{
    using cpp_type = Urho3D::Matrix4;
    // Matrix4 has a user-provided copy constructor, so the Itanium ABI passes and returns it
    // in memory. Cross the boundary as a trivial struct that matches the managed layout.
    using abi_type = Matrix4Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(abi_type v) noexcept { return ToCpp(v); }
};

template <>
struct Marshaller<Urho3D::Matrix4&> : ProjectedValueReferenceMarshallerBase<Urho3D::Matrix4> {};

template <>
struct Marshaller<const Urho3D::Matrix4&>
{
    using cpp_type = const Urho3D::Matrix4&;
    using abi_type = const Urho3D::Matrix4*;
    abi_type ConvertToAbi(cpp_type v) noexcept { return &v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return *v; }
};

template <>
struct Marshaller<Urho3D::Matrix4*> : ProjectedValuePointerMarshallerBase<Urho3D::Matrix4> {};

template <>
struct Marshaller<const Urho3D::Matrix4*> : ProjectedValueConstPointerMarshallerBase<Urho3D::Matrix4> {};

template <>
struct ReturnMarshaller<Urho3D::Matrix4>
{
    using cpp_type = Urho3D::Matrix4;
    using abi_type = Matrix4Abi;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return ToAbi(v); }
    cpp_type ConvertToCpp(const abi_type& v) noexcept { return ToCpp(v); }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Plane",
    marshaller_type = "global::Urho3D.Math.Plane",
    suppress_emission = true
)]]
Marshaller<Urho3D::Plane>
{
    using cpp_type = Urho3D::Plane;
    using abi_type = Urho3D::Plane;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Ray",
    marshaller_type = "global::Urho3D.Math.Ray",
    suppress_emission = true
)]]
Marshaller<Urho3D::Ray>
{
    using cpp_type = Urho3D::Ray;
    using abi_type = Urho3D::Ray;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Sphere",
    marshaller_type = "global::Urho3D.Math.Sphere",
    suppress_emission = true
)]]
Marshaller<Urho3D::Sphere>
{
    using cpp_type = Urho3D::Sphere;
    using abi_type = Urho3D::Sphere;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Rect",
    marshaller_type = "global::Urho3D.Math.Rect",
    suppress_emission = true
)]]
Marshaller<Urho3D::Rect>
{
    using cpp_type = Urho3D::Rect;
    using abi_type = Urho3D::Rect;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.IntRect",
    marshaller_type = "global::Urho3D.Math.IntRect",
    suppress_emission = true
)]]
Marshaller<Urho3D::IntRect>
{
    using cpp_type = Urho3D::IntRect;
    using abi_type = Urho3D::IntRect;
    abi_type ConvertToAbi(const cpp_type& v) noexcept { return v; }
    cpp_type ConvertToCpp(abi_type v) noexcept { return v; }
};

template <>
struct [[sharpcpp::managed_type(
    public_type = "global::Urho3D.Math.Transform",
    marshaller_type = "global::Urho3D.Math.Transform",
    suppress_emission = true
)]]
Marshaller<Urho3D::Transform>
{
    // Quaternion has a user-provided copy constructor, so Transform is not trivially copyable.
    // The ABI type has the same layout and is trivially copyable, so it returns like a C struct.
    struct AbiTransform
    {
        Urho3D::Vector3 position_;
        float rotation_[4];
        Urho3D::Vector3 scale_;
    };

    using cpp_type = Urho3D::Transform;
    using abi_type = AbiTransform;

    static_assert(sizeof(AbiTransform) == sizeof(Urho3D::Transform));
    static_assert(sizeof(Urho3D::Quaternion) == sizeof(float) * 4);

    abi_type ConvertToAbi(const cpp_type& v) noexcept
    {
        abi_type result;
        result.position_ = v.position_;
        std::memcpy(result.rotation_, &v.rotation_, sizeof(result.rotation_));
        result.scale_ = v.scale_;
        return result;
    }

    cpp_type ConvertToCpp(abi_type v) noexcept
    {
        cpp_type result;
        result.position_ = v.position_;
        std::memcpy(static_cast<void*>(&result.rotation_), v.rotation_, sizeof(v.rotation_));
        result.scale_ = v.scale_;
        return result;
    }
};

namespace detail
{
// Transform has the same bytes as its ABI type but is not trivially copyable (Quaternion has a
// user-provided copy constructor), so the generic layout check rejects it.
template <>
struct is_layout_compatible_pair<Urho3D::Transform, Marshaller<Urho3D::Transform>::abi_type, void>
    : std::bool_constant<
          sizeof(Urho3D::Transform) == sizeof(Marshaller<Urho3D::Transform>::abi_type) &&
          alignof(Urho3D::Transform) == alignof(Marshaller<Urho3D::Transform>::abi_type)>
{
};
} // namespace detail

} // namespace SharpCpp
