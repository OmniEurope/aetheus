#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

rc="${1:-}"
flipped="${2:-}"
committed="${3:-}"
case "$rc" in ''|*[!0-9]*) echo "invalid deployment exit code" >&2; exit 2 ;; esac
case "$flipped" in yes|no) ;; *) echo "invalid flipped state" >&2; exit 2 ;; esac
case "$committed" in yes|no) ;; *) echo "invalid committed state" >&2; exit 2 ;; esac

if [ "$rc" -eq 0 ] || [ "$committed" = yes ]; then
  printf '%s\n' none
elif [ "$flipped" = yes ]; then
  printf '%s\n' rollback
else
  printf '%s\n' cleanup
fi
