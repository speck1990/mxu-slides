#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-run}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAC_ROOT="$REPO_ROOT/apps/mac"
BUILD_ROOT="$MAC_ROOT/build/LocalInstall"
BUILT_APP="$BUILD_ROOT/Build/Products/Debug/MxU Slides.app"
INSTALL_DIR="${HOME}/Applications"
INSTALLED_APP="$INSTALL_DIR/MxU Slides.app"
APP_BINARY="$INSTALLED_APP/Contents/MacOS/MxU Slides"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This installer must run on the local Mac." >&2
  exit 1
fi
command -v xcodebuild >/dev/null || { echo "Xcode 26 is required." >&2; exit 1; }
command -v node >/dev/null || { echo "Node 24 is required." >&2; exit 1; }
if ! command -v xcodegen >/dev/null; then
  command -v brew >/dev/null || { echo "Install Homebrew or XcodeGen, then retry." >&2; exit 1; }
  brew install xcodegen
fi

pkill -x "MxU Slides" >/dev/null 2>&1 || true
(
  cd "$MAC_ROOT"
  xcodegen generate
  xcodebuild -project MxUSlides.xcodeproj -scheme MxUSlides -configuration Debug \
    -derivedDataPath "$BUILD_ROOT" \
    -disableAutomaticPackageResolution -onlyUsePackageVersionsFromResolvedFile build
)
test -d "$BUILT_APP" || { echo "Xcode did not produce $BUILT_APP" >&2; exit 1; }
mkdir -p "$INSTALL_DIR"
rm -rf "$INSTALLED_APP"
ditto "$BUILT_APP" "$INSTALLED_APP"

case "$MODE" in
  run)
    /usr/bin/open -n "$INSTALLED_APP"
    ;;
  --debug|debug)
    lldb -- "$APP_BINARY"
    ;;
  --logs|logs)
    /usr/bin/open -n "$INSTALLED_APP"
    /usr/bin/log stream --info --style compact --predicate 'process == "MxU Slides"'
    ;;
  --telemetry|telemetry)
    /usr/bin/open -n "$INSTALLED_APP"
    /usr/bin/log stream --info --style compact --predicate 'subsystem == "com.example.mxuslides"'
    ;;
  --verify|verify)
    /usr/bin/open -n "$INSTALLED_APP"
    sleep 2
    pgrep -x "MxU Slides" >/dev/null
    ;;
  *)
    echo "usage: $0 [run|--debug|--logs|--telemetry|--verify]" >&2
    exit 2
    ;;
esac
