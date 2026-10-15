// The native entry of an rbfx_csharp_package_application on iOS. The function compiles this file into the
// application library together with SOURCES.
//
// The .NET iOS app has its own main, which starts the .NET runtime and runs Program.Main of the head.
// On iOS, URHO3D_DEFINE_APPLICATION_MAIN defines the C function SDL_main, not main. The main of SDL
// (SDL_uikit_main.c in the engine library) is not used: the head does not force-load the engine library,
// so the linker does not take that object. Program.Main calls rbfx_application_start. It gives SDL_main
// to SDL_UIKitRunApp, which runs UIApplicationMain with the application delegate of SDL. SDL then calls
// SDL_main on the main thread when the application is ready. SDL_UIKitRunApp does not return.

extern "C" int SDL_main(int argc, char** argv);
extern "C" int SDL_UIKitRunApp(int argc, char** argv, int (*mainFunction)(int, char**));

extern "C" __attribute__((visibility("default"), used)) int rbfx_application_start()
{
    // SDL keeps a copy of the arguments, but they must be valid during the call.
    static char program[] = "rbfx";
    static char* arguments[] = {program, nullptr};
    return SDL_UIKitRunApp(1, arguments, &SDL_main);
}
