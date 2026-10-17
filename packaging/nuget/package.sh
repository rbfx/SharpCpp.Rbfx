#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
PACKAGE_ROOT="$REPO_ROOT/build/package/nuget"
# Stage and out folders are per RID, so builds of different RIDs can run in parallel.
# create_packages sets them from the RID.
STAGE_DIR=""
OUT_DIR=""
LOCAL_REPOSITORY_DIR="$PACKAGE_ROOT/local-repository"
FETCH_SDK_SCRIPT="$REPO_ROOT/eng/fetch-rbfx-sdk.sh"
SDK_OUTPUT_DIR="$REPO_ROOT/build/sdk"

DOTNET="${DOTNET:-dotnet}"
NUGET="${NUGET:-nuget}"

usage() {
  cat >&2 <<'EOF'
Usage: package.sh <action> [options]

Actions:
  fetch-sdk     Fetch one rbfx prebuilt SDK with eng/fetch-rbfx-sdk.sh
  fetch-sdks    Fetch all known rbfx prebuilt SDKs with eng/fetch-rbfx-sdk.sh
  create        Build, stage, and pack SDK-style NuGet packages
  local-repo    Create a local NuGet repository from packages in <rid>/out/ (option --rid)

Use 'package.sh <action> --help' for action-specific options.
EOF
}

default_rid() {
  local kernel machine
  kernel="$(uname -s)"
  machine="$(uname -m)"

  case "$kernel" in
    Linux) echo "linux-x64" ;;
    Darwin)
      if [ "$machine" = "arm64" ]; then
        echo "osx-arm64"
      else
        echo "osx-x64"
      fi
      ;;
    MINGW*|MSYS*|CYGWIN*) echo "win-x64" ;;
    *)
      echo "Error: cannot infer runtime identifier for $kernel/$machine" >&2
      return 1
      ;;
  esac
}

# Runs eng/fetch-rbfx-sdk.sh, the single source of truth for SDK downloads.
# Its log goes to stderr. With --print-root, stdout is the SDK root only.
run_fetch_sdk() {
  bash "$FETCH_SDK_SCRIPT" "$@"
}

# Sets FETCH_RID and FETCH_ARGS from the options shared by fetch-sdk and fetch-sdks.
parse_fetch_options() {
  FETCH_RID=""
  FETCH_ARGS=()
  while [ $# -gt 0 ]; do
    case "$1" in
      --rid)
        [ $# -ge 2 ] || { echo "Error: $1 needs a value." >&2; return 1; }
        FETCH_RID="$2"
        shift 2
        ;;
      --rbfx-tag|--output)
        [ $# -ge 2 ] || { echo "Error: $1 needs a value." >&2; return 1; }
        FETCH_ARGS+=("$1" "$2")
        shift 2
        ;;
      --force)
        FETCH_ARGS+=("$1")
        shift
        ;;
      *)
        echo "Error: unknown fetch option '$1'." >&2
        return 1
        ;;
    esac
  done
}

fetch_sdk() {
  case "${1:-}" in
    --help|-h)
      cat <<'EOF'
Usage: package.sh fetch-sdk [--rid RID] [--rbfx-tag TAG] [--output DIR] [--force]

Fetches one rbfx prebuilt SDK with eng/fetch-rbfx-sdk.sh into build/sdk/<rid>/
and prints the SDK root. If --rid is omitted, the host RID is inferred.
--output sets the SDK directory. See eng/fetch-rbfx-sdk.sh --help.
EOF
      return 0
      ;;
  esac

  parse_fetch_options "$@"
  if [ -z "$FETCH_RID" ]; then
    FETCH_RID="$(default_rid)"
  fi
  run_fetch_sdk --rid "$FETCH_RID" "${FETCH_ARGS[@]}" --print-root
}

fetch_sdks() {
  case "${1:-}" in
    --help|-h)
      cat <<'EOF'
Usage: package.sh fetch-sdks [--rbfx-tag TAG] [--output DIR] [--force]

Fetches all known rbfx prebuilt SDKs with eng/fetch-rbfx-sdk.sh --all.
EOF
      return 0
      ;;
  esac

  parse_fetch_options "$@"
  if [ -n "$FETCH_RID" ]; then
    echo "Error: fetch-sdks does not accept --rid." >&2
    return 1
  fi
  run_fetch_sdk --all "${FETCH_ARGS[@]}"
}

