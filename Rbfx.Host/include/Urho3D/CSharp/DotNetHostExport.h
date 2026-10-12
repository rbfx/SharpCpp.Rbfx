#pragma once

// Export macro of the SharpCpp.Rbfx.Host library.
// URHO3D_DOTNETHOST_STATIC: the library is built or used as a static library.
// URHO3D_DOTNETHOST_EXPORTS: set while the shared library itself is built.
#if defined(URHO3D_DOTNETHOST_STATIC)
#define URHO3D_DOTNETHOST_API
#elif defined(_WIN32)
#if defined(URHO3D_DOTNETHOST_EXPORTS)
#define URHO3D_DOTNETHOST_API __declspec(dllexport)
#else
#define URHO3D_DOTNETHOST_API __declspec(dllimport)
#endif
#else
#define URHO3D_DOTNETHOST_API __attribute__((visibility("default")))
#endif
