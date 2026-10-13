#include <SharpCpp/Export.hpp>

#include <Urho3D/Container/Ptr.h>
#include <Urho3D/Core/Attribute.h>
#include <Urho3D/Core/ObjectReflection.h>
#include <Urho3D/Core/Variant.h>
#include <Urho3D/IO/Log.h>
#include <Urho3D/Scene/Serializable.h>

#include <atomic>

namespace
{
// Cleared at process exit. Then the accessors do not call into managed code, which can be already unloaded.
std::atomic<bool> managedCallbacksEnabled{true};

// The managed side owns the GC handle. The callbacks catch all managed exceptions.
using ManagedAttributeGetCallback = void (*)(void* handle, const Urho3D::Serializable* ptr, Urho3D::Variant* dest);
using ManagedAttributeSetCallback = void (*)(void* handle, Urho3D::Serializable* ptr, const Urho3D::Variant* src);
using ManagedAttributeFreeCallback = void (*)(void* handle);

// Forwards attribute access to a managed accessor object. The destructor frees the GC handle,
// so the managed accessor lives as long as the last AttributeInfo that holds this accessor.
class ManagedAttributeAccessor final : public Urho3D::AttributeAccessor
{
public:
    ManagedAttributeAccessor(void* handle, ManagedAttributeGetCallback get, ManagedAttributeSetCallback set,
        ManagedAttributeFreeCallback free)
        : handle_(handle)
        , get_(get)
        , set_(set)
        , free_(free)
    {
    }

    ~ManagedAttributeAccessor() override
    {
        if (free_ != nullptr && handle_ != nullptr && managedCallbacksEnabled.load(std::memory_order_acquire))
        {
            free_(handle_);
        }
    }

    void Get(const Urho3D::Serializable* ptr, Urho3D::Variant& dest) const override
    {
        if (get_ != nullptr && handle_ != nullptr && managedCallbacksEnabled.load(std::memory_order_acquire))
        {
            get_(handle_, ptr, &dest);
        }
    }

    void Set(Urho3D::Serializable* ptr, const Urho3D::Variant& src) override
    {
        if (set_ != nullptr && handle_ != nullptr && managedCallbacksEnabled.load(std::memory_order_acquire))
        {
            set_(handle_, ptr, &src);
        }
    }

private:
    void* handle_;
    ManagedAttributeGetCallback get_;
    ManagedAttributeSetCallback set_;
    ManagedAttributeFreeCallback free_;
};
} // namespace

extern "C"
{
// Returns a new accessor that carries one counted reference. The managed wrapper takes its own reference
// and then releases this one, so the wrapper is the only owner until an AttributeInfo copies the accessor.
SHARPCPP_EXPORT Urho3D::AttributeAccessor* sharpcpp_rbfx_create_managed_attribute_accessor(void* handle,
    ManagedAttributeGetCallback get, ManagedAttributeSetCallback set, ManagedAttributeFreeCallback free)
{
    auto* accessor = new ManagedAttributeAccessor(handle, get, set, free);
    accessor->AddRef();
    return accessor;
}

// Adds an attribute with the accessor to the reflection. The AttributeInfo takes its own accessor reference.
// The generated AttributeInfo constructor passes the mode flag set by pointer on the native side
// and by value on the managed side, so this glue builds the AttributeInfo natively.
// Returns false when the reflection does not accept the attribute.
SHARPCPP_EXPORT bool sharpcpp_rbfx_add_managed_attribute(Urho3D::ObjectReflection* reflection, unsigned type,
    const char* name, Urho3D::AttributeAccessor* accessor, const char* const* enumNames, int enumNameCount,
    const Urho3D::Variant* defaultValue, unsigned mode)
{
    if (reflection == nullptr || name == nullptr || accessor == nullptr)
    {
        return false;
    }

    Urho3D::StringVector names;
    for (int i = 0; enumNames != nullptr && i < enumNameCount; ++i)
    {
        names.emplace_back(enumNames[i] != nullptr ? enumNames[i] : "");
    }

    if (reflection->GetAttribute(Urho3D::StringHash{name}) != nullptr)
    {
        URHO3D_LOGWARNING("The attribute '{}' already exists in {}. It is not added.", name, reflection->GetTypeName());
        return false;
    }

    const Urho3D::AttributeInfo info{static_cast<Urho3D::VariantType>(type), name,
        Urho3D::SharedPtr<Urho3D::AttributeAccessor>(accessor), names,
        defaultValue != nullptr ? *defaultValue : Urho3D::Variant::EMPTY,
        Urho3D::AttributeModeFlags{static_cast<Urho3D::AttributeMode>(mode)}};

    const auto countBefore = reflection->GetAttributes().size();
    reflection->AddAttribute(info);
    return reflection->GetAttributes().size() > countBefore;
}

// Stops calls into managed code. Accessors that die later keep their handles. The process is ending.
SHARPCPP_EXPORT void sharpcpp_rbfx_uninstall_managed_attribute_callbacks()
{
    managedCallbacksEnabled.store(false, std::memory_order_release);
}

// Copies src into dest. The managed get callback builds a temporary Variant and assigns it to the engine value.
SHARPCPP_EXPORT void sharpcpp_rbfx_variant_assign(Urho3D::Variant* dest, const Urho3D::Variant* src)
{
    if (dest == nullptr)
    {
        return;
    }

    if (src == nullptr)
    {
        dest->Clear();
        return;
    }

    *dest = *src;
}

SHARPCPP_EXPORT void sharpcpp_rbfx_log_warning(const char* message)
{
    URHO3D_LOGWARNING("{}", message != nullptr ? message : "");
}
}