require_file() {
  local path="$1"
  if [ ! -f "$path" ]; then
    echo "Error: required file is missing: $path" >&2
    return 1
  fi
}

copy_optional_related_files() {
  local directory="$1"
  local stem="$2"
  local destination="$3"
  local extension

  for extension in dll pdb xml; do
    if [ -f "$directory/$stem.$extension" ]; then
      cp -a "$directory/$stem.$extension" "$destination/"
    fi
  done
}

copy_native_glob() {
  local pattern="$1"
  local destination="$2"
  local matched=0
  local file

  shopt -s nullglob
  for file in $pattern; do
    cp -a "$file" "$destination/"
    matched=1
  done
  shopt -u nullglob

  return "$matched"
}

# The native build copies the rbfx runtime dependencies next to SharpCpp.Rbfx.Native.
copy_built_runtime_dependencies() {
  local pattern="$1"
  local destination="$2"
  local file

  shopt -s nullglob
  for file in $pattern; do
    case "$(basename "$file")" in
      *SharpCpp.Rbfx.Native.*|*Example.UserCode.Native.*) ;;
      *) cp -a "$file" "$destination/" ;;
    esac
  done
  shopt -u nullglob
}

detect_target_framework() {
  local package_project="$SCRIPT_DIR/packages/SharpCpp.Rbfx.Bindings/SharpCpp.Rbfx.Bindings.Package.csproj"
  local target_framework

  target_framework="$($DOTNET msbuild "$package_project" -getProperty:TargetFramework -nologo)"
  target_framework="${target_framework//$'\r'/}"
  target_framework="${target_framework//$'\n'/}"

  if [ -z "$target_framework" ]; then
    echo "Error: unable to detect package TargetFramework from $package_project" >&2
    return 1
  fi

  echo "$target_framework"
}

