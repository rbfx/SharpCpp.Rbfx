#pragma once

// Export macro of the SharpCpp.Rbfx.Host library.
// SHARPCPP_RBFX_HOST_STATIC: the library is built or used as a static library.
// SHARPCPP_RBFX_HOST_EXPORTS: set while the shared library itself is built.
#if defined(SHARPCPP_RBFX_HOST_STATIC)
#define SHARPCPP_RBFX_HOST_API
#elif defined(_WIN32)
#if defined(SHARPCPP_RBFX_HOST_EXPORTS)
#define SHARPCPP_RBFX_HOST_API __declspec(dllexport)
#else
#define SHARPCPP_RBFX_HOST_API __declspec(dllimport)
#endif
#else
#define SHARPCPP_RBFX_HOST_API __attribute__((visibility("default")))
#endif
