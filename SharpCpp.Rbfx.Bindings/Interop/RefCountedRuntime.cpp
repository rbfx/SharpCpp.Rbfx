#include <SharpCpp/Export.hpp>
#include <SharpCpp/detail/RbfxRefCountedAccess.hpp>

#include <Urho3D/Container/RefCounted.h>

#if !URHO3D_CSHARP
#include <Urho3D/Container/Ptr.h>
#include <mutex>
#include <unordered_map>
#include <vector>

namespace sharpcpp::interop::rbfx
{
namespace
{
struct ScriptObject
{
    Urho3D::WeakPtr<Urho3D::RefCounted> object;
    void* handle{};
};
std::mutex scriptObjectMutex;
std::unordered_map<Urho3D::RefCounted*, ScriptObject> scriptObjects;
std::vector<void*> expiredScriptObjects;
}

void* GetScriptObject(Urho3D::RefCounted* instance) noexcept
{
    std::lock_guard lock(scriptObjectMutex);
    const auto it = scriptObjects.find(instance);
    if (it == scriptObjects.end()) return nullptr;
    if (it->second.object.Expired())
    {
        expiredScriptObjects.push_back(it->second.handle);
        scriptObjects.erase(it);
        return nullptr;
    }
    return it->second.handle;
}

void AssignScriptObject(Urho3D::RefCounted* instance, void* handle, bool) noexcept
{
    if (!instance) return;
    std::lock_guard lock(scriptObjectMutex);
    scriptObjects.insert_or_assign(instance, ScriptObject{Urho3D::WeakPtr<Urho3D::RefCounted>(instance), handle});
}

void ResetScriptObject(Urho3D::RefCounted* instance) noexcept
{
    std::lock_guard lock(scriptObjectMutex);
    scriptObjects.erase(instance);
}

void* TakeExpiredScriptObject() noexcept
{
    std::lock_guard lock(scriptObjectMutex);
    // Native destruction can happen without a managed callback in non-C# SDKs.
    // Sweep expired control blocks even when their addresses are never reused.
    for (auto it = scriptObjects.begin(); it != scriptObjects.end();)
    {
        if (it->second.object.Expired())
        {
            expiredScriptObjects.push_back(it->second.handle);
            it = scriptObjects.erase(it);
        }
        else ++it;
    }
    if (expiredScriptObjects.empty()) return nullptr;
    const auto handle = expiredScriptObjects.back();
    expiredScriptObjects.pop_back();
    return handle;
}
}
#endif

extern "C"
{
SHARPCPP_EXPORT void* sharpcpp_rbfx_refcounted_get_script_object(Urho3D::RefCounted* self)
{
    return sharpcpp::interop::rbfx::GetScriptObject(self);
}

SHARPCPP_EXPORT void* sharpcpp_rbfx_refcounted_take_expired_script_object()
{
    return sharpcpp::interop::rbfx::TakeExpiredScriptObject();
}

SHARPCPP_EXPORT void sharpcpp_rbfx_refcounted_set_script_object(Urho3D::RefCounted* self, void* handle, bool isStrong)
{
    sharpcpp::interop::rbfx::AssignScriptObject(self, handle, isStrong);
}

SHARPCPP_EXPORT void sharpcpp_rbfx_refcounted_reset_script_object(Urho3D::RefCounted* self)
{
    sharpcpp::interop::rbfx::ResetScriptObject(self);
}

SHARPCPP_EXPORT void sharpcpp_rbfx_refcounted_add_ref(Urho3D::RefCounted* self)
{
    if (self != nullptr)
    {
        self->AddRef();
    }
}

SHARPCPP_EXPORT void sharpcpp_rbfx_refcounted_release_ref(Urho3D::RefCounted* self)
{
    if (self != nullptr)
    {
        self->ReleaseRef();
    }
}
}
