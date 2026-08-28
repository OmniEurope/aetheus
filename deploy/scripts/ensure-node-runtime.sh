#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2

# Resolve the exact Node.js runtime required by the release toolchain. Existing
# agents may have been binary-upgraded without re-running the root installer, so
# install a verified, non-root fallback in the agent user's home and print only
# the executable path on stdout.

set -eu

NODE_VERSION="24.4.1"
NODE_LINUX_X64_SHA256="063f2eb299ba60e3fc9b424d8e87d0e2f6be84b39bdeadc421ee2865914c498b"
NODE_ARCHIVE="node-v${NODE_VERSION}-linux-x64.tar.gz"
NODE_URL="https://nodejs.org/dist/v${NODE_VERSION}/${NODE_ARCHIVE}"

has_runtime() {
    [ -x "$1" ] && [ "$("$1" --version 2>/dev/null)" = "v${NODE_VERSION}" ]
}

if command -v node >/dev/null 2>&1; then
    SYSTEM_NODE="$(command -v node)"
    if has_runtime "$SYSTEM_NODE"; then
        printf '%s\n' "$SYSTEM_NODE"
        exit 0
    fi
fi

if [ "$(uname -s)" != "Linux" ] || [ "$(uname -m)" != "x86_64" ]; then
    echo "Pinned Node.js $NODE_VERSION bootstrap supports Linux x86_64 only" >&2
    exit 1
fi

: "${HOME:?HOME is required to install pinned Node.js without root}"
INSTALL_DIR="$HOME/.aetheus/node-v${NODE_VERSION}-linux-x64"
LOCAL_NODE="$INSTALL_DIR/bin/node"
if has_runtime "$LOCAL_NODE"; then
    printf '%s\n' "$LOCAL_NODE"
    exit 0
fi

for command_name in curl sha256sum tar; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "$command_name is required to install Node.js $NODE_VERSION" >&2
        exit 1
    fi
done

INSTALL_PARENT="$(dirname "$INSTALL_DIR")"
LOCK_DIR="${INSTALL_DIR}.lock"
mkdir -p "$INSTALL_PARENT"

attempt=0
while ! mkdir "$LOCK_DIR" 2>/dev/null; do
    if has_runtime "$LOCAL_NODE"; then
        printf '%s\n' "$LOCAL_NODE"
        exit 0
    fi
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 120 ]; then
        echo "Timed out waiting for the pinned Node.js $NODE_VERSION installation lock" >&2
        exit 1
    fi
    sleep 1
done

ARCHIVE="$(mktemp "$INSTALL_PARENT/.node-archive.XXXXXX")"
STAGING_DIR="$(mktemp -d "$INSTALL_PARENT/.node-install.XXXXXX")"
cleanup() {
    rm -f "$ARCHIVE"
    rm -rf "$STAGING_DIR"
    rmdir "$LOCK_DIR" 2>/dev/null || true
}
trap cleanup EXIT HUP INT TERM

if has_runtime "$LOCAL_NODE"; then
    printf '%s\n' "$LOCAL_NODE"
    exit 0
fi

echo "Installing pinned Node.js $NODE_VERSION in $INSTALL_DIR" >&2
curl -fsSL --retry 3 --retry-delay 2 --connect-timeout 15 \
    "$NODE_URL" -o "$ARCHIVE"
printf '%s  %s\n' "$NODE_LINUX_X64_SHA256" "$ARCHIVE" | sha256sum -c - >/dev/null
tar -xzf "$ARCHIVE" --strip-components=1 -C "$STAGING_DIR"

STAGING_NODE="$STAGING_DIR/bin/node"
if ! has_runtime "$STAGING_NODE"; then
    echo "Pinned Node.js $NODE_VERSION archive could not be verified after extraction" >&2
    exit 1
fi

rm -rf "$INSTALL_DIR"
mv "$STAGING_DIR" "$INSTALL_DIR"
STAGING_DIR="${STAGING_DIR}.installed"

if ! has_runtime "$LOCAL_NODE"; then
    echo "Pinned Node.js $NODE_VERSION installation could not be verified" >&2
    exit 1
fi

printf '%s\n' "$LOCAL_NODE"
