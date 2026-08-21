#!/bin/bash

set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: scripts/package-macos.sh <osx-arm64|osx-x64> <version> [build-number] [output-directory]

Builds a self-contained METAR Viewer app bundle, signs it, and creates a verified DMG.

Environment:
  CONFIGURATION             .NET build configuration (default: Release)
  METAR_DOTNET              dotnet executable to use (default: dotnet from PATH)
  MACOS_SIGNING_IDENTITY    codesign identity (default: - for an ad-hoc local/CI signature)

Examples:
  scripts/package-macos.sh osx-arm64 1.2.3
  scripts/package-macos.sh osx-x64 1.2.3 42 artifacts/macos/osx-x64
USAGE
}

if [[ $# -lt 2 || $# -gt 4 ]]; then
  usage >&2
  exit 64
fi

runtime="$1"
version="$2"
build_number="${3:-1}"
output_argument="${4:-}"
configuration="${CONFIGURATION:-Release}"
dotnet_command="${METAR_DOTNET:-dotnet}"
signing_identity="${MACOS_SIGNING_IDENTITY:--}"

case "$runtime" in
  osx-arm64)
    architecture="arm64"
    architecture_label="Apple-Silicon"
    minimum_system_version="14.0"
    ;;
  osx-x64)
    architecture="x86_64"
    architecture_label="Intel"
    minimum_system_version="14.0"
    ;;
  *)
    echo "Unsupported runtime: $runtime (expected osx-arm64 or osx-x64)" >&2
    exit 64
    ;;
esac

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must contain exactly three numeric components, for example 1.2.3." >&2
  exit 64
fi

if [[ ! "$build_number" =~ ^[1-9][0-9]*$ ]]; then
  echo "Build number must be a positive integer." >&2
  exit 64
fi

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packaging must run on macOS." >&2
  exit 69
fi

for required_command in ditto hdiutil lipo plutil codesign shasum; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "Required command is unavailable: $required_command" >&2
    exit 69
  fi
done

if ! command -v "$dotnet_command" >/dev/null 2>&1; then
  echo "Configured .NET executable is unavailable: $dotnet_command" >&2
  exit 69
fi

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_directory/.." && pwd)"
project_path="$repository_root/src/MetarViewer.App.Avalonia/MetarViewer.App.Avalonia.csproj"

plist_template="$repository_root/packaging/macos/Info.plist"
entitlements_path="$repository_root/packaging/macos/MetarViewer.entitlements"

cd "$repository_root"

if [[ -z "$output_argument" ]]; then
  output_directory="$repository_root/artifacts/macos/$runtime"
