#!/usr/bin/env bash
# Fails when a distributed file holds a path of the build machine.
#
# Usage: check-no-local-paths.sh --root <dir|nupkg|zip|7z> [--root ...] --forbid <path> [--forbid ...]
#
# --root    One or more directories or archives, up to the next option. An archive (.nupkg, .zip, .7z) is extracted into a temporary directory.
# --forbid  A path prefix that must not appear, for example the repository checkout, $HOME or RUNNER_TEMP.
#           The script checks the prefix with both slash styles. With cygpath it also checks the forms of
#           cygpath -m and cygpath -w, and a drive letter ignores case. A $HOME of Git Bash (/c/Users/x) matches C:\Users\x.
#
# Text files are searched as they are. A binary file (.so, .dylib, .dll, .pdb, .a, .lib, .exe, .o, .wasm)
# is searched for printable strings. Strings in UTF-16 (the user strings of a .NET assembly) are included.
# The script prints each file with 2 samples and exits with 1 when it finds a prefix. It exits with 2 on a usage error.
# It needs bash, grep, tr, and unzip, bsdtar, 7z or python for an archive. It works on Linux, macOS and Git Bash.
set -uo pipefail
export LC_ALL=C

# The script checks only files that we build:
# - Text files (props, targets, nuspec, cmake, json, headers) and .pdb files.
# - Managed assemblies and native files with SharpCpp or Rbfx in the name (case-insensitive).
# It skips every other native binary (.a .so .dylib .dll .lib .exe .o .obj .wasm). Those files come from the rbfx SDK.
# The engine and its third-party libraries (for example SDL) carry the __FILE__ strings of the engine build machine.
# This rule skips libUrho3D*, Urho3D*.dll and all other files of the SDK that do not match the names above.
is_allowed() {
  local name
  name="$(basename -- "$1" | tr '[:upper:]' '[:lower:]')"
  case "$name" in
    *.pdb) return 1 ;;
    *sharpcpp*|*rbfx*) return 1 ;;
  esac
  case "$name" in
    *.so|*.so.*|*.dylib|*.dll|*.a|*.lib|*.exe|*.o|*.obj|*.wasm) return 0 ;;
  esac
  return 1
}

is_binary() {
  case "$1" in
    *.so|*.so.*|*.dylib|*.dll|*.pdb|*.a|*.lib|*.exe|*.o|*.obj|*.wasm) return 0 ;;
  esac
  return 1
}

usage() {
  echo "Usage: $0 --root <dir|nupkg|zip|7z> [--root ...] --forbid <path> [--forbid ...]" >&2
  exit 2
}

roots=()
forbidden=()
while [ $# -gt 0 ]; do
  case "$1" in
    --root)
      [ $# -ge 2 ] || usage
      shift
      # One option takes every argument up to the next option, so a shell glob works.
      while [ $# -gt 0 ] && [ "${1#--}" = "$1" ]; do roots+=("$1"); shift; done ;;
    --forbid) [ $# -ge 2 ] || usage; forbidden+=("$2"); shift 2 ;;
    -h|--help) usage ;;
    *) echo "Unknown argument: $1" >&2; usage ;;
  esac
