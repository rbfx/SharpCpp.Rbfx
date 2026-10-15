#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"

RBFX_REPOSITORY="rbfx/rbfx"
WEB_REPOSITORY="rbfx/SharpCpp.Rbfx"
RBFX_GIT_URL="https://github.com/rbfx/rbfx.git"
API_BASE="https://api.github.com/repos"
CONFIG_RELATIVE="share/Urho3D/CMake/Urho3DConfig.cmake"
WEB_ASSET_REGEX='^rbfx-web-wasm-sdk-emscripten-.*-rbfx-.*\.7z$'
STAMP_NAME=".rbfx-sdk.json"

# RID to "<platform>-<compiler>-<arch>" part of the asset name. Plain case: macOS bash 3.2 has no associative arrays.
sdk_platform() {
  case "$1" in
    linux-x64) echo "linux-gcc-x64" ;;
    osx-arm64) echo "macos-clang-arm64" ;;
    osx-x64) echo "macos-clang-x64" ;;
    win-x64) echo "windows-msvc-x64" ;;
    win-x86) echo "windows-msvc-x86" ;;
    uap-x64) echo "uwp-msvc-x64" ;;
    android-arm) echo "android-clang-arm" ;;
    android-arm64) echo "android-clang-arm64" ;;
    android-x64) echo "android-clang-x64" ;;
    ios-arm64) echo "ios-clang-arm64" ;;
  esac
}
ALL_RIDS=(linux-x64 osx-arm64 osx-x64 win-x64 win-x86 uap-x64 android-arm android-arm64 android-x64 ios-arm64 browser-wasm)

RIDS=()
ALL=0
LINK="auto"
RBFX_TAG="latest"
WEB_TAG="latest"
OUTPUT="$REPO_ROOT/build/sdk"
FORCE=0
PRINT_ROOT=0
PRINT_CACHE_KEY=0

TMP_DIR=""

# Result of resolve_rid.
R_ASSET=""
R_SIZE=""
R_DIGEST=""
R_UPDATED=""
R_URL=""
R_REPO=""
R_TAG=""

usage() {
  cat >&2 <<'USAGE'
Usage: fetch-rbfx-sdk.sh [options]

Downloads and extracts prebuilt rbfx SDK archives into <output>/<rid>.

Options:
  --rid <rid>          RID to fetch. Can be repeated. Default: the host RID.
  --all                Fetch every known RID (about 4 GB).
  --link auto|dll|lib  Library variant. auto = dll, except lib for ios-arm64. Default: auto.
  --rbfx-tag <tag>     Release tag of rbfx/rbfx. Default: latest.
  --web-tag <tag>      Release tag of rbfx/SharpCpp.Rbfx for browser-wasm. Default: latest.
  --output <dir>       Output directory. Default: <repo>/build/sdk.
  --force              Download and extract again, even if the SDK is current.
  --print-root         Print the SDK root of the single RID on stdout.
  --print-cache-key    Print <asset>-<updated_at> for each RID. Downloads nothing.
  --help               Show this help.

RIDs: linux-x64 osx-arm64 osx-x64 win-x64 win-x86 uap-x64 android-arm android-arm64
      android-x64 ios-arm64 browser-wasm

Layout:
  <output>/<rid>/                SDK root
  <output>/<rid>/.rbfx-sdk.json  Stamp file (asset, size, digest, updated_at, tag, repo)
  <output>/.archives/            Archive cache

Environment: GITHUB_TOKEN (optional) is sent as a bearer token to GitHub.
Tools: curl, jq, and one of 7z, 7zz or 7za. All log output goes to stderr.
USAGE
}

log() {
  echo "$*" >&2
}

warn() {
  echo "Warning: $*" >&2
}

die() {
  echo "Error: $*" >&2
  exit 1
}

cleanup() {
  if [ -n "$TMP_DIR" ] && [ -d "$TMP_DIR" ]; then
    rm -rf "$TMP_DIR"
  fi
}
trap cleanup EXIT

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
    *) die "cannot infer runtime identifier for $kernel/$machine. Pass --rid." ;;
  esac
}

