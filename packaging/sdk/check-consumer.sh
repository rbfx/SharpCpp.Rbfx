#!/usr/bin/env bash
# Builds packaging/sdk-consumer against a packed or extracted SharpCpp.Rbfx SDK.
#
# Usage: check-consumer.sh (--sdk-archive <7z> | --sdk-dir <dir>) --rbfx-sdk <rbfx install root>
#                          --config <Config> --work <dir>
#
# The script extracts the archive into <work>/sdk, configures and builds the consumer in
# <work>/build, and checks that the generated wrapper source exists.
# It does not use the source tree of the SDK build.
# The --sdk-dir folder is the SDK root (the folder with lib and share).
set -euo pipefail

usage() {
  echo "Usage: $0 (--sdk-archive <7z> | --sdk-dir <dir>) --rbfx-sdk <rbfx install root> --config <Config> --work <dir>" >&2
  exit 2
}

archive="" sdk_dir="" rbfx_sdk="" config="" work=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --sdk-archive) [[ $# -ge 2 ]] || usage; archive="$2"; shift 2 ;;
    --sdk-dir) [[ $# -ge 2 ]] || usage; sdk_dir="$2"; shift 2 ;;
    --rbfx-sdk) [[ $# -ge 2 ]] || usage; rbfx_sdk="$2"; shift 2 ;;
    --config) [[ $# -ge 2 ]] || usage; config="$2"; shift 2 ;;
    --work) [[ $# -ge 2 ]] || usage; work="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; usage ;;
  esac
done
[[ -n "$rbfx_sdk" && -n "$config" && -n "$work" ]] || usage
if [[ -n "$archive" && -n "$sdk_dir" ]] || [[ -z "$archive" && -z "$sdk_dir" ]]; then
  echo "Error: give exactly one of --sdk-archive and --sdk-dir." >&2
  usage
fi
[[ -d "$rbfx_sdk" ]] || { echo "Error: rbfx SDK not found: $rbfx_sdk" >&2; exit 1; }

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
consumer_src="$(cd "$script_dir/../sdk-consumer" && pwd)"

mkdir -p "$work"
work="$(cd "$work" && pwd)"
rbfx_sdk="$(cd "$rbfx_sdk" && pwd)"
build_dir="$work/build"

if [[ -n "$archive" ]]; then
  [[ -f "$archive" ]] || { echo "Error: archive not found: $archive" >&2; exit 1; }
  archive="$(cd "$(dirname "$archive")" && pwd)/$(basename "$archive")"
  if command -v 7z >/dev/null 2>&1; then
    sevenzip=7z
  elif command -v 7zz >/dev/null 2>&1; then
    sevenzip=7zz
  else
    echo "Error: 7z is not installed or not in PATH. Install 7-Zip and try again." >&2
    exit 1
  fi
  extract_dir="$work/sdk"
  rm -rf "$extract_dir"
  mkdir -p "$extract_dir"
  (cd "$extract_dir" && "$sevenzip" x -y "$archive" >/dev/null)
  roots=()
  for entry in "$extract_dir"/*/; do
    roots+=("${entry%/}")
  done
  if [[ ${#roots[@]} -ne 1 ]]; then
    echo "Error: the archive must have one root folder, found ${#roots[@]}." >&2
    exit 1
  fi
  sdk_dir="${roots[0]}"
else
  [[ -d "$sdk_dir" ]] || { echo "Error: SDK directory not found: $sdk_dir" >&2; exit 1; }
  sdk_dir="$(cd "$sdk_dir" && pwd)"
fi

# Native cmake on Windows needs mixed paths in a value that holds a list. MSYS does not convert those.
prefix_sdk="$sdk_dir"
prefix_rbfx="$rbfx_sdk"
if command -v cygpath >/dev/null 2>&1; then
  prefix_sdk="$(cygpath -m "$sdk_dir")"
  prefix_rbfx="$(cygpath -m "$rbfx_sdk")"
fi

rm -rf "$build_dir"
cmake -S "$consumer_src" -B "$build_dir" \
  -DCMAKE_BUILD_TYPE="$config" \
  -DBUILD_SHARED_LIBS=ON \
  "-DCMAKE_PREFIX_PATH=$prefix_sdk;$prefix_rbfx"
cmake --build "$build_dir" --config "$config"

wrapper="$build_dir/generated/Greeter/Greeter.Bindings.g.cpp"
if [[ ! -f "$wrapper" ]]; then
  echo "Error: generated wrapper source not found: $wrapper" >&2
  exit 1
fi
echo "OK: found $wrapper"
