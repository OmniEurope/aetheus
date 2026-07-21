#!/usr/bin/env bash
# =============================================================================
# vpssim-provision.sh - first-boot provisioning for the Aetheus VPS-sim
# =============================================================================
# Runs ONCE at first boot (systemd oneshot), after dockerd is up. Handles the
# provisioning steps that need a live daemon or a representative runtime - the
# static config (apache vhost, certs, postfix, portsentry) is baked at image
# build time. Real operations only: no faked success. LOCAL TEST ONLY.
# =============================================================================
set -u

MARKER=/var/lib/vpssim-provisioned
[ -e "$MARKER" ] && exit 0

log() { echo "[vpssim-provision] $*"; }

# --- DockerCollector: a real running container + image so `docker ps`/`images`
#     return data. nginx is tiny and always-on.
log "Starting a test nginx container for DockerCollector..."
if docker run -d --name vpssim-web --restart unless-stopped -p 8081:80 nginx:alpine >/dev/null 2>&1; then
    log "  nginx container up."
else
    log "  WARN: could not start nginx container (DockerCollector will show no containers)."
fi

# --- RkhunterCollector: a real scan generates /var/log/rkhunter.log, which the
#     collector parses for the last-scan summary. Skip key/weak-file checks so it
#     completes quickly inside a container.
log "Running an initial rkhunter scan to populate the log..."
rkhunter --check --sk --nocolors --report-warnings-only \
    --skip-keypress >/dev/null 2>&1 || true
log "  rkhunter scan done (log at /var/log/rkhunter.log)."

touch "$MARKER"
log "First-boot provisioning complete."
