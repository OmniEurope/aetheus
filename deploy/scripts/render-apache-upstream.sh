#!/bin/sh
# Render the versioned blue-green Apache Define template without accepting arbitrary values.
set -eu

[ "$#" -eq 5 ] || {
  echo "Usage: render-apache-upstream.sh TEMPLATE OUTPUT FRONT_PORT BACK_PORT ACTIVE_COLOR" >&2
  exit 2
}

template="$1"
output="$2"
front_port="$3"
back_port="$4"
active_color="$5"

case "$template" in */.pipeline/configs/apache/aetheus-upstream.conf) ;; *) echo "Unexpected Apache upstream template: $template" >&2; exit 2 ;; esac
case "$output" in /etc/apache2/sites-available/000-aetheus-upstream.conf) ;; *) echo "Unexpected Apache upstream destination: $output" >&2; exit 2 ;; esac
case "$active_color" in blue|green) ;; *) echo "Invalid active colour: $active_color" >&2; exit 2 ;; esac
case "$front_port" in ''|*[!0-9]*) echo "Invalid front port: $front_port" >&2; exit 2 ;; esac
case "$back_port" in ''|*[!0-9]*) echo "Invalid back port: $back_port" >&2; exit 2 ;; esac
[ "$front_port" -ge 1 ] && [ "$front_port" -le 65535 ] || { echo "Front port is out of range." >&2; exit 2; }
[ "$back_port" -ge 1 ] && [ "$back_port" -le 65535 ] || { echo "Back port is out of range." >&2; exit 2; }
[ -f "$template" ] || { echo "Apache upstream template not found: $template" >&2; exit 2; }

temporary="${output}.tmp.$$"
trap 'rm -f "$temporary"' EXIT
sed \
  -e "s/#{FRONT_PORT}#/$front_port/g" \
  -e "s/#{BACK_PORT}#/$back_port/g" \
  -e "s/#{ACTIVE_COLOR}#/$active_color/g" \
  "$template" > "$temporary"
if grep -q '#{' "$temporary"; then
  echo "Apache upstream template contains an unresolved token." >&2
  exit 2
fi
mv "$temporary" "$output"
trap - EXIT
