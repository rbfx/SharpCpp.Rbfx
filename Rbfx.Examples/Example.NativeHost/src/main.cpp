#include <Urho3D/CSharp/DotNetHost.h>

#include <filesystem>
#include <iostream>
#include <string>

#if defined(_WIN32)
#include <windows.h>
#elif defined(__APPLE__)
#include <mach-o/dyld.h>
#include <vector>
#else
#include <unistd.h>
#include <climits>
#endif

namespace
{

std::filesystem::path GetExecutableDirectory()
{
#if defined(_WIN32)
    std::wstring buffer(32768, L'\0');
    const DWORD size = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    buffer.resize(size);
    return std::filesystem::path(buffer).parent_path();
#elif defined(__APPLE__)
    uint32_t size = 0;
    _NSGetExecutablePath(nullptr, &size);
    std::vector<char> buffer(size + 1, '\0');
    if (_NSGetExecutablePath(buffer.data(), &size) != 0)
        return {};
    return std::filesystem::weakly_canonical(std::filesystem::path(buffer.data())).parent_path();
#else
    char buffer[PATH_MAX + 1] = {};
    const ssize_t size = readlink("/proc/self/exe", buffer, PATH_MAX);
    if (size <= 0)
        return {};
    return std::filesystem::path(std::string(buffer, static_cast<size_t>(size))).parent_path();
#endif
}

using RunFunction = int (*)(int, char**);

} // namespace

int main(int argc, char** argv)
{
    const std::filesystem::path directory = GetExecutableDirectory();
    if (directory.empty())
    {
        std::cerr << "Cannot find the directory of the executable." << std::endl;
        return 1;
    }

    Urho3D::DotNetHost host;
    Urho3D::DotNetHostOptions options;
    options.runtimeConfigPath = directory / "Rbfx.NativeHost.Managed.runtimeconfig.json";

    std::string error;
    if (!host.Initialize(options, error))
    {
        std::cerr << "Cannot start the .NET runtime: " << error << std::endl;
        return 1;
    }

    const auto run = host.GetFunction<RunFunction>(
        directory / "Rbfx.NativeHost.Managed.dll", "RbfxNativeHost.Entry, Rbfx.NativeHost.Managed", "Run", {}, error);
    if (!run)
    {
        std::cerr << "Cannot find the managed entry point: " << error << std::endl;
        return 1;
    }

    return run(argc, argv);
}
