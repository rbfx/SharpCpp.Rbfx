#include "Urho3D/CSharp/DotNetHost.h"

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

#include <cstdio>
#include <cstdlib>
#include <exception>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace Urho3D
{

namespace
{

using HostString = std::basic_string<char_t>;

// The managed entry point of LoadPlugin. It is in SharpCpp.Rbfx.Bindings/Plugins/NativeHostEntryPoints.cs.
constexpr std::string_view LoadPluginTypeName = "Urho3D.Plugins.NativeHostEntryPoints, SharpCpp.Rbfx.Bindings";
constexpr std::string_view LoadPluginMethodName = "LoadPlugin";
constexpr std::string_view RegisterPluginMethodName = "RegisterPlugin";
using ManagedErrorWriter = void (*)(void* state, const char* text, int length);
using LoadPluginFunction = int (*)(void* pluginManager, const char* assemblyPath, ManagedErrorWriter writeError, void* state);

void AppendManagedError(void* state, const char* text, int length)
{
    if (state && text && length > 0)
        static_cast<std::string*>(state)->append(text, static_cast<std::size_t>(length));
}

#if defined(_WIN32)
HostString ToHostString(std::string_view utf8)
{
    if (utf8.empty())
        return {};
    const int length = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
    HostString result(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), result.data(), length);
    return result;
}

std::string FromHostString(const char_t* text)
{
    if (!text)
        return {};
    const int length = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    if (length <= 1)
        return {};
    std::string result(static_cast<size_t>(length - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text, -1, result.data(), length, nullptr, nullptr);
    return result;
}

HostString PathToHostString(const std::filesystem::path& path) { return path.native(); }
#else
HostString ToHostString(std::string_view utf8) { return HostString(utf8); }
std::string FromHostString(const char_t* text) { return text ? std::string(text) : std::string(); }
HostString PathToHostString(const std::filesystem::path& path) { return path.native(); }
#endif

std::string FormatCode(int code)
{
    char buffer[32];
    std::snprintf(buffer, sizeof(buffer), "0x%08X", static_cast<unsigned>(code));
    return buffer;
}

void* LoadNativeLibrary(const HostString& path)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(LoadLibraryW(path.c_str()));
#else
    return dlopen(path.c_str(), RTLD_LAZY | RTLD_LOCAL);
#endif
}

void* FindExport(void* library, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(static_cast<HMODULE>(library), name));
#else
    return dlsym(library, name);
#endif
}

std::string LastLoadError()
{
#if defined(_WIN32)
    return "error code " + FormatCode(static_cast<int>(GetLastError()));
#else
    const char* message = dlerror();
    return message ? message : "unknown error";
#endif
}

// hostfxr reports detailed errors through a writer that is local to the calling thread.
thread_local std::string* g_errorSink = nullptr;

void HOSTFXR_CALLTYPE HostErrorWriter(const char_t* message)
{
    if (!g_errorSink)
        return;
    if (!g_errorSink->empty())
        g_errorSink->push_back('\n');
    g_errorSink->append(FromHostString(message));
}

// Installs the hostfxr error writer for one scope.
class ErrorCapture
{
public:
    ErrorCapture(void* hostfxrLibrary, std::string& sink)
        : previousSink_(g_errorSink)
    {
        setErrorWriter_ = reinterpret_cast<hostfxr_set_error_writer_fn>(FindExport(hostfxrLibrary, "hostfxr_set_error_writer"));
        g_errorSink = &sink;
        if (setErrorWriter_)
            previousWriter_ = setErrorWriter_(&HostErrorWriter);
    }

    ~ErrorCapture()
    {
        if (setErrorWriter_)
            setErrorWriter_(previousWriter_);
        g_errorSink = previousSink_;
    }

    ErrorCapture(const ErrorCapture&) = delete;
    ErrorCapture& operator=(const ErrorCapture&) = delete;

private:
    hostfxr_set_error_writer_fn setErrorWriter_{};
    hostfxr_error_writer_fn previousWriter_{};
    std::string* previousSink_{};
};

