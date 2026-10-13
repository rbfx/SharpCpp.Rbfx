#include <SharpCpp/Export.hpp>

#include <Urho3D/Container/Ptr.h>
#include <Urho3D/Core/Context.h>
#include <Urho3D/Core/Object.h>
#include <Urho3D/Core/ObjectReflection.h>
#include <Urho3D/Core/TypeInfo.h>

#include <atomic>

namespace
{
// Creates a managed object. The returned object carries one counted reference owned by the managed wrapper.
// The wrapper is already marked as non-owning, so the trampoline drops that reference natively.
using ManagedFactoryCallback = Urho3D::Object* (*)(const Urho3D::TypeInfo*, Urho3D::Context*);

std::atomic<ManagedFactoryCallback> managedFactoryCallback{nullptr};

Urho3D::SharedPtr<Urho3D::Object> ManagedFactoryTrampoline(const Urho3D::TypeInfo* typeInfo, Urho3D::Context* context)
{
    const auto callback = managedFactoryCallback.load();
    if (callback == nullptr)
    {
        return nullptr;
    }

    Urho3D::Object* object = callback(typeInfo, context);
    if (object == nullptr)
    {
        return nullptr;
    }

    // The engine reference is taken first, so releasing the managed reference cannot destroy the object.
    Urho3D::SharedPtr<Urho3D::Object> result{object};
    object->ReleaseRef();
    return result;
}
} // namespace

extern "C"
{
SHARPCPP_EXPORT void sharpcpp_rbfx_set_managed_factory_callback(ManagedFactoryCallback callback)
{
    managedFactoryCallback.store(callback);
}

SHARPCPP_EXPORT void sharpcpp_rbfx_register_managed_factory(
    Urho3D::Context* context, const Urho3D::TypeInfo* typeInfo, const char* category)
{
    if (context == nullptr || typeInfo == nullptr)
    {
        return;
    }

    Urho3D::ObjectReflection* reflection = context->Reflect(typeInfo);
    if (reflection == nullptr)
    {
        return;
    }

    reflection->SetObjectFactory(&ManagedFactoryTrampoline);
    context->SetReflectionCategory(typeInfo, category != nullptr ? category : "");
}
}
