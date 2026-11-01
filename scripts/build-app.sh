#!/usr/bin/env bash
# Builds FlowScribe.app and installs it in /Applications.
#
# Signing matters: macOS ties the Accessibility permission to the app's code
# signature. An ad-hoc signature changes on every build, so macOS silently
# forgets the permission and pasting stops working ("goes silent").
# This script creates a local self-signed certificate once and signs every
# build with it, so the permission survives rebuilds.
set -euo pipefail

cd "$(dirname "$0")/.."
APP_NAME="FlowScribe"
IDENTITY="FlowScribe Local Signing"
BUILD_DIR=".build/release"
APP="build/${APP_NAME}.app"
INSTALL_DIR="/Applications"

echo "==> Building (release, Apple Silicon)"
swift build -c release --arch arm64

echo "==> Assembling ${APP}"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BUILD_DIR/$APP_NAME" "$APP/Contents/MacOS/$APP_NAME"
cp Resources/Info.plist "$APP/Contents/Info.plist"
# SwiftPM resource bundles (from dependencies) must sit next to the binary's bundle.
for bundle in "$BUILD_DIR"/*.bundle; do
    [ -e "$bundle" ] && cp -R "$bundle" "$APP/Contents/Resources/"
done

if ! security find-identity -v -p codesigning | grep -q "$IDENTITY"; then
    echo "==> Creating a local signing certificate (one time): $IDENTITY"
    TMP="$(mktemp -d)"
    cat > "$TMP/cert.cfg" <<EOF
[ req ]
distinguished_name = dn
x509_extensions = ext
prompt = no
[ dn ]
CN = $IDENTITY
[ ext ]
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
basicConstraints = critical, CA:false
EOF
    openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
        -keyout "$TMP/key.pem" -out "$TMP/cert.pem" -config "$TMP/cert.cfg" >/dev/null 2>&1
    PASS="flowscribe-$(date +%s)"
    openssl pkcs12 -export -legacy -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
        -out "$TMP/cert.p12" -passout "pass:$PASS" >/dev/null 2>&1 \
      || openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
        -out "$TMP/cert.p12" -passout "pass:$PASS" >/dev/null 2>&1
    security import "$TMP/cert.p12" -k "$HOME/Library/Keychains/login.keychain-db" \
        -P "$PASS" -T /usr/bin/codesign >/dev/null
    echo "==> macOS will ask for your password to trust this certificate (one time)"
    security add-trusted-cert -r trustRoot -p codeSign \
        -k "$HOME/Library/Keychains/login.keychain-db" "$TMP/cert.pem" || true
    rm -rf "$TMP"
fi

echo "==> Signing with \"$IDENTITY\""
if ! codesign --force --deep --sign "$IDENTITY" "$APP" 2>/dev/null; then
    echo "!! Could not sign with the local certificate; falling back to ad-hoc."
    echo "!! You will need to re-enable Accessibility for FlowScribe after every rebuild."
    codesign --force --deep --sign - "$APP"
fi

echo "==> Installing to ${INSTALL_DIR}"
pkill -x "$APP_NAME" 2>/dev/null || true
# Relaunching while the old copy is still quitting fails (LaunchServices -600).
for _ in $(seq 1 50); do pgrep -x "$APP_NAME" >/dev/null || break; sleep 0.1; done
rm -rf "${INSTALL_DIR:?}/${APP_NAME}.app"
cp -R "$APP" "$INSTALL_DIR/"
open "$INSTALL_DIR/${APP_NAME}.app"

echo
echo "Done. First launch downloads the speech model once (a few hundred MB)."
echo "Grant Microphone and Accessibility when asked, then hold Right Option and speak."
