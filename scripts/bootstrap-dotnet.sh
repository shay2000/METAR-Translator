#!/usr/bin/env bash
#
# Installs the .NET SDK version pinned in global.json into ./.dotnet.
#
# ./.dotnet is gitignored, so it is a local-only artifact. This script makes it
# reproducible on a fresh clone, ensuring local builds use the same SDK band as
# CI (actions/setup-dotnet with dotnet-version: 8.0.x) regardless of which SDK
# is installed system-wide.
#
# Safe to re-run: exits early if the local SDK already satisfies global.json.
#
# Usage:
#   ./scripts/bootstrap-dotnet.sh
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_DIR="$REPO_ROOT/.dotnet"
GLOBAL_JSON="$REPO_ROOT/global.json"

if [[ ! -f "$GLOBAL_JSON" ]]; then
  echo "error: $GLOBAL_JSON not found" >&2
  exit 1
fi

# Read the pinned SDK version, preferring jq but falling back to sed so the
# script works without extra tooling.
if command -v jq >/dev/null 2>&1; then
  SDK_VERSION="$(jq -r '.sdk.version // empty' "$GLOBAL_JSON")"
else
  SDK_VERSION="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$GLOBAL_JSON" | head -1)"
fi

if [[ -z "${SDK_VERSION:-}" || "$SDK_VERSION" == "null" ]]; then
  echo "error: could not read sdk.version from $GLOBAL_JSON" >&2
  exit 1
fi

# `dotnet --version` honours global.json, so a successful call from the repo
# root means the existing local install can already build this solution.
if [[ -x "$DOTNET_DIR/dotnet" ]] && current="$(
  cd "$REPO_ROOT" && DOTNET_ROOT="$DOTNET_DIR" "$DOTNET_DIR/dotnet" --version 2>/dev/null
)"; then
  echo "Local SDK $current already satisfies global.json (pinned: $SDK_VERSION). Nothing to do."
  exit 0
fi

echo "Installing .NET SDK $SDK_VERSION into $DOTNET_DIR ..."

INSTALL_SCRIPT="$(mktemp "${TMPDIR:-/tmp}/dotnet-install.XXXXXX.sh")"
cleanup() { rm -f "$INSTALL_SCRIPT"; }
trap cleanup EXIT

curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$INSTALL_SCRIPT"
chmod +x "$INSTALL_SCRIPT"
"$INSTALL_SCRIPT" --version "$SDK_VERSION" --install-dir "$DOTNET_DIR" --no-path

echo
echo "Installed SDKs in $DOTNET_DIR:"
DOTNET_ROOT="$DOTNET_DIR" "$DOTNET_DIR/dotnet" --list-sdks

cat <<EOF

Next steps:
  * In VS Code: reload the window (Cmd+Shift+P -> "Developer: Reload Window")
    so .vscode/settings.json applies to new integrated terminals.
  * In an external terminal, either use ./.dotnet/dotnet directly, or export:
      export DOTNET_ROOT="$DOTNET_DIR"
      export PATH="\$DOTNET_ROOT:\$PATH"
EOF