is_known_rid() {
  local rid="$1" known
  for known in "${ALL_RIDS[@]}"; do
    if [ "$known" = "$rid" ]; then
      return 0
    fi
  done
  return 1
}

require_tools() {
  command -v curl >/dev/null 2>&1 || die "curl is required."
  command -v jq >/dev/null 2>&1 || die "jq is required."
}

find_7z() {
  local tool
  for tool in 7z 7zz 7za; do
    if command -v "$tool" >/dev/null 2>&1; then
      echo "$tool"
      return 0
    fi
  done
  die "7z, 7zz or 7za is required to extract SDK archives."
}

# curl with the optional GitHub token. The token stays out of the process list.
curl_gh() {
  if [ -n "${GITHUB_TOKEN:-}" ]; then
    printf 'header = "Authorization: Bearer %s"\n' "$GITHUB_TOKEN" | curl --config - "$@"
  else
    curl "$@"
  fi
}

# Name of the variable that caches the release JSON of <repo> <tag>.
release_var() {
  echo "RELEASE_JSON_$(printf %s "$1_$2" | tr -c 'A-Za-z0-9\n' _)"
}

# Fetch the release JSON of <repo> <tag> into a cache variable. Return 1 on a network or API failure.
fetch_release() {
  local repo="$1" tag="$2"
  local var
  var="$(release_var "$repo" "$tag")"
  if [ -n "${!var:-}" ]; then
    return 0
  fi

  local json
  if ! json="$(curl_gh --fail --silent --show-error --location --retry 2 \
      -H 'Accept: application/vnd.github+json' -H 'User-Agent: fetch-rbfx-sdk' \
      "$API_BASE/$repo/releases/tags/$tag" 2>&1)"; then
    log "Could not read release '$tag' of $repo: ${json:0:200}"
    if [ -z "${GITHUB_TOKEN:-}" ]; then
      log "Hint: set GITHUB_TOKEN if the GitHub API rate limit is reached."
    fi
    return 1
  fi
  printf -v "$var" %s "$json"
}

available_rids() {
  local json="$1" rid platform variant asset list=()
  for rid in "${ALL_RIDS[@]}"; do
    if [ "$rid" = "browser-wasm" ]; then
      continue
    fi
    platform="$(sdk_platform "$rid")"
    for variant in dll lib; do
      asset="rebelfork-sdk-$platform-$variant-$RBFX_TAG.7z"
      if jq -e --arg n "$asset" 'any(.assets[]; .name == $n)' <<<"$json" >/dev/null; then
        list+=("$rid($variant)")
      fi
    done
  done
  echo "${list[*]}"
}

# Set R_* for <rid>. Return 1 on a network failure. Exit on an unknown or missing asset.
resolve_rid() {
  local rid="$1"
  local json entry

  if [ "$rid" = "browser-wasm" ]; then
    fetch_release "$WEB_REPOSITORY" "$WEB_TAG" || return 1
    json="$(release_var "$WEB_REPOSITORY" "$WEB_TAG")"; json="${!json}"
    entry="$(jq -c --arg re "$WEB_ASSET_REGEX" \
      '[.assets[] | select(.name | test($re))] | sort_by(.created_at) | last // empty' <<<"$json")"
    if [ -z "$entry" ]; then
      die "no asset matching rbfx-web-wasm-sdk-emscripten-*-rbfx-*.7z in release '$WEB_TAG' of $WEB_REPOSITORY."
    fi
    R_REPO="$WEB_REPOSITORY"
    R_TAG="$WEB_TAG"
  else
    local platform="$(sdk_platform "$rid")" variant asset
    fetch_release "$RBFX_REPOSITORY" "$RBFX_TAG" || return 1
    json="$(release_var "$RBFX_REPOSITORY" "$RBFX_TAG")"; json="${!json}"

    case "$LINK" in
      auto)
        if [ "$rid" = "ios-arm64" ]; then variant="lib"; else variant="dll"; fi
        ;;
      dll|lib) variant="$LINK" ;;
      *) die "--link must be auto, dll or lib." ;;
    esac

    asset="rebelfork-sdk-$platform-$variant-$RBFX_TAG.7z"
    entry="$(jq -c --arg n "$asset" '.assets[] | select(.name == $n)' <<<"$json")"
    if [ -z "$entry" ]; then
      die "asset '$asset' for RID '$rid' is not in release '$RBFX_TAG' of $RBFX_REPOSITORY. Available RIDs: $(available_rids "$json")"
    fi
    R_REPO="$RBFX_REPOSITORY"
    R_TAG="$RBFX_TAG"
  fi

  R_ASSET="$(jq -r '.name' <<<"$entry")"
  R_SIZE="$(jq -r '.size' <<<"$entry")"
  R_DIGEST="$(jq -r '.digest // ""' <<<"$entry")"
  R_UPDATED="$(jq -r '.updated_at' <<<"$entry")"
  R_URL="$(jq -r '.browser_download_url' <<<"$entry")"
}

