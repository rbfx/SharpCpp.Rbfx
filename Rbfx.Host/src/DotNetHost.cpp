#include "Urho3D/CSharp/DotNetHost.h"
#include "HostPaths.h"

#include <Urho3D/Container/Str.h>
#include <Urho3D/IO/FileSystem.h>

#include <EASTL/vector.h>

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

#include <exception>

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

// char_t is the character type of hostfxr: char, or wchar_t on Windows.
using HostString = ea::basic_string<char_t>;

// The managed entry point of LoadPlugin. It is in SharpCpp.Rbfx.Bindings/Plugins/NativeHostEntryPoints.cs.
constexpr ea::string_view LoadPluginTypeName = "Urho3D.Plugins.NativeHostEntryPoints, SharpCpp.Rbfx.Bindings";
constexpr ea::string_view LoadPluginMethodName = "LoadPlugin";
constexpr ea::string_view RegisterPluginMethodName = "RegisterPlugin";
using ManagedErrorWriter = void (*)(void* state, const char* text, int length);
using LoadPluginFunction = int (*)(void* pluginManager, const char* assemblyPath, ManagedErrorWriter writeError, void* state);

void AppendManagedError(void* state, const char* text, int length)
{
    if (state && text && length > 0)
        static_cast<ea::string*>(state)->append(text, static_cast<std::size_t>(length));
}

#if defined(_WIN32)
HostString ToHostString(ea::string_view utf8)
{
    if (utf8.empty())
        return {};
    const int length = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
    HostString result(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), result.data(), length);
    return result;
}

ea::string FromHostString(const char_t* text)
{
    if (!text)
        return {};
    const int length = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    if (length <= 1)
        return {};
    ea::string result(static_cast<size_t>(length - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text, -1, result.data(), length, nullptr, nullptr);
    return result;
}

#else
HostString ToHostString(ea::string_view utf8) { return HostString(utf8.data(), utf8.size()); }
ea::string FromHostString(const char_t* text) { return text ? ea::string(text) : ea::string(); }
#endif

ea::string ToString(ea::string_view text) { return ea::string(text.data(), text.size()); }

HostString PathToHostString(const ea::string& path) { return ToHostString(ea::string_view(path.data(), path.size())); }

ea::string FormatCode(int code)
{
    return ea::string(ea::string::CtorSprintf(), "0x%08X", static_cast<unsigned>(code));
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

ea::string LastLoadError()
{
#if defined(_WIN32)
    return "error code " + FormatCode(static_cast<int>(GetLastError()));
#else
    const char* message = dlerror();
    return message ? message : "unknown error";
#endif
}

// hostfxr reports detailed errors through a writer that is local to the calling thread.
thread_local ea::string* g_errorSink = nullptr;

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
    ErrorCapture(void* hostfxrLibrary, ea::string& sink)
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
    ea::string* previousSink_{};
};

void AppendError(ea::string& error, const ea::string& text)
{
    if (text.empty())
        return;
    if (!error.empty())
        error.push_back('\n');
    error += text;
}

// Finds hostfxr: explicit root, then the default lookup (DOTNET_ROOT and global install), then the build-time root.
bool FindHostfxrPath(const ea::string& explicitRoot, HostString& result, ea::string& error)
{
    ea::vector<HostString> roots;
    ea::vector<bool> useRoot;
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

        ea::vector<char_t> buffer(1024);
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
                + (useRoot[i] ? " for dotnet root '" + FromHostString(roots[i].c_str()) + "'" : ea::string(" for the default lookup")));
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

bool DotNetHost::Initialize(const DotNetHostOptions& options, ea::string& error)
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
                + options.runtimeConfigPath + "'.");
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
            ? GetPath(options.runtimeConfigPath) + "SharpCpp.Rbfx.Bindings.dll"
            : options.bindingsAssemblyPath;
        error.clear();
        return true;
    }
    catch (const std::exception& exception)
    {
        error = ea::string("Initialize failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "Initialize failed with an unknown exception.";
        return false;
    }
}

void* DotNetHost::GetFunctionPointer(
    const ea::string& assemblyPath,
    ea::string_view typeName,
    ea::string_view methodName,
    ea::string_view delegateTypeName,
    ea::string& error)
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
                + ToString(typeName) + "." + ToString(methodName) + "' in '" + assemblyPath + "'.");
            return nullptr;
        }
        error.clear();
        return result;
    }
    catch (const std::exception& exception)
    {
        error = ea::string("GetFunctionPointer failed: ") + exception.what();
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
bool CallPluginEntryPoint(void* function, const char* action, PluginManager* pluginManager,
    const ea::string& assemblyPath, ea::string& error)
{
    const ea::string utf8Path = MakeAbsoluteHostPath(assemblyPath);
    ea::string managedError;
    const auto entryPoint = reinterpret_cast<LoadPluginFunction>(function);
    if (entryPoint(pluginManager, utf8Path.c_str(), &AppendManagedError, &managedError) == 0)
    {
        error = ea::string("Cannot ") + action + " the managed plugin '" + assemblyPath + "': "
            + (managedError.empty() ? ea::string("unknown error.") : managedError);
        return false;
    }
    return true;
}

} // namespace

bool DotNetHost::LoadPlugin(PluginManager* pluginManager, const ea::string& assemblyPath, ea::string& error)
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
        error = ea::string("LoadPlugin failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "LoadPlugin failed with an unknown exception.";
        return false;
    }
}

bool DotNetHost::RegisterPlugin(PluginManager* pluginManager, const ea::string& assemblyPath, ea::string& error)
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
        error = ea::string("RegisterPlugin failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "RegisterPlugin failed with an unknown exception.";
        return false;
    }
}

bool DotNetHost::LoadPluginEntryPoint(ea::string& error)
{
    if (!loadAssemblyIntoDefaultFunction_ || !getFunctionPointerFunction_)
    {
        error = "The runtime does not support load_assembly and get_function_pointer. LoadPlugin needs .NET 8 or later.";
        return false;
    }

    ErrorCapture capture(hostfxrLibrary_, error);

    // The default load context holds the bindings. Plugin load contexts share this copy, so the plugin and the
    // entry point use the same wrapper types.
    const HostString bindingsPath = PathToHostString(MakeAbsoluteHostPath(bindingsAssemblyPath_));
    const auto loadAssembly = reinterpret_cast<load_assembly_fn>(loadAssemblyIntoDefaultFunction_);
    int rc = loadAssembly(bindingsPath.c_str(), nullptr, nullptr);
    if (rc != 0)
    {
        AppendError(error, "load_assembly failed (" + FormatCode(rc) + ") for '" + bindingsAssemblyPath_ + "'.");
        return false;
    }

    const HostString type = ToHostString(LoadPluginTypeName);
    const auto getFunctionPointer = reinterpret_cast<get_function_pointer_fn>(getFunctionPointerFunction_);
    void* functions[2] = {};
    const ea::string_view methods[2] = {LoadPluginMethodName, RegisterPluginMethodName};
    for (int index = 0; index < 2; ++index)
    {
        const HostString method = ToHostString(methods[index]);
        rc = getFunctionPointer(type.c_str(), method.c_str(), UNMANAGEDCALLERSONLY_METHOD, nullptr, nullptr, &functions[index]);
        if (rc != 0 || functions[index] == nullptr)
        {
            AppendError(error, "get_function_pointer failed (" + FormatCode(rc) + ") for '" + ToString(LoadPluginTypeName)
                + "." + ToString(methods[index]) + "'.");
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