# The user-code generator of a consumer reads the engine binding metadata, so the runtime package carries it
# with the umbrella header that the metadata names.
# The native build tree of the package build holds it. The metadata depends on the headers of the RID.
stage_engine_metadata() {
  local rid="$1"
  local build_root="$2"
  local metadata_files=()
  local metadata_file
  for metadata_file in "$build_root"/native/*/generated/rbfx/SharpCpp.Rbfx.Bindings.json; do
    [ -f "$metadata_file" ] && metadata_files+=("$metadata_file")
  done
  if [ "${#metadata_files[@]}" -ne 1 ]; then
    echo "Error: expected one engine binding metadata file below $build_root/native/*/generated/rbfx, found ${#metadata_files[@]}." >&2
    return 1
  fi
  mkdir -p "$STAGE_DIR/runtime/$rid/metadata"
  cp -a "${metadata_files[0]}" "$STAGE_DIR/runtime/$rid/metadata/SharpCpp.Rbfx.Bindings.json"
  # The binding wrapper of user code includes the umbrella header that the metadata names.
  require_file "$(dirname "${metadata_files[0]}")/SharpCpp.Rbfx.ParseUmbrella.hpp"
  cp -a "$(dirname "${metadata_files[0]}")/SharpCpp.Rbfx.ParseUmbrella.hpp" "$STAGE_DIR/runtime/$rid/metadata/"
}

stage_packages() {
  local rid="$1"
  local configuration="$2"
  local sdk_root="$3"
  local target_framework="$4"
  local build_root="$5"
  local bin_dir="$build_root/bin/$configuration"

  if [ ! -d "$bin_dir" ]; then
    echo "Error: SharpCpp output directory is missing: $bin_dir" >&2
    return 1
  fi

  rm -rf "$STAGE_DIR"
  mkdir -p \
    "$STAGE_DIR/managed/ref/$target_framework" \
    "$STAGE_DIR/managed/lib/$target_framework" \
    "$STAGE_DIR/managed/analyzers/dotnet/cs" \
    "$STAGE_DIR/runtime/$rid/native" \
    "$STAGE_DIR/data"

  require_file "$bin_dir/SharpCpp.Rbfx.Bindings.dll"
  require_file "$bin_dir/SharpCpp.Support.dll"
  require_file "$bin_dir/SharpCpp.Std.dll"
  require_file "$bin_dir/SharpCpp.Ea.dll"
  require_file "$bin_dir/SharpCpp.Rbfx.SourceGenerator.dll"
  require_file "$bin_dir/SharpCpp.Support.SourceGenerator.dll"

  copy_optional_related_files "$bin_dir" "SharpCpp.Rbfx.Bindings" "$STAGE_DIR/managed/lib/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Support" "$STAGE_DIR/managed/lib/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Std" "$STAGE_DIR/managed/lib/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Ea" "$STAGE_DIR/managed/lib/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Rbfx.Bindings" "$STAGE_DIR/managed/ref/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Support" "$STAGE_DIR/managed/ref/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Std" "$STAGE_DIR/managed/ref/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Ea" "$STAGE_DIR/managed/ref/$target_framework"
  copy_optional_related_files "$bin_dir" "SharpCpp.Rbfx.SourceGenerator" "$STAGE_DIR/managed/analyzers/dotnet/cs"
  copy_optional_related_files "$bin_dir" "SharpCpp.Support.SourceGenerator" "$STAGE_DIR/managed/analyzers/dotnet/cs"

  case "$rid" in
    android-*)
      require_file "$bin_dir/libSharpCpp.Rbfx.Native.so"
      cp -a "$bin_dir/libSharpCpp.Rbfx.Native.so" "$STAGE_DIR/runtime/$rid/native/"
      copy_native_glob "$sdk_root/bin/$configuration/*.so" "$STAGE_DIR/runtime/$rid/native" || true
      if [ ! -f "$STAGE_DIR/runtime/$rid/native/libUrho3D.so" ]; then
        copy_native_glob "$sdk_root/bin/RelWithDebInfo/*.so" "$STAGE_DIR/runtime/$rid/native" || true
      fi
      rm -f "$STAGE_DIR/runtime/$rid/native/libPlayer.so" "$STAGE_DIR/runtime/$rid/native/"libSample.*.so
      require_file "$STAGE_DIR/runtime/$rid/native/libUrho3D.so"

      local ndk_root="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
      local ndk_triple libcxx_candidate libcxx_source=""
      if [ -z "$ndk_root" ]; then
        echo "Error: ANDROID_NDK_HOME or ANDROID_NDK_ROOT is required to package Android's libc++_shared.so." >&2
        return 1
      fi
      case "$rid" in
        android-arm) ndk_triple="arm-linux-androideabi" ;;
        android-arm64) ndk_triple="aarch64-linux-android" ;;
        android-x64) ndk_triple="x86_64-linux-android" ;;
      esac
      for libcxx_candidate in "$ndk_root"/toolchains/llvm/prebuilt/*/sysroot/usr/lib/"$ndk_triple"/libc++_shared.so; do
        if [ -f "$libcxx_candidate" ]; then
          libcxx_source="$libcxx_candidate"
          break
        fi
      done
      if [ -z "$libcxx_source" ]; then
        echo "Error: could not find NDK libc++_shared.so for $rid under $ndk_root." >&2
        return 1
      fi
      cp -a "$libcxx_source" "$STAGE_DIR/runtime/$rid/native/"
      ;;
    ios-arm64|browser-wasm)
      require_file "$bin_dir/libSharpCpp.Rbfx.Native.a"
      cp -a "$bin_dir/libSharpCpp.Rbfx.Native.a" "$STAGE_DIR/runtime/$rid/native/"
      local library_dir="$sdk_root/lib/$configuration"
      if [ ! -d "$library_dir" ]; then library_dir="$sdk_root/lib/RelWithDebInfo"; fi
      if [ ! -d "$library_dir" ]; then library_dir="$sdk_root/lib/Release"; fi
      local library
      shopt -s nullglob
      for library in "$library_dir"/*.a; do
        case "$(basename "$library")" in
          libPlayerLibrary.a|libSample.*) ;;
          *) cp -a "$library" "$STAGE_DIR/runtime/$rid/native/" ;;
        esac
      done
      shopt -u nullglob
      require_file "$STAGE_DIR/runtime/$rid/native/libUrho3D.a"
      if [ "$rid" = "browser-wasm" ]; then
        copy_native_glob "$sdk_root/include/libdatachannel-wasm/wasm/js/*.js" "$STAGE_DIR/runtime/$rid/native" || true
      fi
      ;;
    linux-*)
      require_file "$bin_dir/libSharpCpp.Rbfx.Native.so"
      cp -a "$bin_dir/libSharpCpp.Rbfx.Native.so" "$STAGE_DIR/runtime/$rid/native/"
      copy_native_glob "$sdk_root/bin/*.so" "$STAGE_DIR/runtime/$rid/native" || true
      copy_native_glob "$sdk_root/bin/*.so.*" "$STAGE_DIR/runtime/$rid/native" || true
      copy_built_runtime_dependencies "$bin_dir/*.so" "$STAGE_DIR/runtime/$rid/native"
      copy_built_runtime_dependencies "$bin_dir/*.so.*" "$STAGE_DIR/runtime/$rid/native"
      require_file "$STAGE_DIR/runtime/$rid/native/libUrho3D.so"
      # The copy above also stages libSharpCpp.Rbfx.Host.so and the shared libnethost.so that
      # the host build copies next to it.
      ;;
    osx-*)
      require_file "$bin_dir/libSharpCpp.Rbfx.Native.dylib"
      cp -a "$bin_dir/libSharpCpp.Rbfx.Native.dylib" "$STAGE_DIR/runtime/$rid/native/"
      copy_native_glob "$sdk_root/bin/*.dylib" "$STAGE_DIR/runtime/$rid/native" || true
      copy_built_runtime_dependencies "$bin_dir/*.dylib" "$STAGE_DIR/runtime/$rid/native"
      # The copy above also stages libSharpCpp.Rbfx.Host.dylib and the shared libnethost.dylib that
      # the host build copies next to it.
      ;;
    win-*|uap-*)
      require_file "$bin_dir/SharpCpp.Rbfx.Native.dll"
      cp -a "$bin_dir/SharpCpp.Rbfx.Native.dll" "$STAGE_DIR/runtime/$rid/native/"
      # The output directory also contains managed assemblies, so the native libraries of the
      # SDK are copied by name. The SDK keeps them in a directory for each configuration.
      local native_name native_directory
      for native_name in Urho3D.dll D3DCOMPILER_47.dll; do
        for native_directory in "$bin_dir" "$sdk_root/bin/RelWithDebInfo" "$sdk_root/bin/Release" "$sdk_root/bin"; do
          if [ -f "$native_directory/$native_name" ]; then
            cp -a "$native_directory/$native_name" "$STAGE_DIR/runtime/$rid/native/"
            break
          fi
        done
      done
      require_file "$STAGE_DIR/runtime/$rid/native/Urho3D.dll"
      # The host library links nethost.dll on MSVC. The host build copies it next to the host
      # library. UWP does not build the host library.
      if [ -f "$bin_dir/SharpCpp.Rbfx.Host.dll" ]; then
        cp -a "$bin_dir/SharpCpp.Rbfx.Host.dll" "$STAGE_DIR/runtime/$rid/native/"
        require_file "$bin_dir/nethost.dll"
        cp -a "$bin_dir/nethost.dll" "$STAGE_DIR/runtime/$rid/native/"
      fi
      ;;
    *)
      echo "Error: unsupported runtime identifier '$rid'." >&2
      return 1
      ;;
  esac

  if ! compgen -G "$STAGE_DIR/runtime/$rid/native/*" >/dev/null; then
    echo "Error: no native runtime files were staged for '$rid' from '$bin_dir'." >&2
    return 1
  fi

  stage_engine_metadata "$rid" "$build_root"

  local data_name
  for data_name in CoreData Data EditorData; do
    if [ -d "$sdk_root/bin/$data_name" ]; then
      mkdir -p "$STAGE_DIR/data/$data_name"
      cp -a "$sdk_root/bin/$data_name/." "$STAGE_DIR/data/$data_name/"
    fi
  done
}

# The editor is a desktop program. The SDK needs the editor headers (the same check as eng/fetch-rbfx-sdk.sh).
editor_supported() {
  local rid="$1"
  local sdk_root="$2"
  case "$rid" in
    linux-*|win-*|osx-*) [ -f "$sdk_root/include/Tools/Editor/EditorApplication.h" ] ;;
    *) return 1 ;;
  esac
}

# Finds the native build tree of the package build (the tree that stage_engine_metadata reads).
native_build_dir() {
  local build_root="$1"
  local cache_files=("$build_root"/native/*/CMakeCache.txt)
  if [ "${#cache_files[@]}" -ne 1 ] || [ ! -f "${cache_files[0]}" ]; then
    echo "Error: expected one native build tree below $build_root/native." >&2
    return 1
  fi
  dirname "${cache_files[0]}"
}