done
[ ${#roots[@]} -gt 0 ] || usage
[ ${#forbidden[@]} -gt 0 ] || usage

# One entry for each forbidden prefix and form, without a trailing slash. An entry starts with "i:" (ignore case) or "s:".
# A path that starts with a drive letter ("C:" or "/c/") ignores case, because Windows ignores case in a drive letter.
# With cygpath (Git Bash) the script also tests the Windows forms of a path. $HOME in Git Bash is /c/Users/x,
# but a tool of Windows writes C:\Users\x or C:/Users/x.
patterns=()
add_pattern() {
  local value="$1" mode="s" entry
  case "$value" in
    [A-Za-z]:*|/[A-Za-z]/*|/[A-Za-z]) mode="i" ;;
  esac
  for entry in "${patterns[@]+"${patterns[@]}"}"; do
    [ "$entry" = "$mode:$value" ] && return
  done
  patterns+=("$mode:$value")
}
for prefix in "${forbidden[@]}"; do
  forms=("$prefix")
  if command -v cygpath >/dev/null 2>&1; then
    forms+=("$(cygpath -m -- "$prefix" 2>/dev/null)" "$(cygpath -w -- "$prefix" 2>/dev/null)")
  fi
  for form in "${forms[@]}"; do
    [ -n "$form" ] || continue
    while [ "${#form}" -gt 1 ] && { [ "${form%/}" != "$form" ] || [ "${form%\\}" != "$form" ]; }; do
      form="${form%/}"
      form="${form%\\}"
    done
    [ ${#form} -ge 2 ] || { echo "Refusing the forbidden prefix '$form': it is too short." >&2; exit 2; }
    forward="${form//\\//}"
    back="${forward//\//\\}"
    add_pattern "$forward"
    [ "$back" != "$forward" ] && add_pattern "$back"
  done
done

work="$(mktemp -d "${TMPDIR:-/tmp}/check-no-local-paths.XXXXXX")"
trap 'rm -rf "$work"' EXIT

extract() {
  local archive="$1" destination="$2"
  mkdir -p "$destination"
  case "$archive" in
    *.7z)
      local seven
      for seven in 7z 7zz 7za; do
        if command -v "$seven" >/dev/null 2>&1; then "$seven" x -y -bso0 -bsp0 "-o$destination" "$archive" && return 0; return 1; fi
      done
      echo "No 7z program found to extract $archive." >&2; return 1 ;;
    *)
      if command -v unzip >/dev/null 2>&1; then unzip -q -o "$archive" -d "$destination" && return 0; return 1; fi
      if command -v bsdtar >/dev/null 2>&1; then bsdtar -xf "$archive" -C "$destination" && return 0; return 1; fi
      if command -v 7z >/dev/null 2>&1; then 7z x -y -bso0 -bsp0 "-o$destination" "$archive" && return 0; return 1; fi
      local python
      for python in python3 python; do
        if command -v "$python" >/dev/null 2>&1; then "$python" -I -m zipfile -e "$archive" "$destination" && return 0; return 1; fi
      done
      echo "No unzip, bsdtar, 7z or python program found to extract $archive." >&2; return 1 ;;
  esac
}

# Prints up to 2 samples of a file: the match with some text around it.
# A binary file is reduced to its printable strings first. With the strings program the reduction includes
# UTF-16 strings (the user strings of a .NET assembly). Without it, NUL bytes and other
# non-printable bytes become line breaks, and UTF-16 strings are not searched.
samples() {
  local file="$1" entry="$2" pattern ignore_case=0 grep_case=()
  pattern="${entry#?:}"
  if [ "${entry%%:*}" = "i" ]; then ignore_case=1; grep_case=(-i); fi
  {
    if is_binary "$file"; then
      if command -v strings >/dev/null 2>&1; then
        strings -a "$file"
        strings -a -e l "$file"
      else
        tr -c '[:print:]' '\n' < "$file"
      fi
    else
      grep -I -F "${grep_case[@]+"${grep_case[@]}"}" -- "$pattern" "$file"
    fi
  } 2>/dev/null | PATTERN="$pattern" IGNORE_CASE="$ignore_case" awk '
    BEGIN { p = ENVIRON["PATTERN"]; ic = ENVIRON["IGNORE_CASE"] == "1"; if (ic) p = tolower(p) }
    { i = index(ic ? tolower($0) : $0, p); if (i > 0) { start = (i > 30) ? i - 30 : 1; print substr($0, start, 110); if (++n == 2) exit } }'
}

failed=0
index=0
for root in "${roots[@]}"; do
  index=$((index + 1))
  if [ -d "$root" ]; then
    scan="$root"
    label="$(basename -- "$root")"
  elif [ -f "$root" ]; then
    scan="$work/$index"
    label="$(basename -- "$root")"
    extract "$root" "$scan" || { echo "Cannot extract $root" >&2; exit 2; }
  else
    echo "Root does not exist: $root" >&2
    exit 2
  fi

  checked=0
  while IFS= read -r -d '' file; do
    is_allowed "$file" && continue
    checked=$((checked + 1))
    for pattern in "${patterns[@]}"; do
      found="$(samples "$file" "$pattern")"
      if [ -n "$found" ]; then
        failed=$((failed + 1))
        echo "FOUND $label/${file#"$scan"/} (prefix: ${pattern#?:})"
        printf '%s\n' "$found" | sed 's/^/    /'
      fi
    done
  done < <(find "$scan" -type f -print0)
  echo "Checked $checked files in $label"
done

if [ "$failed" -gt 0 ]; then
  echo "Error: $failed file hits hold a path of the build machine." >&2
  exit 1
fi
echo "No path of the build machine found."