elif [[ "$output_argument" = /* ]]; then
  output_directory="$output_argument"
else
  output_directory="$repository_root/$output_argument"
fi

for required_file in "$project_path" "$plist_template" "$entitlements_path"; do
  if [[ ! -f "$required_file" ]]; then
    echo "Required packaging input is missing: $required_file" >&2
    exit 66
  fi
done

staging_root="$(mktemp -d "${TMPDIR:-/tmp}/metar-viewer-macos.XXXXXX")"
mount_point="$staging_root/mounted-dmg"
is_mounted=false
cleanup() {
  if [[ "$is_mounted" == true ]]; then
    hdiutil detach "$mount_point" >/dev/null 2>&1 || true
  fi
  rm -rf "$staging_root"
}
trap cleanup EXIT

publish_directory="$staging_root/publish"
app_bundle="$staging_root/METAR Viewer.app"
macos_directory="$app_bundle/Contents/MacOS"
resources_directory="$app_bundle/Contents/Resources"
dmg_root="$staging_root/dmg-root"
dmg_name="METAR-Viewer-Mac-${architecture_label}-${version}.dmg"
staged_dmg="$staging_root/$dmg_name"

mkdir -p \
  "$publish_directory" \
  "$macos_directory" \
  "$resources_directory" \
  "$dmg_root" \
  "$mount_point" \
  "$output_directory"

"$dotnet_command" publish "$project_path" \
  --configuration "$configuration" \
  --runtime "$runtime" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:ContinuousIntegrationBuild=true \
  --output "$publish_directory"

ditto "$publish_directory" "$macos_directory"
chmod +x "$macos_directory/MetarViewer"
cp "$plist_template" "$app_bundle/Contents/Info.plist"

/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app_bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $build_number" "$app_bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :LSMinimumSystemVersion $minimum_system_version" "$app_bundle/Contents/Info.plist"

plutil -lint "$app_bundle/Contents/Info.plist"
test -x "$macos_directory/MetarViewer"
lipo "$macos_directory/MetarViewer" -verify_arch "$architecture"

sign_code() {
  local code_path="$1"

  if [[ "$signing_identity" == "-" ]]; then
    codesign --force --sign - --timestamp=none "$code_path"
  else
    codesign --force --sign "$signing_identity" --options runtime --timestamp "$code_path"
  fi
}

# Sign nested native code before the containing bundle. Avoid codesign --deep for
# signing: each code object receives its own deterministic signature.
while IFS= read -r native_library; do
  sign_code "$native_library"
done < <(find "$macos_directory" -type f -name '*.dylib' -print | sort)

if [[ "$signing_identity" == "-" ]]; then
  codesign --force --sign - --timestamp=none "$app_bundle"
else
  codesign --force --sign "$signing_identity" \
    --options runtime \
    --timestamp \
    --entitlements "$entitlements_path" \
    "$app_bundle"
fi

codesign --verify --deep --strict --verbose=2 "$app_bundle"

ditto "$app_bundle" "$dmg_root/METAR Viewer.app"
ln -s /Applications "$dmg_root/Applications"
codesign --verify --deep --strict --verbose=2 "$dmg_root/METAR Viewer.app"

# hdiutil intermittently fails with "Resource busy" on CI runners when a
# lingering diskimages-helper still holds the backing store. The inputs are
# unchanged between attempts, so retrying is safe rather than papering over a
# real packaging error.
create_dmg() {
  local attempt=1
  local max_attempts=5

  while true; do
    rm -f "$staged_dmg"

    if hdiutil create \
      -volname "METAR Viewer for Mac" \
      -srcfolder "$dmg_root" \
      -ov \
      -format UDZO \
      "$staged_dmg"; then
      return 0
    fi

    if (( attempt >= max_attempts )); then
      echo "hdiutil create failed after $max_attempts attempts." >&2
      return 1
    fi

    echo "hdiutil create failed (attempt $attempt/$max_attempts); retrying in $((attempt * 5))s." >&2
    sleep "$((attempt * 5))"
    attempt="$((attempt + 1))"
  done
}

create_dmg

hdiutil verify "$staged_dmg"

final_dmg="$output_directory/$dmg_name"

ditto "$staged_dmg" "$final_dmg"
hdiutil verify "$final_dmg"

hdiutil attach \
  -readonly \
  -nobrowse \
  -mountpoint "$mount_point" \
  "$final_dmg"
is_mounted=true

mounted_app="$mount_point/METAR Viewer.app"
test -L "$mount_point/Applications"
test "$(readlink "$mount_point/Applications")" = "/Applications"
test -x "$mounted_app/Contents/MacOS/MetarViewer"
plutil -lint "$mounted_app/Contents/Info.plist"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$mounted_app/Contents/Info.plist")" = "$version"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$mounted_app/Contents/Info.plist")" = "$build_number"
test "$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$mounted_app/Contents/Info.plist")" = "$minimum_system_version"
lipo "$mounted_app/Contents/MacOS/MetarViewer" -verify_arch "$architecture"
codesign --verify --deep --strict --verbose=2 "$mounted_app"

hdiutil detach "$mount_point"
is_mounted=false

(
  cd "$output_directory"
  shasum -a 256 "$dmg_name" > "$dmg_name.sha256"
)

echo "Created $final_dmg"
echo "Created $final_dmg.sha256"
