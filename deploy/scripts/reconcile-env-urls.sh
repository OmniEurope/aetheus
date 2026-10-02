#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
#
# Makes the public URLs of a persistent environment file follow the libraries (PLAN-003 2.1).
#
# The environment file is written once, at the first deployment, and then reused verbatim: it holds
# the secrets an existing database was created under. Its URL lines are not secrets, though, and a
# host renamed in a library (the API moving to api.<domain>) would never reach them. This rewrites
# only the KEY=URL lines it is given, and only when they differ, after a dated copy of the file, through
# a temporary file moved into place so an interruption cannot leave it truncated. Every other line is
# kept byte for byte.
#
# Usage: reconcile-env-urls.sh ENV_FILE KEY=https://host [KEY=https://host ...]
set -eu

fail() { echo "FATAL: $*" >&2; exit 1; }

[ "$#" -ge 2 ] || fail "Usage: reconcile-env-urls.sh ENV_FILE KEY=URL ..."
env_file="$1"
shift
[ -f "$env_file" ] && [ ! -L "$env_file" ] || fail "Not a regular environment file: $env_file"

for pair in "$@"; do
  key="${pair%%=*}"
  url="${pair#*=}"
  case "$key" in ''|*[!A-Z0-9_]*) fail "Not an environment key: '$key'" ;; esac
  # A URL, not a shell or sed expression: it is written into a file Compose reads.
  case "$url" in
    https://*/*|https://*[!a-zA-Z0-9.:-]*) fail "$key must be https://host[:port] with nothing else: '$url'" ;;
    https://?*) ;;
    *) fail "$key must be an https URL: '$url'" ;;
  esac
done

changed=""
temporary="$env_file.urls.$$"
trap 'rm -f "$temporary"' EXIT
cp -p "$env_file" "$temporary"
for pair in "$@"; do
  key="${pair%%=*}"
  url="${pair#*=}"
  current="$(sed -n "s/^$key=//p" "$temporary" | tail -n 1)"
  [ "$current" != "$url" ] || continue
  if grep -q "^$key=" "$temporary"; then
    awk -v key="$key" -v line="$pair" 'index($0, key "=") == 1 { print line; next } { print }' \
      "$temporary" > "$temporary.next"
    mv "$temporary.next" "$temporary"
  else
    printf '%s\n' "$pair" >> "$temporary"
  fi
  echo ">>> $key: '${current:-unset}' -> '$url'"
  changed="$changed $key"
done

if [ -z "$changed" ]; then
  echo ">>> Public URLs already match the libraries."
  exit 0
fi
backup="$(dirname "$env_file")/$(basename "$env_file")-$(date -u +"%Y%m%dT%H%M%SZ")"
[ -f "$backup" ] || cp -p "$env_file" "$backup"
chmod 600 "$backup" "$temporary"
mv "$temporary" "$env_file"
trap - EXIT
echo ">>> Rewrote$changed in $env_file (previous copy: $backup)."
