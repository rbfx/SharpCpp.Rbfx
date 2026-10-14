#!/usr/bin/env bash
# Packs the installed CMake SDK of SharpCpp.Rbfx into a 7z archive.
#
# Usage: pack-sdk.sh --build-dir <tree> --config <Config> --tag <platform tag> --out <dir>
#
# The archive is <out>/sharpcpp-rbfx-sdk-<tag>-latest.7z.
# It has one root folder: sharpcpp-rbfx-sdk-<tag>-latest.
# Example tag: linux-gcc-x64-dll.
#
# Requirements:
# - The build tree is configured with -DRBFX_HOST_DEFAULT_DOTNET_ROOT=OFF.
#   Then the host library has no path of the build machine.
# - The build tree is built for <Config> (native build and `dotnet build SharpCpp.Rbfx.slnx`).
#   The install step does not build.
# - 7z is in PATH. Git Bash on Windows runners works.
set -euo pipefail

usage() {
  echo "Usage: $0 --build-dir <tree> --config <Config> --tag <platform tag> --out <dir>" >&2
  exit 2
}

build_dir="" config="" tag="" out=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --build-dir) [[ $# -ge 2 ]] || usage; build_dir="$2"; shift 2 ;;
    --config) [[ $# -ge 2 ]] || usage; config="$2"; shift 2 ;;
    --tag) [[ $# -ge 2 ]] || usage; tag="$2"; shift 2 ;;
    --out) [[ $# -ge 2 ]] || usage; out="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; usage ;;
  esac
done
[[ -n "$build_dir" && -n "$config" && -n "$tag" && -n "$out" ]] || usage

[[ -d "$build_dir" ]] || { echo "Error: build directory not found: $build_dir" >&2; exit 1; }

if command -v 7z >/dev/null 2>&1; then
  sevenzip=7z
elif command -v 7zz >/dev/null 2>&1; then
  sevenzip=7zz
else
  echo "Error: 7z is not installed or not in PATH. Install 7-Zip and try again." >&2
  exit 1
fi

name="sharpcpp-rbfx-sdk-${tag}-latest"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
prefix="$out/$name"
archive="$out/$name.7z"

rm -rf "$prefix"
rm -f "$archive"

cmake --install "$build_dir" --prefix "$prefix" --config "$config"

(
  cd "$out"
  "$sevenzip" a -t7z -m0=lzma2 -mx=9 -mfb=64 -md=32m -ms=on "$archive" "$name"
)

echo "SDK archive: $archive"