stamp_field() {
  local stamp="$1" field="$2"
  jq -r --arg f "$field" '.[$f] // ""' "$stamp" 2>/dev/null || true
}

stamp_matches_api() {
  local rid="$1"
  local stamp="$OUTPUT/$rid/$STAMP_NAME"
  [ -f "$stamp" ] && [ -f "$OUTPUT/$rid/$CONFIG_RELATIVE" ] || return 1
  [ "$(stamp_field "$stamp" asset)" = "$R_ASSET" ] &&
    [ "$(stamp_field "$stamp" size)" = "$R_SIZE" ] &&
    [ "$(stamp_field "$stamp" digest)" = "$R_DIGEST" ] &&
    [ "$(stamp_field "$stamp" updated_at)" = "$R_UPDATED" ] &&
    [ "$(stamp_field "$stamp" tag)" = "$R_TAG" ] &&
    [ "$(stamp_field "$stamp" source_repo)" = "$R_REPO" ]
}

stamp_is_usable() {
  local rid="$1"
  local stamp="$OUTPUT/$rid/$STAMP_NAME"
  [ -f "$stamp" ] && [ -f "$OUTPUT/$rid/$CONFIG_RELATIVE" ] && jq -e . "$stamp" >/dev/null 2>&1
}

file_size() {
  stat -c %s "$1" 2>/dev/null || stat -f %z "$1"
}

file_sha256() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | cut -d' ' -f1
  else
    shasum -a 256 "$1" | cut -d' ' -f1
  fi
}

# Check size and digest of an archive. Print the reason on failure.
verify_archive() {
  local archive="$1"
  local size actual
  size="$(file_size "$archive")"
  if [ "$size" != "$R_SIZE" ]; then
    log "Size mismatch for $archive: expected $R_SIZE, got $size."
    return 1
  fi
  case "$R_DIGEST" in
    sha256:*)
      actual="$(file_sha256 "$archive")"
      if [ "$actual" != "${R_DIGEST#sha256:}" ]; then
        log "sha256 mismatch for $archive: expected ${R_DIGEST#sha256:}, got $actual."
        return 1
      fi
      ;;
  esac
  return 0
}

download_archive() {
  local archive="$1"
  local part="$archive.part"
  local attempt size progress=(--silent --show-error)

  if [ -t 2 ]; then
    progress=(--progress-bar)
  fi

  mkdir -p "$(dirname "$archive")"
  for attempt in 1 2 3; do
    if [ -f "$part" ] && [ "$(file_size "$part")" -gt "$R_SIZE" ]; then
      rm -f "$part"
    fi
    log "Downloading $R_ASSET (attempt $attempt of 3)"
    if [ -f "$part" ] && [ "$(file_size "$part")" = "$R_SIZE" ]; then
      break
    fi
    if curl_gh --fail --location "${progress[@]}" --continue-at - --output "$part" \
        -H 'User-Agent: fetch-rbfx-sdk' "$R_URL" >&2; then
      size="$(file_size "$part")"
      if [ "$size" = "$R_SIZE" ]; then
        break
      fi
      log "Downloaded $size bytes, expected $R_SIZE."
    fi
    if [ "$attempt" -eq 3 ]; then
      die "download of $R_ASSET failed after 3 attempts."
    fi
    sleep 2
  done

  if ! verify_archive "$part"; then
    rm -f "$part"
    die "downloaded archive $R_ASSET is corrupt. Run again."
  fi
  mv -f "$part" "$archive"
}

