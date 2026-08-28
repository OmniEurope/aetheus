#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Retain application images after a successful deployment smoke test. Every image referenced by any
# container (running or stopped) is protected, which preserves the active and blue-green N-1 colours.
set -eu

DRY_RUN="${AETHEUS_IMAGE_RETENTION_DRY_RUN:-false}"
KEEP="${AETHEUS_IMAGE_RETENTION_KEEP:-2}"
case "$DRY_RUN" in true|false) ;; *) echo "AETHEUS_IMAGE_RETENTION_DRY_RUN must be true or false" >&2; exit 2 ;; esac
case "$KEEP" in ''|*[!0-9]*) echo "AETHEUS_IMAGE_RETENTION_KEEP must be a positive integer" >&2; exit 2 ;; esac
[ "$KEEP" -ge 2 ] || { echo "Image retention must keep at least active and N-1" >&2; exit 2; }
[ "$#" -gt 0 ] || { echo "At least one managed image repository is required" >&2; exit 2; }

PROTECTED="$(mktemp "${TMPDIR:-/tmp}/aetheus-protected-images.XXXXXX")"
LOCK_FILE="${TMPDIR:-/tmp}/aetheus-image-retention.lock"
trap 'rm -f "$PROTECTED"' EXIT INT TERM

# Serialize inventory and deletion across concurrent releases. Without this lock, two
# deployments can each decide that the other's newly-tagged image is old and unprotected.
command -v flock >/dev/null 2>&1 || {
    echo "flock is required for safe image retention; refusing image retention." >&2
    exit 1
}
exec 9>"$LOCK_FILE"
flock -n 9 || {
    echo "Another image-retention operation is active; refusing concurrent retention." >&2
    exit 1
}

refresh_protected_images() {
    CONTAINERS="$(docker ps -aq --no-trunc)" || {
        echo "Could not enumerate containers; refusing image retention." >&2
        exit 1
    }
    : > "$PROTECTED"
    while IFS= read -r container; do
        [ -n "$container" ] || continue
        if ! image_id="$(docker inspect --format '{{.Image}}' "$container")"; then
            # Build/migration containers may legitimately disappear between the snapshot above and
            # this inspection. Re-query the daemon before deciding: a vanished container no longer
            # protects an image, while an extant-but-uninspectable one must still fail closed.
            matching="$(docker ps -aq --no-trunc --filter "id=$container")" || {
                echo "Could not re-enumerate container '$container'; refusing image retention." >&2
                exit 1
            }
            if printf '%s\n' "$matching" | grep -Fxq "$container"; then
                echo "Could not inspect container '$container'; refusing image retention." >&2
                exit 1
            fi
            echo "SKIP vanished container $container"
            continue
        fi
        printf '%s\n' "$image_id" >> "$PROTECTED"
    done <<EOF
$CONTAINERS
EOF
}

refresh_protected_images

echo ">>> Docker image retention: dryRun=$DRY_RUN keep=$KEEP"
echo ">>> Protected image IDs referenced by active or stopped containers:"
sed 's/^/    /' "$PROTECTED"

for repository in "$@"; do
    case "$repository" in
        -*) echo "Managed repository must not start with '-': '$repository'" >&2; exit 2 ;;
        ''|*[!a-zA-Z0-9._/-]*) echo "Unsafe managed repository '$repository'" >&2; exit 2 ;;
    esac

    echo ">>> Inventory before retention for $repository"
    docker image ls "$repository" --no-trunc --format '{{.ID}}|{{.Repository}}:{{.Tag}}|{{.CreatedAt}}'
    seen="|"
    retained=0
    docker image ls "$repository" --no-trunc --format '{{.ID}}|{{.Repository}}:{{.Tag}}' | while IFS='|' read -r image_id tag; do
        [ -n "$image_id" ] || continue
        [ "$tag" != "$repository:<none>" ] || continue
        case "$seen" in *"|$image_id|"*) continue ;; esac
        seen="${seen}${image_id}|"

        if grep -Fxq "$image_id" "$PROTECTED"; then
            retained=$((retained + 1))
            echo "KEEP protected $tag $image_id"
            continue
        fi
        if [ "$retained" -lt "$KEEP" ]; then
            retained=$((retained + 1))
            echo "KEEP recent $tag $image_id"
            continue
        fi
        if [ "$DRY_RUN" = true ]; then
            echo "WOULD_REMOVE $tag $image_id"
        else
            # Re-resolve both the tag and every container immediately before mutation. A
            # deployment may have retagged or started a container since the inventory above.
            current_id="$(docker image inspect --format '{{.Id}}' "$tag")" || {
                echo "Could not re-inspect '$tag'; refusing image retention." >&2
                exit 1
            }
            [ "$current_id" = "$image_id" ] || {
                echo "Tag '$tag' changed from $image_id to $current_id; refusing stale deletion." >&2
                exit 1
            }
            refresh_protected_images
            if grep -Fxq "$current_id" "$PROTECTED"; then
                echo "KEEP newly-protected $tag $current_id"
                continue
            fi
            echo "REMOVE $tag $image_id"
            docker image rm "$tag"
        fi
    done
    echo ">>> Inventory after retention for $repository"
    docker image ls "$repository" --no-trunc --format '{{.ID}}|{{.Repository}}:{{.Tag}}|{{.CreatedAt}}'
done