# True when a build made the editor executable. CMake makes the folder SharpCppRbfxEditor.bin, with a folder for each
# configuration, at configure time, so the check tests the executable file.
editor_built() {
  local configuration="$1"
  local build_root="$2"
  local native_dir editor_dir
  native_dir="$(native_build_dir "$build_root" 2>/dev/null)" || return 1
  for editor_dir in "$native_dir/SharpCppRbfxEditor.bin/$configuration" "$native_dir/SharpCppRbfxEditor.bin"; do
    if [ -f "$editor_dir/SharpCppRbfxEditor" ] || [ -f "$editor_dir/SharpCppRbfxEditor.exe" ]; then
      return 0
    fi
  done
  return 1
}

# The native tree of the package build is a tree that MSBuild drives, so the editor target SharpCppRbfxEditor is in it and
# its managed plugin target does nothing. This function builds the editor, and then builds the plugin project into the
# folder of the editor. The folder is <native tree>/SharpCppRbfxEditor.bin[/Configuration].
# The arguments after the second are the MSBuild arguments of the binding build.
build_editor() {
  local configuration="$1"
  local build_root="$2"
  shift 2
  local native_dir editor_dir
  native_dir="$(native_build_dir "$build_root")"
  cmake --build "$native_dir" --config "$configuration" --target SharpCppRbfxEditor
  editor_dir="$native_dir/SharpCppRbfxEditor.bin"
  # A multi-config generator adds the folder of the configuration.
  if [ -d "$editor_dir/$configuration" ]; then
    editor_dir="$editor_dir/$configuration"
  fi
  "$DOTNET" build "$REPO_ROOT/SharpCpp.Rbfx.EditorHost/SharpCpp.Rbfx.EditorHost.csproj" \
    --configuration "$configuration" \
    --nologo \
    "$@" \
    -p:RbfxNativeBuildDriver=cmake \
    -p:RbfxNativeBuildDir="$native_dir" \
    -p:OutDir="$editor_dir/"
}

