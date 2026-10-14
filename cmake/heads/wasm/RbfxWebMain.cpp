// The native entry of an rbfx_csharp_add_application on Emscripten. The function compiles this file into the
// application library together with SOURCES.
//
// The .NET runtime has its own main in dotnet.native.wasm. So the function compiles SOURCES with the definition
// main=rbfx_application_main, and URHO3D_DEFINE_APPLICATION_MAIN defines rbfx_application_main instead of main.
// That name has C++ linkage. This file gives it the C name rbfx_application_start, which main.js calls.
// The head exports the name (EmccExportedFunction), and EMSCRIPTEN_KEEPALIVE keeps it in the link.

#include <emscripten/emscripten.h>

int rbfx_application_main(int argc, char** argv);

extern "C" EMSCRIPTEN_KEEPALIVE int rbfx_application_start()
{
    // The engine main loop continues after this function returns, so the arguments must stay valid.
    static char program[] = "rbfx";
    static char* arguments[] = {program, nullptr};
    return rbfx_application_main(1, arguments);
}
