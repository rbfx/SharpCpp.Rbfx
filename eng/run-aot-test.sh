#!/usr/bin/env bash
# Builds and runs the NativeAOT desktop head test (SharpCpp.Rbfx.Tests.Aot) in shim mode.
# The native launcher starts the engine headless and loads the head. The C# plugin in the head logs
# "AotApp: update 1..3" and stops the engine. The test passes when the exit code is 0 and the log has "AotApp: update 3".

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
FIXTURE_DIR="$REPO_ROOT/SharpCpp.Rbfx.Tests.Aot"

RID=""
CONFIG="Debug"
SDK_DIR=""
BUILD_DIR=""
TIMEOUT=120

usage() {
  cat <<USAGE
Usage: $(basename "$0") --rid <rid> [--config <Debug|Release>] [--sdk <rbfx sdk dir>] [--build-dir <dir>] [--timeout <seconds>]
  --rid        linux-x64, osx-arm64 or win-x64. It selects the configure preset.
  --config     The build configuration (default Debug).
  --sdk        The installed rbfx SDK (default build/sdk/<rid>).
  --build-dir  The build folder (default build/aot-test/<rid>).
  --timeout    The time limit of the run in seconds (default 120).
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) RID="$2"; shift 2 ;;
    --config) CONFIG="$2"; shift 2 ;;
    --sdk) SDK_DIR="$2"; shift 2 ;;
    --build-dir) BUILD_DIR="$2"; shift 2 ;;
    --timeout) TIMEOUT="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ -z "$RID" ]]; then
  usage >&2
  exit 2
fi

# The RID to configure preset map of eng/SharpCpp.Rbfx.Build.props (desktop RIDs only).
case "$RID" in
  linux-x64) PRESET="linux-gcc-x64-dll"; EXE_SUFFIX="" ;;
  osx-arm64) PRESET="macos-clang-arm64-dll"; EXE_SUFFIX="" ;;
  win-x64) PRESET="windows-msvc-x64-dll"; EXE_SUFFIX=".exe" ;;
  *) echo "The RID '$RID' is not a desktop RID with a preset." >&2; exit 2 ;;
esac

SDK_DIR="${SDK_DIR:-$REPO_ROOT/build/sdk/$RID}"
BUILD_DIR="${BUILD_DIR:-$REPO_ROOT/build/aot-test/$RID}"
if [[ ! -f "$SDK_DIR/share/Urho3D/CMake/Urho3DConfig.cmake" ]]; then
  echo "No rbfx SDK in '$SDK_DIR'. Run eng/fetch-rbfx-sdk.sh --rid $RID, or pass --sdk." >&2
  exit 1
fi

echo "== Configure ($PRESET, shim mode) in $BUILD_DIR"
# The fixture presets include the presets of the repository. --preset reads them from the current folder.
(
  cd "$FIXTURE_DIR"
  cmake --preset "$PRESET" -B "$BUILD_DIR" \
    "-DCMAKE_PREFIX_PATH=$SDK_DIR;$REPO_ROOT/cmake/SharpCppRbfx" \
    -DRBFX_HOST_MODE=shim \
    -DRBFX_BUILD_HOST=ON
)

echo "== Build ($CONFIG)"
cmake --build "$BUILD_DIR" --config "$CONFIG" --target AotTest.CSharp

EXE="$BUILD_DIR/bin/$CONFIG/AotTest$EXE_SUFFIX"
LOG="$BUILD_DIR/aot-test.log"
echo "== Run $EXE --headless (timeout ${TIMEOUT}s)"
set +e
# The engine can ignore SIGTERM, so SIGKILL follows after 10 seconds.
timeout --kill-after=10 "$TIMEOUT" "$EXE" --headless >"$LOG" 2>&1
STATUS=$?
set -e
tail -n 20 "$LOG"

if [[ $STATUS -ne 0 ]]; then
  echo "FAIL: the exit code is $STATUS (124 or 137 is the timeout)." >&2
  exit 1
fi
if ! grep -q "AotApp: update 3" "$LOG"; then
  echo "FAIL: the log has no 'AotApp: update 3'." >&2
  exit 1
fi
echo "PASS: the NativeAOT head ran three updates and the engine stopped with exit code 0."