# Copies the editor folder and the native runtime files that its executable loads into the stage folder of the editor package.
# The executable finds its libraries in its own folder. The build tree executable has paths of the build machine in its
# run path, so the run path of the staged executable is set again.
stage_editor() {
  local rid="$1"
  local configuration="$2"
  local build_root="$3"
  local native_dir editor_dir editor_stage="$STAGE_DIR/editor/$rid"
  native_dir="$(native_build_dir "$build_root")"
  editor_dir="$native_dir/SharpCppRbfxEditor.bin"
  if [ -d "$editor_dir/$configuration" ]; then
    editor_dir="$editor_dir/$configuration"
  fi
  case "$rid" in
    win-*) require_file "$editor_dir/SharpCppRbfxEditor.exe" ;;
    *) require_file "$editor_dir/SharpCppRbfxEditor" ;;
  esac
  require_file "$editor_dir/SharpCpp.Rbfx.EditorHost.dll"
  require_file "$editor_dir/SharpCpp.Rbfx.EditorHost.runtimeconfig.json"

  rm -rf "$editor_stage"
  mkdir -p "$editor_stage"
  cp -a "$STAGE_DIR/runtime/$rid/native/." "$editor_stage/"
  # The native engine files are the files of the runtime package. The editor folder of the build also has the files of the
  # generators, so the list names the files that the editor loads: the executable and the closure of the plugin
  # SharpCpp.Rbfx.EditorHost (its deps.json file names the assemblies).
  local editor_file
  for editor_file in SharpCppRbfxEditor SharpCppRbfxEditor.exe \
      SharpCpp.Rbfx.EditorHost.dll SharpCpp.Rbfx.EditorHost.deps.json SharpCpp.Rbfx.EditorHost.runtimeconfig.json \
      SharpCpp.Rbfx.Bindings.dll SharpCpp.Ea.dll SharpCpp.Std.dll SharpCpp.Support.dll; do
    if [ -f "$editor_dir/$editor_file" ]; then
      cp -a "$editor_dir/$editor_file" "$editor_stage/$editor_file"
    fi
  done
  require_file "$editor_stage/SharpCpp.Rbfx.Bindings.dll"

  # The editor sets the resource paths CoreData and EditorData and looks for them in the folder of the executable.
  # The folders are the same as in the packages CoreData and EditorData. The folder Data is for the applications only.
  local data_name
  for data_name in CoreData EditorData; do
    if [ ! -d "$STAGE_DIR/data/$data_name" ]; then
      echo "Error: the SDK has no $data_name folder, so the editor cannot find its resources." >&2
      return 1
    fi
    cp -a "$STAGE_DIR/data/$data_name" "$editor_stage/$data_name"
  done

  case "$rid" in
    linux-*)
      cmake -DEDITOR_FILE="$editor_stage/SharpCppRbfxEditor" -DEDITOR_RPATH='$ORIGIN' -P "$SCRIPT_DIR/SetEditorRpath.cmake"
      ;;
    osx-*)
      local rpath
      while IFS= read -r rpath; do
        install_name_tool -delete_rpath "$rpath" "$editor_stage/SharpCppRbfxEditor"
      done < <(otool -l "$editor_stage/SharpCppRbfxEditor" | awk '$1 == "cmd" && $2 == "LC_RPATH" { found = 1 } found && $1 == "path" { print $2; found = 0 }')
      install_name_tool -add_rpath "@loader_path" "$editor_stage/SharpCppRbfxEditor"
      # Changing the load commands invalidates the signature that the linker made.
      codesign --force --sign - "$editor_stage/SharpCppRbfxEditor"
      ;;
  esac
}