normalize_sdk_cmake_exports() {
  local sdk_root="$1"
  local cmake_file="$sdk_root/share/Urho3D/CMake/Urho3D.cmake"
  local debian_dbus_path="/usr/lib/x86_64-linux-gnu/libdbus-1.so"

  if [ ! -f "$cmake_file" ] || [ -e "$debian_dbus_path" ]; then
    return 0
  fi

  if command -v pkg-config >/dev/null 2>&1 && pkg-config --exists dbus-1; then
    sed -i "s#$debian_dbus_path#dbus-1#g" "$cmake_file"
    log "Normalized missing DBus link path in $cmake_file"
  fi
}

check_wasm_hash() {
  local name="$R_ASSET" hash commit remote
  hash="${name##*-rbfx-}"
  hash="${hash%.7z}"
  command -v git >/dev/null 2>&1 || return 0
  remote="$(GIT_TERMINAL_PROMPT=0 timeout 20 git ls-remote "$RBFX_GIT_URL" refs/tags/latest 'refs/tags/latest^{}' 2>/dev/null)" || return 0
  commit="$(awk '$2 ~ /\^\{\}$/ {print $1}' <<<"$remote" | head -n1)"
  if [ -z "$commit" ]; then
    commit="$(awk 'NR == 1 {print $1}' <<<"$remote")"
  fi
  if [ -z "$commit" ] || [ -z "$hash" ]; then
    return 0
  fi
  case "$commit" in
    "$hash"*) ;;
    *) warn "the Wasm SDK was built from rbfx $hash, but the rbfx 'latest' tag is at ${commit:0:12}." ;;
  esac
}

extract_sdk() {
  local rid="$1" archive="$2"
  local sdk7z extracted_root entries log_file
  sdk7z="$(find_7z)"

  log "Testing $R_ASSET"
  log_file="$(mktemp)"
  if ! "$sdk7z" t -bd "$archive" >"$log_file" 2>&1; then
    tail -n 20 "$log_file" >&2
    rm -f "$log_file"
    die "archive test failed for $archive. Delete it or use --force."
  fi

  TMP_DIR="$(mktemp -d "$OUTPUT/.extract.XXXXXX")"
  log "Extracting $R_ASSET"
  if ! "$sdk7z" x -y -bd "-o$TMP_DIR" "$archive" >"$log_file" 2>&1; then
    tail -n 20 "$log_file" >&2
    rm -f "$log_file"
    die "extraction of $archive failed."
  fi
  rm -f "$log_file"

  extracted_root="$TMP_DIR"
  if [ ! -f "$extracted_root/$CONFIG_RELATIVE" ]; then
    entries="$(find "$TMP_DIR" -mindepth 1 -maxdepth 1)"
    if [ "$(wc -l <<<"$entries")" -eq 1 ] && [ -d "$entries" ]; then
      extracted_root="$entries"
    fi
  fi
  if [ ! -f "$extracted_root/$CONFIG_RELATIVE" ]; then
    die "$R_ASSET does not contain $CONFIG_RELATIVE."
  fi
  log "Archive top directory: $([ "$extracted_root" = "$TMP_DIR" ] && echo '(none, files at archive root)' || basename "$extracted_root")"

  rm -rf "${OUTPUT:?}/$rid"
  mv "$extracted_root" "$OUTPUT/$rid"
  cleanup
  TMP_DIR=""
  # The exported link path is a Debian host path. Only the Linux SDK has it.
  if [ "$rid" = "linux-x64" ]; then
    normalize_sdk_cmake_exports "$OUTPUT/$rid"
  fi
}