void AppendError(std::string& error, const std::string& text)
{
    if (text.empty())
        return;
    if (!error.empty())
        error.push_back('\n');
    error += text;
}

// Finds hostfxr: explicit root, then the default lookup (DOTNET_ROOT and global install), then the build-time root.
bool FindHostfxrPath(const std::filesystem::path& explicitRoot, HostString& result, std::string& error)
{
    std::vector<HostString> roots;
    std::vector<bool> useRoot;
    if (!explicitRoot.empty())
    {
        roots.push_back(PathToHostString(explicitRoot));
        useRoot.push_back(true);
    }
    roots.emplace_back();
    useRoot.push_back(false);
#if defined(SHARPCPP_RBFX_DEFAULT_DOTNET_ROOT)
    roots.push_back(ToHostString(SHARPCPP_RBFX_DEFAULT_DOTNET_ROOT));
    useRoot.push_back(true);
#endif

    for (size_t i = 0; i < roots.size(); ++i)
    {
        get_hostfxr_parameters parameters{};
        parameters.size = sizeof(get_hostfxr_parameters);
        parameters.dotnet_root = useRoot[i] ? roots[i].c_str() : nullptr;

        std::vector<char_t> buffer(1024);
        size_t size = buffer.size();
        int rc = get_hostfxr_path(buffer.data(), &size, useRoot[i] ? &parameters : nullptr);
        if (rc != 0 && size > buffer.size())
        {
            buffer.resize(size);
            rc = get_hostfxr_path(buffer.data(), &size, useRoot[i] ? &parameters : nullptr);
        }
        if (rc == 0)
        {
            result.assign(buffer.data());
            return true;
        }
        AppendError(
            error,
            "get_hostfxr_path failed (" + FormatCode(rc) + ")"
                + (useRoot[i] ? " for dotnet root '" + FromHostString(roots[i].c_str()) + "'" : std::string(" for the default lookup")));
    }
    return false;
}

} // namespace

DotNetHost::DotNetHost() = default;

DotNetHost::~DotNetHost()
{
    Close();
}

bool DotNetHost::IsInitialized() const
{
    return hostContext_ != nullptr && loadAssemblyFunction_ != nullptr;
}

bool DotNetHost::Initialize(const DotNetHostOptions& options, std::string& error)
{
    error.clear();
    try
    {
        if (IsInitialized())
        {
            error = "The .NET host is already initialized.";
            return false;
        }
        if (options.runtimeConfigPath.empty())
        {
            error = "The runtime config path is empty.";
            return false;
        }

        if (!hostfxrLibrary_)
        {
            HostString hostfxrPath;
            if (!FindHostfxrPath(options.dotnetRoot, hostfxrPath, error))
                return false;
            hostfxrLibrary_ = LoadNativeLibrary(hostfxrPath);
            if (!hostfxrLibrary_)
            {
                error = "Cannot load hostfxr '" + FromHostString(hostfxrPath.c_str()) + "': " + LastLoadError();
                return false;
            }
        }

        const auto initialize = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
            FindExport(hostfxrLibrary_, "hostfxr_initialize_for_runtime_config"));
        const auto getDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
            FindExport(hostfxrLibrary_, "hostfxr_get_runtime_delegate"));
        const auto close = reinterpret_cast<hostfxr_close_fn>(FindExport(hostfxrLibrary_, "hostfxr_close"));
        if (!initialize || !getDelegate || !close)
        {
            error = "hostfxr does not export the required hosting functions.";
            return false;
        }

        ErrorCapture capture(hostfxrLibrary_, error);

        const HostString configPath = PathToHostString(options.runtimeConfigPath);
        hostfxr_handle context = nullptr;
        int rc = initialize(configPath.c_str(), nullptr, &context);
        if (rc < 0 || context == nullptr)
        {
            AppendError(error, "hostfxr_initialize_for_runtime_config failed (" + FormatCode(rc) + ") for '"
                + options.runtimeConfigPath.generic_string() + "'.");
            if (context)
                close(context);
            return false;
        }

        void* loadAssembly = nullptr;
        rc = getDelegate(context, hdt_load_assembly_and_get_function_pointer, &loadAssembly);
        if (rc != 0 || loadAssembly == nullptr)
        {
            AppendError(error, "hostfxr_get_runtime_delegate failed (" + FormatCode(rc) + ").");
            close(context);
            return false;
        }

        // These delegates need .NET 8 or later. Only LoadPlugin needs them, so a failure is not an error here.
        void* loadAssemblyIntoDefault = nullptr;
        if (getDelegate(context, hdt_load_assembly, &loadAssemblyIntoDefault) != 0)
            loadAssemblyIntoDefault = nullptr;
        void* getFunctionPointer = nullptr;
        if (getDelegate(context, hdt_get_function_pointer, &getFunctionPointer) != 0)
            getFunctionPointer = nullptr;

        hostContext_ = context;
        closeFunction_ = reinterpret_cast<void*>(close);
        loadAssemblyFunction_ = loadAssembly;
        loadAssemblyIntoDefaultFunction_ = loadAssemblyIntoDefault;
        getFunctionPointerFunction_ = getFunctionPointer;
        bindingsAssemblyPath_ = options.bindingsAssemblyPath.empty()
            ? options.runtimeConfigPath.parent_path() / "SharpCpp.Rbfx.Bindings.dll"
            : options.bindingsAssemblyPath;
        error.clear();
        return true;
    }
    catch (const std::exception& exception)
    {
        error = std::string("Initialize failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "Initialize failed with an unknown exception.";
        return false;
    }
}

