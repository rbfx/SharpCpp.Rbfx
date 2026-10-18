#pragma once

#include <Urho3D/Container/Str.h>
#include <Urho3D/IO/FileSystem.h>

#include <filesystem>

namespace Urho3D
{

/// Returns an absolute UTF-8 path. A relative path is relative to the current directory of the process.
/// The engine has no function for the current directory without a Context, so this uses std::filesystem::current_path.
inline ea::string MakeAbsoluteHostPath(const ea::string& path)
{
    if (path.empty() || IsAbsolutePath(path))
        return path;
    const std::u8string current = std::filesystem::current_path().u8string();
    const ea::string currentDirectory(reinterpret_cast<const char*>(current.data()), static_cast<ea::string::size_type>(current.size()));
    return GetAbsolutePath(path, AddTrailingSlash(GetInternalPath(currentDirectory)));
}

} // namespace Urho3D