pack_project() {
  local project="$1"
  local version="$2"
  local rid="$3"
  local target_framework="$4"

  "$DOTNET" pack "$project" \
    --configuration Release \
    --nologo \
    --verbosity minimal \
    --output "$OUT_DIR" \
    --source "$OUT_DIR" \
    -p:PackageVersion="$version" \
    -p:SharpCppNuGetStageRoot="$STAGE_DIR" \
    -p:SharpCppRuntimeIdentifier="$rid" \
    -p:RbfxRestoreRid="$rid" \
    -p:SharpCppIntermediateRoot="$PACKAGE_ROOT/$rid/obj" \
    -p:SharpCppManagedOutputRoot="$PACKAGE_ROOT/$rid/bin" \
    -p:SharpCppPackageTargetFramework="$target_framework"
}

create_packages() {
  local version=""
  local rid=""
  local configuration="Release"
  local configure_preset=""
  local sdk_root=""
  local rbfx_tag=""
  local target_framework=""
  local skip_fetch=0
  local skip_build=0
  local build_root=""
  local cmake_arguments=()

  while [ $# -gt 0 ]; do
    case "$1" in
      --version|-v)
        version="$2"
        shift 2
        ;;
      --rid)
        rid="$2"
        shift 2
        ;;
      --configuration|-c)
        configuration="$2"
        shift 2
        ;;
      --configure-preset|--preset)
        configure_preset="$2"
        shift 2
        ;;
      --sdk-root)
        sdk_root="$2"
        shift 2
        ;;
      --rbfx-tag)
        rbfx_tag="$2"
        shift 2
        ;;
      --target-framework)
        target_framework="$2"
        shift 2
        ;;
      --skip-fetch)
        skip_fetch=1
        shift
        ;;
      --skip-build)
        skip_build=1
        shift
        ;;
      --build-root)
        build_root="$2"
        shift 2
        ;;
      --cmake-arg)
        cmake_arguments+=("$2")
        shift 2
        ;;
      --help|-h)
        cat <<'EOF'
Usage: package.sh create --version VERSION [options]

Options:
  --version, -v VERSION      Package version. Suffix -alpha or -dev appends the git SHA.
  --rid RID                 Runtime package RID. Defaults to host RID.
  --configuration, -c NAME   Build configuration. Defaults to Release.
  --configure-preset NAME    CMake configure preset (alias --preset). Defaults to the preset of the RID (eng/SharpCpp.Rbfx.Build.props).
  --sdk-root PATH            Existing extracted rbfx SDK root. Overrides the fetch.
  --rbfx-tag TAG             Release tag of rbfx/rbfx for the SDK. Defaults to latest.
  --target-framework TFM     Override package target framework. Defaults to repo TargetFramework.
  --skip-fetch               Do not fetch the SDK (use build/sdk/RID or --sdk-root).
  --skip-build               Do not build package inputs.
  --build-root PATH          Build directory containing bin/CONFIGURATION.
  --cmake-arg ARG            Extra CMake configure argument (repeatable).
