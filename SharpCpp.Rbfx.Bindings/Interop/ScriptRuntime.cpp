#include <SharpCpp/Export.hpp>
#include <SharpCpp/detail/RbfxRefCountedAccess.hpp>

#include <atomic>

#if SHARPCPP_RBFX_SCRIPT_RUNTIME
#include <Urho3D/Script/Script.h>
#endif

namespace
{
// Managed callbacks. They take GC handles that the instance cache stores in RefCounted.
struct ScriptRuntimeCallbacks
{
    // Disposes the managed wrapper of a native object that is about to be deleted.
    void (*dispose)(void* handle);
    // Frees a GC handle. When the target is a wrapper, the wrapper detaches from the deleted native object.
    void (*freeHandle)(void* handle);
    // Allocates a strong GC handle to the target of a handle.
    void* (*cloneHandle)(void* handle);
    // Replaces a GC handle with a strong or weak handle to the same target.
    void* (*recreateHandle)(void* handle, bool strong);
    // Runs a full garbage collection.
    void (*fullGc)();
};

std::atomic<const ScriptRuntimeCallbacks*> runtimeCallbacks{nullptr};

#if SHARPCPP_RBFX_SCRIPT_RUNTIME
// Port of the ScriptRuntimeApi of the legacy rbfx C# bindings. The engine calls it when refcounts change and
// when a RefCounted that holds a wrapper handle dies. Assemblies and plugins are not supported.
class SharpCppScriptRuntimeApi final : public Urho3D::ScriptRuntimeApi
{
public:
    bool VerifyAssembly(const ea::string&) override { return false; }
    bool SetAssemblyVersion(const ea::string&, unsigned) override { return false; }
    void* LoadAssembly(const ea::string&) override { return nullptr; }
    Urho3D::Plugin* CreatePlugin(void*) override { return nullptr; }
    Urho3D::Plugin* CompileResourceScriptPlugin() override { return nullptr; }

    // The engine calls this on the thread that drops the last reference, before the native destructors run.
    void Dispose(Urho3D::RefCounted* instance) override
    {
        void* handle = sharpcpp::interop::rbfx::GetScriptObject(instance);
        if (handle == nullptr)
        {
            return;
        }

        const auto* callbacks = runtimeCallbacks.load(std::memory_order_acquire);
        if (callbacks == nullptr)
        {
            return;
        }

        // The wrapper unregisters itself. This frees the handle and clears the script object.
        callbacks->dispose(handle);

        // A collected weak target or a foreign wrapper leaves the handle in place. Free it here.
        if (void* remaining = sharpcpp::interop::rbfx::GetScriptObject(instance))
        {
            sharpcpp::interop::rbfx::ResetScriptObject(instance);
            callbacks->freeHandle(remaining);
        }
    }

    void FreeGCHandle(void* handle) override
    {
        const auto* callbacks = runtimeCallbacks.load(std::memory_order_acquire);
        if (handle != nullptr && callbacks != nullptr)
        {
            callbacks->freeHandle(handle);
        }
    }

    void* CloneGCHandle(void* handle) override
    {
        const auto* callbacks = runtimeCallbacks.load(std::memory_order_acquire);
        return handle != nullptr && callbacks != nullptr ? callbacks->cloneHandle(handle) : nullptr;
    }

    // Contract from the legacy bindings: refcounts of script-visible objects change on the main thread.
    // RefCounted::AddRef replaces the handle without a lock, so a concurrent wrapper lookup is not safe.
    void* RecreateGCHandle(void* handle, bool strong) override
    {
        const auto* callbacks = runtimeCallbacks.load(std::memory_order_acquire);
        if (handle == nullptr || callbacks == nullptr)
        {
            return handle;
        }
        return callbacks->recreateHandle(handle, strong);
    }

    void FullGC() override
    {
        if (const auto* callbacks = runtimeCallbacks.load(std::memory_order_acquire))
        {
            callbacks->fullGc();
        }
    }
};

// The object is never deleted. The engine can call it from static destructors after the managed runtime stops.
SharpCppScriptRuntimeApi* GetScriptRuntimeApi()
{
    static auto* api = new SharpCppScriptRuntimeApi();
    return api;
}
#endif
} // namespace

extern "C"
{
// Installs the runtime API in the engine. Returns false when the SDK has no script runtime
// or when a different runtime API is already installed.
SHARPCPP_EXPORT bool sharpcpp_rbfx_install_script_runtime(const ScriptRuntimeCallbacks* callbacks)
{
#if SHARPCPP_RBFX_SCRIPT_RUNTIME
    if (callbacks == nullptr)
    {
        return false;
    }

    static ScriptRuntimeCallbacks storage{};
    storage = *callbacks;
    runtimeCallbacks.store(&storage, std::memory_order_release);

    auto* api = GetScriptRuntimeApi();
    Urho3D::ScriptRuntimeApi* current = Urho3D::Script::GetRuntimeApi();
    if (current == nullptr)
    {
        Urho3D::Script::SetRuntimeApi(api);
        return true;
    }
    return current == api;
#else
    (void)callbacks;
    return false;
#endif
}

// Stops calls into managed code. Objects that die later keep their handles. The process is ending.
SHARPCPP_EXPORT void sharpcpp_rbfx_uninstall_script_runtime()
{
    runtimeCallbacks.store(nullptr, std::memory_order_release);
#if SHARPCPP_RBFX_SCRIPT_RUNTIME
    if (Urho3D::Script::GetRuntimeApi() == GetScriptRuntimeApi())
    {
        Urho3D::Script::SetRuntimeApi(nullptr);
    }
#endif
}
}