void* DotNetHost::GetFunctionPointer(
    const std::filesystem::path& assemblyPath,
    std::string_view typeName,
    std::string_view methodName,
    std::string_view delegateTypeName,
    std::string& error)
{
    error.clear();
    try
    {
        if (!IsInitialized())
        {
            error = "The .NET host is not initialized.";
            return nullptr;
        }

        const HostString assembly = PathToHostString(assemblyPath);
        const HostString type = ToHostString(typeName);
        const HostString method = ToHostString(methodName);
        const HostString delegateType = ToHostString(delegateTypeName);
        const char_t* delegateTypeArgument = delegateType.empty() ? UNMANAGEDCALLERSONLY_METHOD : delegateType.c_str();

        ErrorCapture capture(hostfxrLibrary_, error);

        void* result = nullptr;
        const auto loadAssembly = reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loadAssemblyFunction_);
        const int rc = loadAssembly(assembly.c_str(), type.c_str(), method.c_str(), delegateTypeArgument, nullptr, &result);
        if (rc != 0 || result == nullptr)
        {
            AppendError(error, "load_assembly_and_get_function_pointer failed (" + FormatCode(rc) + ") for '"
                + std::string(typeName) + "." + std::string(methodName) + "' in '" + assemblyPath.generic_string() + "'.");
            return nullptr;
        }
        error.clear();
        return result;
    }
    catch (const std::exception& exception)
    {
        error = std::string("GetFunctionPointer failed: ") + exception.what();
        return nullptr;
    }
    catch (...)
    {
        error = "GetFunctionPointer failed with an unknown exception.";
        return nullptr;
    }
}

namespace
{

// Calls LoadPlugin or RegisterPlugin of NativeHostEntryPoints. Both have the same signature.
bool CallPluginEntryPoint(void* function, std::string_view action, PluginManager* pluginManager,
    const std::filesystem::path& assemblyPath, std::string& error)
{
    const std::u8string path = std::filesystem::absolute(assemblyPath).u8string();
    const std::string utf8Path(path.begin(), path.end());
    std::string managedError;
    const auto entryPoint = reinterpret_cast<LoadPluginFunction>(function);
    if (entryPoint(pluginManager, utf8Path.c_str(), &AppendManagedError, &managedError) == 0)
    {
        error = "Cannot " + std::string(action) + " the managed plugin '" + assemblyPath.generic_string() + "': "
            + (managedError.empty() ? std::string("unknown error.") : managedError);
        return false;
    }
    return true;
}

} // namespace

