#!/bin/bash
set -euo pipefail
RID="${1:?runtime identifier}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
CACHE="$ROOT/.firebase-ios"
if [ "$RID" = "iossimulator-arm64" ]; then
  DEST="$CACHE/iphonesimulator"
else
  DEST="$CACHE/iphoneos"
fi
if [ -d "$DEST/FirebaseCore.framework" ]; then
  exit 0
fi
mkdir -p "$CACHE"
ZIP="$CACHE/Firebase-12.12.0.zip"
if [ ! -f "$ZIP" ]; then
  curl -L --fail --retry 3 -o "$ZIP.partial" \
    "https://github.com/firebase/firebase-ios-sdk/releases/download/12.12.0/Firebase.zip"
  mv "$ZIP.partial" "$ZIP"
fi
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
unzip -q "$ZIP" -d "$STAGE"
rm -rf "$DEST"
mkdir -p "$DEST"
while IFS= read -r xcf; do
  slice=""
  if [ "$RID" = "iossimulator-arm64" ]; then
    for candidate in ios-arm64-simulator ios-arm64_x86_64-simulator; do
      if [ -d "$xcf/$candidate" ]; then
        slice="$xcf/$candidate"
        break
      fi
    done
  elif [ -d "$xcf/ios-arm64" ]; then
    slice="$xcf/ios-arm64"
  fi
  if [ -n "$slice" ]; then
    find "$slice" -maxdepth 3 -type d -name '*.framework' -exec cp -R {} "$DEST/" \;
  fi
done < <(find "$STAGE" -type d -name '*.xcframework')
test -d "$DEST/FirebaseCore.framework"
