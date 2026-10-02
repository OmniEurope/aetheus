#!/bin/sh
# Render the versioned blue-green Apache Define template without accepting arbitrary values.
#
#   render-apache-upstream.sh TEMPLATE OUTPUT FRONT_PORT BACK_PORT ACTIVE_COLOR
#       the live upstream file itself (release-fast and its rollback);
#   render-apache-upstream.sh --define-only TEMPLATE OUTPUT
#       a run-local copy with only the environment's Define name resolved, for the bluegreen-switch
#       step, which substitutes the ports and the colour and nothing else.
#
# Environment: UPSTREAM_DEFINE (AETHEUS in production, AETHEUS_DEMO for the demo), and for the first
# form UPSTREAM_CONF, the one destination the live file may have. PLAN-003 2.1: the guard compares
# with that variable instead of a literal path, so the demo can use the same mechanism.
set -eu

define_only=false
if [ "${1:-}" = --define-only ]; then
  define_only=true
  shift
  [ "$#" -eq 2 ] || { echo "Usage: render-apache-upstream.sh --define-only TEMPLATE OUTPUT" >&2; exit 2; }
else
  [ "$#" -eq 5 ] || {
    echo "Usage: render-apache-upstream.sh TEMPLATE OUTPUT FRONT_PORT BACK_PORT ACTIVE_COLOR" >&2
    exit 2
  }
fi

template="$1"
output="$2"
define="${UPSTREAM_DEFINE:-}"

case "$template" in */.pipeline/configs/apache/aetheus-upstream.conf) ;; *) echo "Unexpected Apache upstream template: $template" >&2; exit 2 ;; esac
case "$define" in [A-Z]*) ;; *) echo "UPSTREAM_DEFINE must start with an upper-case letter: '$define'" >&2; exit 2 ;; esac
case "$define" in *[!A-Z0-9_]*) echo "UPSTREAM_DEFINE must be upper case, digits and underscores: '$define'" >&2; exit 2 ;; esac
[ -f "$template" ] || { echo "Apache upstream template not found: $template" >&2; exit 2; }

if [ "$define_only" = true ]; then
  temporary="${output}.tmp.$$"
  trap 'rm -f "$temporary"' EXIT
  sed -e "s/#{UPSTREAM_DEFINE}#/$define/g" "$template" > "$temporary"
  # The switch renders what is left; a copy with nothing left to substitute would make it reload the
  # configuration already in place and report a cutover that never happened.
  grep -q '#{FRONT_PORT}#' "$temporary" || { echo "The upstream template lost its front-port placeholder." >&2; exit 2; }
  grep -q '#{BACK_PORT}#' "$temporary" || { echo "The upstream template lost its back-port placeholder." >&2; exit 2; }
  if grep -q '#{UPSTREAM_DEFINE}#' "$temporary"; then echo "The Define name was not resolved." >&2; exit 2; fi
  mv "$temporary" "$output"
  trap - EXIT
  exit 0
fi

front_port="$3"
back_port="$4"
active_color="$5"
expected="${UPSTREAM_CONF:-}"
case "$expected" in */sites-available/?*) ;; *) echo "UPSTREAM_CONF must name a file in a sites-available directory: '$expected'" >&2; exit 2 ;; esac
case "$expected" in *..*) echo "UPSTREAM_CONF must not contain a traversal: $expected" >&2; exit 2 ;; esac
[ "$output" = "$expected" ] || { echo "Unexpected Apache upstream destination: $output (expected $expected)" >&2; exit 2; }
case "$active_color" in blue|green) ;; *) echo "Invalid active colour: $active_color" >&2; exit 2 ;; esac
case "$front_port" in ''|*[!0-9]*) echo "Invalid front port: $front_port" >&2; exit 2 ;; esac
case "$back_port" in ''|*[!0-9]*) echo "Invalid back port: $back_port" >&2; exit 2 ;; esac
[ "$front_port" -ge 1 ] && [ "$front_port" -le 65535 ] || { echo "Front port is out of range." >&2; exit 2; }
[ "$back_port" -ge 1 ] && [ "$back_port" -le 65535 ] || { echo "Back port is out of range." >&2; exit 2; }

temporary="${output}.tmp.$$"
trap 'rm -f "$temporary"' EXIT
sed \
  -e "s/#{UPSTREAM_DEFINE}#/$define/g" \
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