EOF
        return 0
        ;;
      *)
        echo "Error: unknown create option '$1'." >&2
        return 1
        ;;
    esac
  done

  if [ -z "$version" ]; then
    echo "Error: --version is required." >&2
    return 1
  fi

  case "$version" in
    *-alpha|*-dev)
      version="$version-$(git -C "$REPO_ROOT" rev-parse --short HEAD)"
      ;;
  esac

  if [ -z "$rid" ]; then
    rid="$(default_rid)"
  fi

  # Each RID builds in its own tree below the package folder. Builds of different RIDs run in parallel, and
  # the CMake cache of a package build never mixes with a build of the examples.
  : "${build_root:=$PACKAGE_ROOT/$rid/build}"

  STAGE_DIR="$PACKAGE_ROOT/$rid/stage"
  OUT_DIR="$PACKAGE_ROOT/$rid/out"

  if [ -z "$sdk_root" ]; then
    if [ "$skip_fetch" -eq 0 ]; then
      local fetch_args=(--rid "$rid")
      if [ -n "$rbfx_tag" ]; then
        fetch_args+=(--rbfx-tag "$rbfx_tag")
      fi
      sdk_root="$(run_fetch_sdk "${fetch_args[@]}" --print-root)"
    else
      sdk_root="$SDK_OUTPUT_DIR/$rid"
    fi
  fi

  if [ ! -d "$sdk_root" ]; then
    echo "Error: SDK root does not exist: $sdk_root" >&2
    return 1
  fi

  if [ "$skip_build" -eq 0 ]; then
    # A package ships to other machines, so the native libraries must not carry the dotnet root or the directories of this machine.
    local native_configure_arguments=" -DRBFX_HOST_DEFAULT_DOTNET_ROOT=OFF -DRBFX_REMAP_SOURCE_PATHS=ON" encoded_argument argument
    for argument in "${cmake_arguments[@]}"; do
      printf -v encoded_argument '%q' "$argument"
      native_configure_arguments+=" $encoded_argument"
    done
    # The toolchain file of iOS is part of the engine SDK. A global property cannot take it from the props file.
    if [ "$rid" = "ios-arm64" ]; then
      printf -v encoded_argument '%q' "-DCMAKE_TOOLCHAIN_FILE=$sdk_root/share/Urho3D/CMake/Toolchains/IOS.cmake"
      native_configure_arguments+=" $encoded_argument"
    fi
    local preset_arguments=()
    if [ -n "$configure_preset" ]; then
      preset_arguments=(-p:RbfxNativePreset="$configure_preset")
    fi
    # The props file maps the paths of this machine to fixed names. It applies to the projects of the SharpCpp checkout too.
    local deterministic_props="$REPO_ROOT/eng/SharpCpp.Rbfx.Deterministic.props"
    if command -v cygpath >/dev/null 2>&1; then
      deterministic_props="$(cygpath -m "$deterministic_props")"
    fi
    local binding_build_arguments=(
      ${preset_arguments[@]+"${preset_arguments[@]}"}
      -p:RbfxPackaging=true
      -p:CustomAfterMicrosoftCommonProps="$deterministic_props"
      -p:RbfxTargetRid="$rid"
      -p:RbfxRestoreRid="$rid"
      -p:SharpCppBuildRoot="$build_root"
      -p:RbfxNativeConfigureArguments="$native_configure_arguments"
      -p:RbfxSdkRoot="$sdk_root")
    "$DOTNET" build "$REPO_ROOT/SharpCpp.Rbfx.Bindings/SharpCpp.Rbfx.Bindings.csproj" \
      --configuration "$configuration" \
      --nologo \
      "${binding_build_arguments[@]}"
    # The editor builds in the native tree of the binding build, after that build.
    if editor_supported "$rid" "$sdk_root"; then
      build_editor "$configuration" "$build_root" "${binding_build_arguments[@]}"
    fi
  fi

  if [ -z "$target_framework" ]; then
    target_framework="$(detect_target_framework)"
  fi

  echo "Staging packages for $version ($rid, $configuration, $target_framework)"
  stage_packages "$rid" "$configuration" "$sdk_root" "$target_framework" "$build_root"
  "$DOTNET" run --file "$SCRIPT_DIR/CrossPackage.cs" -- targets --rid "$rid" --stage "$STAGE_DIR"

  # --skip-build packs the editor only when an earlier build made it.
  local pack_editor=0
  if editor_supported "$rid" "$sdk_root"; then
    if [ "$skip_build" -eq 0 ] || editor_built "$configuration" "$build_root"; then
      stage_editor "$rid" "$configuration" "$build_root"
      pack_editor=1
    else
      echo "Note: no editor executable below $build_root/native for $configuration. The editor packages are skipped." >&2
    fi
  fi

  rm -rf "$OUT_DIR"
  mkdir -p "$OUT_DIR"

  pack_project "$SCRIPT_DIR/packages/SharpCpp.Rbfx.Bindings/SharpCpp.Rbfx.Bindings.Package.csproj" "$version" "$rid" "$target_framework"
  pack_project "$SCRIPT_DIR/packages/SharpCpp.Rbfx.Bindings.Runtime/SharpCpp.Rbfx.Bindings.Runtime.Package.csproj" "$version" "$rid" "$target_framework"

  if [ -d "$STAGE_DIR/data/CoreData" ]; then
    pack_project "$SCRIPT_DIR/packages/SharpCpp.Rbfx.CoreData/SharpCpp.Rbfx.CoreData.Package.csproj" "$version" "$rid" "$target_framework"
  fi
  if [ -d "$STAGE_DIR/data/Data" ]; then
    pack_project "$SCRIPT_DIR/packages/SharpCpp.Rbfx.Data/SharpCpp.Rbfx.Data.Package.csproj" "$version" "$rid" "$target_framework"
  fi
  if [ -d "$STAGE_DIR/data/EditorData" ]; then
    pack_project "$SCRIPT_DIR/packages/SharpCpp.Rbfx.EditorData/SharpCpp.Rbfx.EditorData.Package.csproj" "$version" "$rid" "$target_framework"
  fi

  if [ "$pack_editor" -eq 1 ]; then
    pack_project "$SCRIPT_DIR/packages/SharpCpp.Rbfx.Editor.Runtime/SharpCpp.Rbfx.Editor.Runtime.Package.csproj" "$version" "$rid" "$target_framework"
    pack_project "$SCRIPT_DIR/packages/SharpCpp.Rbfx.Editor/SharpCpp.Rbfx.Editor.Package.csproj" "$version" "$rid" "$target_framework"
  fi

  echo "Packages created in $OUT_DIR"
}