write_stamp() {
  local rid="$1"
  jq -n --arg asset "$R_ASSET" --argjson size "$R_SIZE" --arg digest "$R_DIGEST" \
    --arg updated "$R_UPDATED" --arg tag "$R_TAG" --arg repo "$R_REPO" --arg rid "$rid" \
    '{asset: $asset, size: $size, digest: $digest, updated_at: $updated, tag: $tag, source_repo: $repo, rid: $rid}' \
    >"$OUTPUT/$rid/$STAMP_NAME"
}

ensure_rid() {
  local rid="$1"
  local sdk_root="$OUTPUT/$rid"
  local archive

  if ! resolve_rid "$rid"; then
    if [ "$FORCE" -eq 0 ] && stamp_is_usable "$rid"; then
      log "Notice: no network. Using the existing SDK in $sdk_root."
      return 0
    fi
    die "cannot read release metadata and no usable SDK exists for $rid."
  fi

  if [ "$rid" = "browser-wasm" ]; then
    check_wasm_hash
  fi

  if [ "$FORCE" -eq 0 ] && stamp_matches_api "$rid"; then
    log "SDK $rid is up to date ($R_ASSET)."
    return 0
  fi

  mkdir -p "$OUTPUT/.archives"
  archive="$OUTPUT/.archives/$R_ASSET"
  if [ "$FORCE" -eq 1 ] || [ ! -f "$archive" ] || ! verify_archive "$archive" 2>/dev/null; then
    rm -f "$archive"
    download_archive "$archive"
  else
    log "Using cached archive $R_ASSET"
  fi

  extract_sdk "$rid" "$archive"
  write_stamp "$rid"
  log "SDK $rid is ready in $sdk_root"
}

parse_args() {
  while [ $# -gt 0 ]; do
    case "$1" in
      --rid) [ $# -ge 2 ] || die "--rid needs a value."; RIDS+=("$2"); shift 2 ;;
      --all) ALL=1; shift ;;
      --link) [ $# -ge 2 ] || die "--link needs a value."; LINK="$2"; shift 2 ;;
      --rbfx-tag) [ $# -ge 2 ] || die "--rbfx-tag needs a value."; RBFX_TAG="$2"; shift 2 ;;
      --web-tag) [ $# -ge 2 ] || die "--web-tag needs a value."; WEB_TAG="$2"; shift 2 ;;
      --output) [ $# -ge 2 ] || die "--output needs a value."; OUTPUT="$2"; shift 2 ;;
      --force) FORCE=1; shift ;;
      --print-root) PRINT_ROOT=1; shift ;;
      --print-cache-key) PRINT_CACHE_KEY=1; shift ;;
      --help|-h) usage; exit 0 ;;
      *) usage; die "unknown option '$1'." ;;
    esac
  done
}

main() {
  parse_args "$@"

  case "$LINK" in
    auto|dll|lib) ;;
    *) die "--link must be auto, dll or lib." ;;
  esac

  if [ "$ALL" -eq 1 ]; then
    RIDS=("${ALL_RIDS[@]}")
    warn "--all downloads every SDK (about 4 GB)."
  elif [ "${#RIDS[@]}" -eq 0 ]; then
    RIDS=("$(default_rid)")
  fi

  local rid
  for rid in "${RIDS[@]}"; do
    is_known_rid "$rid" || die "unknown RID '$rid'. Known RIDs: ${ALL_RIDS[*]}"
  done
  if [ "$PRINT_ROOT" -eq 1 ] && [ "${#RIDS[@]}" -ne 1 ]; then
    die "--print-root needs exactly one RID."
  fi

  require_tools
  mkdir -p "$OUTPUT"
  OUTPUT="$(cd -- "$OUTPUT" && pwd)"

  if [ "$PRINT_CACHE_KEY" -eq 1 ]; then
    for rid in "${RIDS[@]}"; do
      resolve_rid "$rid" || die "cannot read release metadata for $rid."
      echo "$R_ASSET-$R_UPDATED"
    done
    return 0
  fi

  for rid in "${RIDS[@]}"; do
    ensure_rid "$rid"
  done

  if [ "$PRINT_ROOT" -eq 1 ]; then
    echo "$OUTPUT/${RIDS[0]}"
  fi
}

main "$@"