bool DotNetHost::LoadPlugin(PluginManager* pluginManager, const std::filesystem::path& assemblyPath, std::string& error)
{
    error.clear();
    try
    {
        if (!IsInitialized())
        {
            error = "The .NET host is not initialized.";
            return false;
        }
        if (!pluginManager)
        {
            error = "The plugin manager is null. Call LoadPlugin after Engine::Initialize.";
            return false;
        }
        if (!loadPluginFunction_ && !LoadPluginEntryPoint(error))
            return false;
        return CallPluginEntryPoint(loadPluginFunction_, "load", pluginManager, assemblyPath, error);
    }
    catch (const std::exception& exception)
    {
        error = std::string("LoadPlugin failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "LoadPlugin failed with an unknown exception.";
        return false;
    }
}

bool DotNetHost::RegisterPlugin(PluginManager* pluginManager, const std::filesystem::path& assemblyPath, std::string& error)
{
    error.clear();
    try
    {
        if (!IsInitialized())
        {
            error = "The .NET host is not initialized.";
            return false;
        }
        if (!pluginManager)
        {
            error = "The plugin manager is null. Call RegisterPlugin after Engine::Initialize.";
            return false;
        }
        if (!registerPluginFunction_ && !LoadPluginEntryPoint(error))
            return false;
        return CallPluginEntryPoint(registerPluginFunction_, "register", pluginManager, assemblyPath, error);
    }
    catch (const std::exception& exception)
    {
        error = std::string("RegisterPlugin failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "RegisterPlugin failed with an unknown exception.";
        return false;
    }
}

bool DotNetHost::LoadPluginEntryPoint(std::string& error)
{
    if (!loadAssemblyIntoDefaultFunction_ || !getFunctionPointerFunction_)
    {
        error = "The runtime does not support load_assembly and get_function_pointer. LoadPlugin needs .NET 8 or later.";
        return false;
    }

    ErrorCapture capture(hostfxrLibrary_, error);

    // The default load context holds the bindings. Plugin load contexts share this copy, so the plugin and the
    // entry point use the same wrapper types.
    const HostString bindingsPath = PathToHostString(std::filesystem::absolute(bindingsAssemblyPath_));
    const auto loadAssembly = reinterpret_cast<load_assembly_fn>(loadAssemblyIntoDefaultFunction_);
    int rc = loadAssembly(bindingsPath.c_str(), nullptr, nullptr);
    if (rc != 0)
    {
        AppendError(error, "load_assembly failed (" + FormatCode(rc) + ") for '" + bindingsAssemblyPath_.generic_string() + "'.");
        return false;
    }

    const HostString type = ToHostString(LoadPluginTypeName);
    const auto getFunctionPointer = reinterpret_cast<get_function_pointer_fn>(getFunctionPointerFunction_);
    void* functions[2] = {};
    const std::string_view methods[2] = {LoadPluginMethodName, RegisterPluginMethodName};
    for (int index = 0; index < 2; ++index)
    {
        const HostString method = ToHostString(methods[index]);
        rc = getFunctionPointer(type.c_str(), method.c_str(), UNMANAGEDCALLERSONLY_METHOD, nullptr, nullptr, &functions[index]);
        if (rc != 0 || functions[index] == nullptr)
        {
            AppendError(error, "get_function_pointer failed (" + FormatCode(rc) + ") for '" + std::string(LoadPluginTypeName)
                + "." + std::string(methods[index]) + "'.");
            return false;
        }
    }

    loadPluginFunction_ = functions[0];
    registerPluginFunction_ = functions[1];
    error.clear();
    return true;
}

void DotNetHost::Close()
{
    if (hostContext_ && closeFunction_)
        reinterpret_cast<hostfxr_close_fn>(closeFunction_)(static_cast<hostfxr_handle>(hostContext_));
    hostContext_ = nullptr;
    closeFunction_ = nullptr;
    loadAssemblyFunction_ = nullptr;
    loadAssemblyIntoDefaultFunction_ = nullptr;
    getFunctionPointerFunction_ = nullptr;
    loadPluginFunction_ = nullptr;
    registerPluginFunction_ = nullptr;
    // hostfxr stays loaded. The runtime cannot be unloaded from the process.
}

} // namespace Urho3D