create_local_repository() {
  local rid=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --rid) rid="$2"; shift 2 ;;
      *) echo "Error: unknown local-repo option: $1" >&2; return 1 ;;
    esac
  done
  if [ -z "$rid" ]; then
    rid="$(default_rid)"
  fi

  # Managed and data packages come from one RID. Runtime packages come from every RID.
  local packages=() package
  shopt -s nullglob
  for package in "$PACKAGE_ROOT/$rid/out"/*.nupkg; do
    case "$package" in *.runtime.*) ;; *) packages+=("$package") ;; esac
  done
  packages+=("$PACKAGE_ROOT"/*/out/*.runtime.*.nupkg)
  shopt -u nullglob
  if [ ${#packages[@]} -eq 0 ] || [ ! -d "$PACKAGE_ROOT/$rid/out" ]; then
    echo "Error: no packages in $PACKAGE_ROOT/$rid/out. Run 'create --rid $rid' first." >&2
    return 1
  fi

  rm -rf "$LOCAL_REPOSITORY_DIR"
  mkdir -p "$LOCAL_REPOSITORY_DIR"
  for package in "${packages[@]}"; do
    "$NUGET" add "$package" -source "$LOCAL_REPOSITORY_DIR"
  done
}

if [ $# -eq 0 ]; then
  usage
  exit 1
fi

action="$1"
shift

case "$action" in
  fetch-sdk)
    fetch_sdk "$@"
    ;;
  fetch-sdks)
    fetch_sdks "$@"
    ;;
  create)
    create_packages "$@"
    ;;
  local-repo|local-repository)
    create_local_repository "$@"
    ;;
  --help|-h|help)
    usage
    ;;
  *)
    echo "Error: unknown action '$action'." >&2
    usage
    exit 1
    ;;
esac
