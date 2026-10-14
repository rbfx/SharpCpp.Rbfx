#pragma once

#include "Urho3D/CSharp/DotNetHostExport.h"

#include <stdint.h>

// Attach API of the DotNetHost shim.
// On Android, iOS and Emscripten, DotNetHost does not start a .NET runtime. The managed side runs first
// and pushes the function pointers of this table into the native wrapper library.
// DotNetHost::Initialize returns false while no table is attached.

#ifdef __cplusplus
extern "C"
{
#endif

/// Version of SharpCppRbfxManagedHost.
#define SHARPCPP_RBFX_MANAGED_HOST_VERSION 1

/// Writes error text. The text is UTF-8 and has no terminating zero. state is the pointer that the caller passes.
typedef void (*SharpCppRbfxErrorWriter)(void* state, const char* text, int length);

/// Function table that the managed side pushes into the native wrapper library.
typedef struct SharpCppRbfxManagedHost
{
    /// Must be SHARPCPP_RBFX_MANAGED_HOST_VERSION.
    int32_t version;
    /// Must be sizeof(SharpCppRbfxManagedHost) of the managed side.
    int32_t size;
    /// Returns a native function pointer to a managed static method. Returns NULL on failure.
    /// The delegateTypeName is always empty (UnmanagedCallersOnly method).
    void* (*getFunctionPointer)(
        const char* assemblyPath,
        const char* typeName,
        const char* methodName,
        const char* delegateTypeName,
        SharpCppRbfxErrorWriter writeError,
        void* state);
    /// Loads a managed plugin assembly. Returns 1 on success and 0 on failure.
    int (*loadPlugin)(void* pluginManager, const char* assemblyPath, SharpCppRbfxErrorWriter writeError, void* state);
} SharpCppRbfxManagedHost;

/// Attaches the table. The library copies it. Returns 1 on success.
/// Returns 0 when the table is null, the version is not 1, the size is too small or a function is null.
URHO3D_DOTNETHOST_API int sharpcpp_rbfx_attach_managed_host(const SharpCppRbfxManagedHost* host);

/// Detaches the table. DotNetHost::Initialize returns false after it.
URHO3D_DOTNETHOST_API void sharpcpp_rbfx_detach_managed_host();

#ifdef __cplusplus
}
#endif
