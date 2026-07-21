#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# SPDX-FileCopyrightText: 2026 Aetheus contributors
set -e

# =============================================================================
# install-agent-linux.sh - Aetheus Agent Installation Script
# =============================================================================
# Run this script from inside the extracted agent directory
# (e.g. /opt/aetheus-agent/install-agent-linux.sh).
#
# Modes (mutually exclusive):
#   (default)   Clean install: prompts for URL/token/name, wipes any previous
#               install, writes fresh config + service, starts and health-checks.
#   --upgrade   Binaries-only refresh: preserves appsettings.json and work dir.
#               Must be run from a DIFFERENT directory than INSTALL_DIR
#               (extract the new tarball to /tmp first, then run from there).
#   --purge     Full uninstall: stop service, remove unit, sudoers, install and
#               work dirs, delete the system user. Prompts for confirmation
#               unless --yes is given.
#
# Usage:
#   sudo sh install-agent-linux.sh
#   sudo sh install-agent-linux.sh --server-url URL --token TOKEN [--name NAME]
#   sudo sh install-agent-linux.sh --upgrade
#   sudo sh install-agent-linux.sh --purge [--yes]
# =============================================================================

AGENT_USER="aetheus-agent"
AGENT_GROUP="aetheus-agent"
INSTALL_DIR="/opt/aetheus-agent"
WORK_DIR="/var/lib/aetheus-agent"
SERVICE_NAME="aetheus-agent"
SUDOERS_FILE="/etc/sudoers.d/aetheus-agent"
# Item #6 - controlled sudo escalation for Apache management. Separate file so the broader
# service-control sudoers can stay untouched on a routine flag change.
APACHE_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-apache"
CERTBOT_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-certbot"
# Items #10.2/#10.3 - same recipe as Apache for RKHunter scan/update/propupd.
RKHUNTER_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-rkhunter"
# Phase 3 - cron management (sudo helper), portsentry IP unblock (sudo helper), and the
# fixed-unit service-enable allow-list. The two helpers are root-owned, agent-non-writable,
# input-validating binaries the agent invokes via NOPASSWD sudoers (the helper is the boundary).
CRON_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-cron"
PORTSENTRY_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-portsentry"
SERVICE_ENABLE_SUDOERS_FILE="/etc/sudoers.d/aetheus-service-enable"
# S-FEAT-W8KN - argv-exact apt-get install/remove allow-list (managed-package install/uninstall).
PACKAGE_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-package"
# PLAN-006 4.1 - argv-exact apt-get upgrade grant (fleet OS patching / patch-manage capability).
PATCH_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-patch"
# PLAN-006 4.2 - path-only grant for the root-owned firewall (ufw) helper (firewall-manage capability).
FIREWALL_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-firewall"
FIREWALL_HELPER_PATH="/usr/local/lib/aetheus/aetheus-firewall"
# S-FEAT-W8KN - argv-exact grant for the root-owned mail-setup helper (full postfix/dovecot/opendkim
# install+configure). Same root-owned-helper-boundary recipe as cron.
MAIL_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-mail"
AETHEUS_HELPER_DIR="/usr/local/lib/aetheus"
CRON_HELPER_PATH="$AETHEUS_HELPER_DIR/cron-apply"
UNBLOCK_HELPER_PATH="$AETHEUS_HELPER_DIR/unblock-ip"
# Root-owned helper for the typed PortsentrySetup op (apt-install portsentry + write mode/port lists into
# the config + enable the service). Same root-owned-helper-boundary recipe as unblock-ip; granted in the
# SAME aetheus-portsentry sudoers file via its own Cmnd_Alias.
PORTSENTRY_SETUP_HELPER_PATH="$AETHEUS_HELPER_DIR/portsentry-setup"
MAIL_SETUP_HELPER_PATH="$AETHEUS_HELPER_DIR/mail-setup"
# S-FEAT-W8KN (incremental) - root-owned helper for add-domain / add-account / add-alias / dkim-rotate
# on an already-provisioned mail stack. Same boundary recipe as mail-setup; granted in the SAME
# aetheus-mail sudoers file via its own Cmnd_Alias.
MAIL_MANAGE_HELPER_PATH="$AETHEUS_HELPER_DIR/mail-manage"
# argv-exact grant for the root-owned teamspeak-setup helper (full TS3 server install: user, tarball,
# systemd unit, credential extraction). Same root-owned-helper-boundary recipe as mail-setup.
TEAMSPEAK_SETUP_SUDOERS_FILE="/etc/sudoers.d/aetheus-teamspeak"
TEAMSPEAK_SETUP_HELPER_PATH="$AETHEUS_HELPER_DIR/teamspeak-setup"
# Cross-agent deployment module. Same root-owned-helper-boundary recipe as cron: the deploy-restart
# helper re-validates <app> and can ONLY restart aetheus-app@<app>.service. The unit TEMPLATE is
# posed at install (never generated from a pipeline YAML - anti-injection), and the per-app deploy
# tree lives under $WORK_DIR/deploy/<app>/{releases,current} owned by the agent (it flips 'current').
DEPLOY_MANAGE_SUDOERS_FILE="/etc/sudoers.d/aetheus-deploy"
DEPLOY_RESTART_HELPER_PATH="$AETHEUS_HELPER_DIR/deploy-restart"
# Certbot management. certbot is a GTFOBins root primitive with domain-variable argv, so it can't be
# argv-exact granted: instead a root-owned helper re-validates the domain and performs real webroot
# ACME issuance. ACME failure is fatal; it never manufactures a self-signed success.
CERTBOT_ISSUE_HELPER_PATH="$AETHEUS_HELPER_DIR/aetheus-certbot-issue"
# Certbot lifecycle management (renew/delete/revoke a named lineage). Same boundary model as the issue
# helper: root-owned, re-validates the cert name, runs a fixed certbot verb (no free-form argv).
CERTBOT_MANAGE_HELPER_PATH="$AETHEUS_HELPER_DIR/aetheus-certbot-manage"
DEPLOY_UNIT_TEMPLATE_PATH="/etc/systemd/system/aetheus-app@.service"
DEPLOY_BASE_DIR="$WORK_DIR/deploy"
# Static web root the deployment module makes agent-writable (via ACL) so a pipeline can publish a
# static site (e.g. the vitrine) into it, served by Apache. Default /var/www; override with --web-root.
DEPLOY_WEB_ROOT="/var/www"
# S-FEAT-DPU2: dedicated read-only group that owns the deploy trees. The transient per-app DynamicUser
# joins it (SupplementaryGroups) to read its secret-bearing payload (mode 0750, never world) without
# joining the agent's own group - so a deployed app shares no surface with the agent.
DEPLOY_READ_GROUP="aetheus-deploy"
SYSTEMD_UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
DEFAULT_SERVER_URL="https://aetheus.example.com"

# If the wizard command created a source URL marker, use it as prompt default.
SCRIPT_DIR_PREVIEW="$(cd "$(dirname "$0")" && pwd)"
URL_MARKER_PATH="$SCRIPT_DIR_PREVIEW/.aetheus-server-url"
if [ -f "$URL_MARKER_PATH" ]; then
    URL_FROM_MARKER="$(head -n 1 "$URL_MARKER_PATH" | tr -d '\r')"
    if [ -n "$URL_FROM_MARKER" ]; then
        DEFAULT_SERVER_URL="$URL_FROM_MARKER"
    fi
fi

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info()  { printf "${GREEN}[INFO]${NC} %s\n" "$1"; }
log_warn()  { printf "${YELLOW}[WARN]${NC} %s\n" "$1"; }
log_error() { printf "${RED}[ERROR]${NC} %s\n" "$1"; }

show_help() {
    cat <<EOF
Usage: sudo sh install-agent-linux.sh [OPTIONS]

Modes (mutually exclusive):
  (none)                    Clean install (default)
  --upgrade                 Replace binaries + service only; preserve config and data
  --purge                   Full uninstall (service, files, user)

Install options:
  --server-url URL          Backend API URL (e.g. https://aetheus-api.example.com)
  --token TOKEN             One-time registration token from the web UI
  --name NAME               Display name for this agent (default: hostname)

Capability modules:
  --module pipeline-runner  (DEFAULT ON) Install the CI/CD toolchain: .NET SDK, git,
                            sshpass, acl. No elevation - pipeline shell runs as the
                            unprivileged agent user.
  --no-pipeline-runner      Skip the pipeline-runner toolchain install.
  --module server-management
                            Opt-in. Enable all server-administration capabilities
                            (service-control, Apache, Certbot, RKHunter, Docker,
                            TeamSpeak, cron-manage, portsentry-manage, service-enable)
                            - shorthand for the matching --enable-* flags below.
  --module deployment       Opt-in (independent of the two above). Make this host a
                            cross-agent deploy target: installs the aetheus-app@.service
                            unit template, the root-owned deploy-restart helper + its
                            argv-exact sudoers grant, and the agent-owned deploy tree.
                            Requires a sudo grant, so forces NoNewPrivileges=false.
  --web-root PATH           (deployment module) Static web root made agent-writable
                            (ACL) so a pipeline can publish a static site there for
                            Apache to serve. Default: /var/www.

Elevation options (ALL DISABLED BY DEFAULT - opt in with --enable-* or --module server-management):
  --enable-service-control  Allow the agent to start/stop/restart a fixed set of
                            managed services via a narrow NOPASSWD sudoers rule.
                            This is the ONLY option that requires sudo and forces
                            NoNewPrivileges=false (weaker systemd sandbox).
  --enable-apache-introspection
                            Grant the agent READ access (ACL, no sudo) to
                            /etc/apache2 + /etc/httpd so it can enumerate vhosts.
  --enable-certbot-introspection
                            Grant the agent READ access (ACL, no sudo) to
                            /etc/letsencrypt so it can read certificate expiry.
  --enable-apache-manage    Item #6: WRITE access on Apache sites-available + a
                            tight NOPASSWD sudoers rule for apache2ctl
                            configtest / graceful / systemctl reload apache2.
                            Strict allow-list (no wildcards), noexec, no sudo
                            timeout caching, env_reset, secure_path. Required
                            for the "Test config" / "Edit config" / "Reload"
                            buttons on the Apache section to work.
  --enable-certbot-manage   Provision HTTPS issuance: a root-owned certbot-issue
                            helper (real ACME via a non-disruptive webroot;
                            issuance failure stays a hard failure) plus a
                            root-owned certbot-manage helper
                            (renew/delete/revoke a named lineage) + a NOPASSWD
                            sudoers rule scoped to both. Implies
                            --enable-apache-manage. Required for the pipeline
                            'type: certbot' step and the Certbot section's
                            create/renew/delete/revoke buttons.
  --enable-rkhunter-manage  Items #10.2/#10.3: tight NOPASSWD sudoers rule for
                            rkhunter --check / --update / --propupd (no other
                            flags allowed). Same hardened recipe as
                            apache-manage. Required for the "Run Scan" button
                            on the RKHunter section to work.
  --enable-docker           Add the agent to the 'docker' group for container
                            collection (no sudo). Note: docker group ~= root.
  --enable-teamspeak        Provision TeamSpeak ServerQuery access: add the agent
                            to the 'teamspeak' group (read ts3server.ini for a
                            non-default query port; no sudo) and scaffold the
                            credentials dir. Detection itself works without this
                            (pidof + default port 10011); enable to manage TS via
                            the dashboard. Drop the serveradmin ServerQuery
                            password into INSTALL_DIR/teamspeak/query-credentials.
  --enable-cron-manage      Phase 3: deploy the root-owned cron-apply helper + a
                            NOPASSWD sudoers grant for it. Lets the agent write cron
                            jobs (the helper writes /etc/cron.d/aetheus-<id> only;
                            it re-validates every field). Required for the Cron
                            section's save/delete buttons to work.
  --enable-portsentry-manage
                            Phase 3: deploy the root-owned unblock-ip + portsentry-setup
                            helpers + a NOPASSWD sudoers grant for them. Lets the agent
                            unblock an IP (removes it from iptables/ip6tables +
                            /etc/hosts.deny) and install/configure portsentry. Required
                            for the Portsentry "Unblock" and "Setup" actions to work.
  --enable-service-enable   Phase 3: tight NOPASSWD sudoers rule for
                            'systemctl enable --now <unit>' over a FIXED list of
                            pre-existing units (no wildcards). Required for the server
                            Configuration YAML deploy to enable services on boot.
  --enable-package-manage   S-FEAT-W8KN: tight NOPASSWD sudoers rule for
                            'apt-get install|remove -y <pkg>' over a FIXED package list
                            (no wildcards). Required to install/uninstall managed server
                            components (docker/apache/mail/...) from Aetheus.
  --enable-patch-manage     PLAN-006 4.1: tight NOPASSWD sudoers rule for
                            'apt-get upgrade -y' (argv-exact, no wildcards). Lets the fleet
                            APPLY pending OS updates; the agent runs a dry-run first and
                            aborts on any critical package. Pending-update visibility does
                            not need this grant (the dry-run probe is unprivileged).
  --enable-firewall-manage  PLAN-006 4.2: deploy the root-owned aetheus-firewall helper +
                            a path-only NOPASSWD sudoers grant for it. Lets the agent control
                            ufw (open/close ports, enable/disable); the helper re-validates
                            every argument and enforces anti-lockout (never closes the SSH
                            port; auto-allows it before enabling). ufw status needs root, so
                            firewall visibility also depends on this grant.
  --enable-mail-setup       S-FEAT-W8KN: deploy the root-owned mail-setup helper + a
                            NOPASSWD sudoers grant for it. Lets the agent run the full
                            mail-stack setup (install + configure postfix/dovecot/opendkim
                            + enable services); the helper re-validates every argument.
                            Required for the Mail section's "Setup" action to work.
  --enable-teamspeak-setup  Deploy the root-owned teamspeak-setup helper + a NOPASSWD
                            sudoers grant for it. Lets the agent install a full TeamSpeak 3
                            server (system user, tarball, systemd unit, ports); the helper
                            re-validates every argument. Required for the TeamSpeak section's
                            "Install" action (also implies --enable-teamspeak).
  --enable-all              Enable everything (maximal capabilities).

Disable options (use on --upgrade to revoke previously-enabled capabilities):
  --disable-service-control       Remove sudoers rule for service start/stop/restart.
  --disable-apache-introspection  Remove Apache read ACL.
  --disable-apache-manage         Remove Apache write/reload privileges.
  --disable-rkhunter-manage       Remove rkhunter scan/update/propupd privileges.
  --disable-certbot-introspection Remove Certbot read ACL.
  --disable-docker                Don't add agent to docker group.
  --disable-teamspeak             Don't add agent to teamspeak group / provision TS.
  --disable-cron-manage           Remove cron-apply helper + sudoers.
  --disable-portsentry-manage     Remove unblock-ip helper + sudoers.
  --disable-service-enable        Remove the fixed-unit service-enable sudoers.
  --disable-package-manage        Remove the fixed-package apt-get sudoers.
  --disable-mail-setup            Remove mail-setup helper + sudoers.
  --disable-all                   Disable everything (fully sandboxed - the default).

  By default NO elevation is enabled (secure-by-default, per ADR-004).
  No flags => no sudoers, NoNewPrivileges=true.
  On --upgrade the previously-configured elevation posture is preserved automatically.

Other:
  --allow-insecure-certs    DEV ONLY: skip TLS certificate validation (backend API
                            + pipeline git clones). Auto-enabled when the server
                            URL is localhost. Never use in production.
  -y, --yes                 Skip confirmation prompts: auto-install missing dependencies
                            (git/sshpass/acl) without asking, and skip the --purge prompt.
  -h, --help                Show this help

The default and --upgrade modes run a one-shot runtime probe ("can the agent
itself reach the backend over the .NET HTTP stack?") right before starting
the service. This catches TLS / DNS / proxy issues invisible to curl that
would otherwise eat the 120 s systemd start timeout.
EOF
}

# Handle --help / -h before anything else so it works without root.
for _arg in "$@"; do
    case "$_arg" in
        -h|--help) show_help; exit 0 ;;
    esac
done

# --- Check root ---
if [ "$(id -u)" -ne 0 ]; then
    log_error "This script must be run as root (sudo)"
    exit 1
fi

# --- Parse arguments ---
MODE="install"
SERVER_URL=""
REG_TOKEN=""
AGENT_NAME=""
ASSUME_YES=0
ALLOW_INSECURE_CERTS=0     # dev only: TLS bypass for backend API + pipeline git clones

# Elevation posture - all DISABLED by default (secure-by-default per ADR-004).
# Use --enable-* flags to opt in. On --upgrade these are re-derived from the existing install.
ENABLE_SERVICE_CONTROL=0   # opt-in: sudo for service start/stop/restart
ENABLE_APACHE=0            # opt-in: read ACL on /etc/apache2
ENABLE_APACHE_MANAGE=0     # opt-in: write ACL on sites-available + tight Apache sudoers
ENABLE_RKHUNTER_MANAGE=0   # opt-in: tight rkhunter sudoers (scan/update/propupd)
ENABLE_CERTBOT=0           # opt-in: read ACL on /etc/letsencrypt
ENABLE_CERTBOT_MANAGE=0    # opt-in: root-owned webroot ACME helper + NOPASSWD sudoers
ENABLE_DOCKER=0            # opt-in: docker-group membership (~root)
ENABLE_TEAMSPEAK=0         # opt-in: teamspeak-group membership + credential dir (no sudo)
ENABLE_CRON_MANAGE=0       # opt-in: cron-apply sudo helper (writes /etc/cron.d/aetheus-<id>)
ENABLE_PORTSENTRY_MANAGE=0 # opt-in: unblock-ip sudo helper (iptables/ip6tables + hosts.deny)
ENABLE_SERVICE_ENABLE=0    # opt-in: systemctl enable --now on a FIXED unit list (argv-exact)
ENABLE_PACKAGE_MANAGE=0    # opt-in: apt-get install/remove -y on a FIXED package list (argv-exact)
ENABLE_PATCH_MANAGE=0      # opt-in: apt-get upgrade -y (fleet OS patching, argv-exact) - PLAN-006 4.1
ENABLE_FIREWALL_MANAGE=0   # opt-in: ufw control via root-owned helper (firewall) - PLAN-006 4.2
ENABLE_MAIL_SETUP=0        # opt-in: root-owned mail-setup helper (full postfix/dovecot/opendkim setup)
ENABLE_TEAMSPEAK_SETUP=0   # opt-in: root-owned teamspeak-setup helper (full TS3 server install)
POSTURE_EXPLICIT=0         # set if any --enable-* flag was passed explicitly

# Capability modules (higher-level than the granular --enable-* flags below).
#   pipeline-runner    : ON by default. Installs the toolchain a CI/CD runner needs
#                        (dotnet SDK, git, sshpass) + the acl package. NO elevation -
#                        pipeline shell runs as the unprivileged agent user.
#   server-management  : opt-in. Turns on the server-administration capabilities
#                        (service-control, Apache, Certbot, RKHunter, Docker,
#                        TeamSpeak) by expanding to the matching --enable-* flags below.
# The zero-elevation-by-default invariant (ADR-004) is preserved: pipeline-runner
# grants no sudoers/group/root, and server-management must be requested explicitly.
MODULE_PIPELINE_RUNNER=1
MODULE_SERVER_MANAGEMENT=0
#   deployment         : opt-in, independent of the two above. Makes this host a cross-agent deploy
#                        target - installs the aetheus-app@.service unit template, the root-owned
#                        deploy-restart helper + its argv-exact sudoers grant, and the agent-owned
#                        $WORK_DIR/deploy tree. A sudo grant is in play, so it relaxes NoNewPrivileges
#                        (like service-control/package-manage/mail-setup).
MODULE_DEPLOYMENT=0
ENABLE_DEPLOYMENT=0        # derived from MODULE_DEPLOYMENT (and preserved on --upgrade)
# Exact SDK required by global.json. The runtime remains channel-compatible, while
# pipeline builds require this feature band because rollForward is disabled.
DOTNET_SDK_VERSION="10.0.202"
DOTNET_CHANNEL="10.0"
DOTNET_RUNTIME_VERSION="10.0.10"
DOTNET_INSTALLER_URL="https://raw.githubusercontent.com/dotnet/install-scripts/da3ce11ba63f3dbb0fb835d41bda2665d5c48e84/src/dotnet-install.sh"
DOTNET_INSTALLER_SHA256="082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e"

while [ "$#" -gt 0 ]; do
    case "$1" in
        --upgrade)    MODE="upgrade"; shift ;;
        --purge)      MODE="purge"; shift ;;
        --server-url) SERVER_URL="$2"; shift 2 ;;
        --token)      REG_TOKEN="$2"; shift 2 ;;
        --name)       AGENT_NAME="$2"; shift 2 ;;
        --enable-service-control)       ENABLE_SERVICE_CONTROL=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-apache-introspection)  ENABLE_APACHE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-apache-manage)         ENABLE_APACHE_MANAGE=1; ENABLE_APACHE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-rkhunter-manage)       ENABLE_RKHUNTER_MANAGE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-certbot-introspection) ENABLE_CERTBOT=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-certbot-manage)        ENABLE_CERTBOT_MANAGE=1; ENABLE_CERTBOT=1; ENABLE_APACHE_MANAGE=1; ENABLE_APACHE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-docker)                ENABLE_DOCKER=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-teamspeak)             ENABLE_TEAMSPEAK=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-cron-manage)           ENABLE_CRON_MANAGE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-portsentry-manage)     ENABLE_PORTSENTRY_MANAGE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-service-enable)        ENABLE_SERVICE_ENABLE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-package-manage)        ENABLE_PACKAGE_MANAGE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-patch-manage)          ENABLE_PATCH_MANAGE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-firewall-manage)       ENABLE_FIREWALL_MANAGE=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-mail-setup)            ENABLE_MAIL_SETUP=1; POSTURE_EXPLICIT=1; shift ;;
        --enable-teamspeak-setup)       ENABLE_TEAMSPEAK_SETUP=1; ENABLE_TEAMSPEAK=1; POSTURE_EXPLICIT=1; shift ;;
        --disable-service-control)       ENABLE_SERVICE_CONTROL=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-apache-introspection)  ENABLE_APACHE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-apache-manage)         ENABLE_APACHE_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-rkhunter-manage)       ENABLE_RKHUNTER_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-certbot-introspection) ENABLE_CERTBOT=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-certbot-manage)        ENABLE_CERTBOT_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-docker)                ENABLE_DOCKER=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-teamspeak)             ENABLE_TEAMSPEAK=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-cron-manage)           ENABLE_CRON_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-portsentry-manage)     ENABLE_PORTSENTRY_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-service-enable)        ENABLE_SERVICE_ENABLE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-package-manage)        ENABLE_PACKAGE_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-patch-manage)          ENABLE_PATCH_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-firewall-manage)       ENABLE_FIREWALL_MANAGE=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-mail-setup)            ENABLE_MAIL_SETUP=0; POSTURE_EXPLICIT=1; shift ;;
        --disable-teamspeak-setup)       ENABLE_TEAMSPEAK_SETUP=0; POSTURE_EXPLICIT=1; shift ;;
        --enable-all)                    ENABLE_SERVICE_CONTROL=1; ENABLE_APACHE=1; ENABLE_APACHE_MANAGE=1; ENABLE_RKHUNTER_MANAGE=1; ENABLE_CERTBOT=1; ENABLE_CERTBOT_MANAGE=1; ENABLE_DOCKER=1; ENABLE_TEAMSPEAK=1; ENABLE_CRON_MANAGE=1; ENABLE_PORTSENTRY_MANAGE=1; ENABLE_SERVICE_ENABLE=1; ENABLE_PACKAGE_MANAGE=1; ENABLE_PATCH_MANAGE=1; ENABLE_FIREWALL_MANAGE=1; ENABLE_MAIL_SETUP=1; ENABLE_TEAMSPEAK_SETUP=1; POSTURE_EXPLICIT=1; shift ;;
        --disable-all)                   ENABLE_SERVICE_CONTROL=0; ENABLE_APACHE=0; ENABLE_APACHE_MANAGE=0; ENABLE_RKHUNTER_MANAGE=0; ENABLE_CERTBOT=0; ENABLE_CERTBOT_MANAGE=0; ENABLE_DOCKER=0; ENABLE_TEAMSPEAK=0; ENABLE_CRON_MANAGE=0; ENABLE_PORTSENTRY_MANAGE=0; ENABLE_SERVICE_ENABLE=0; ENABLE_PACKAGE_MANAGE=0; ENABLE_PATCH_MANAGE=0; ENABLE_FIREWALL_MANAGE=0; ENABLE_MAIL_SETUP=0; ENABLE_TEAMSPEAK_SETUP=0; POSTURE_EXPLICIT=1; shift ;;
        --allow-insecure-certs) ALLOW_INSECURE_CERTS=1; shift ;;
        --module)
            case "$2" in
                pipeline-runner)     MODULE_PIPELINE_RUNNER=1 ;;
                no-pipeline-runner)  MODULE_PIPELINE_RUNNER=0 ;;
                server-management)   MODULE_SERVER_MANAGEMENT=1 ;;
                deployment)          MODULE_DEPLOYMENT=1; POSTURE_EXPLICIT=1 ;;
                *) log_warn "Unknown module '$2' (expected: pipeline-runner | server-management | deployment)" ;;
            esac
            shift 2 ;;
        --web-root)   DEPLOY_WEB_ROOT="$2"; shift 2 ;;
        --no-pipeline-runner)   MODULE_PIPELINE_RUNNER=0; shift ;;
        -y|--yes)     ASSUME_YES=1; shift ;;
        -h|--help)    show_help; exit 0 ;;
        *) log_warn "Ignoring unknown argument: $1"; shift ;;
    esac
done

# Expand the server-management module to the granular elevation flags it implies.
# (--enable-* flags can still be used on their own for fine-grained control.)
if [ "$MODULE_SERVER_MANAGEMENT" -eq 1 ]; then
    ENABLE_SERVICE_CONTROL=1; ENABLE_APACHE=1; ENABLE_APACHE_MANAGE=1
    ENABLE_RKHUNTER_MANAGE=1; ENABLE_CERTBOT=1; ENABLE_DOCKER=1; ENABLE_TEAMSPEAK=1
    ENABLE_CRON_MANAGE=1; ENABLE_PORTSENTRY_MANAGE=1; ENABLE_SERVICE_ENABLE=1
    ENABLE_PACKAGE_MANAGE=1; ENABLE_MAIL_SETUP=1; ENABLE_TEAMSPEAK_SETUP=1; POSTURE_EXPLICIT=1
fi

# The deployment module is independent (not part of server-management): it provisions the deploy
# target capability. Modelled as ENABLE_DEPLOYMENT so it slots into the same provisioning/teardown,
# posture-summary, NoNewPrivileges and upgrade-preservation machinery as the other capabilities.
if [ "$MODULE_DEPLOYMENT" -eq 1 ]; then
    ENABLE_DEPLOYMENT=1
fi

# =============================================================================
# Helper functions
# =============================================================================

# Stop and disable the service if it exists; returns 0 always (idempotent).
stop_and_disable_service() {
    if systemctl list-unit-files 2>/dev/null | grep -q "^${SERVICE_NAME}\.service"; then
        if systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
            log_info "  Stopping ${SERVICE_NAME}..."
            systemctl stop "$SERVICE_NAME" || log_warn "  Failed to stop ${SERVICE_NAME}"
        fi
        if systemctl is-enabled --quiet "$SERVICE_NAME" 2>/dev/null; then
            log_info "  Disabling ${SERVICE_NAME}..."
            systemctl disable "$SERVICE_NAME" 2>/dev/null || log_warn "  Failed to disable ${SERVICE_NAME}"
        fi
    fi
}

# Remove the systemd unit file and reload daemon; returns 0 always.
remove_systemd_unit() {
    if [ -f "$SYSTEMD_UNIT_PATH" ]; then
        log_info "  Removing systemd unit..."
        rm -f "$SYSTEMD_UNIT_PATH"
        systemctl daemon-reload
        systemctl reset-failed "$SERVICE_NAME" 2>/dev/null || true
    fi
}

ensure_docker_group() {
    # Opt-in only (--enable-docker). Docker-group membership is ~= root, so it is
    # not granted by default. DockerCollector needs it for `docker ps -a` etc.
    if [ "$ENABLE_DOCKER" -ne 1 ]; then
        return 0
    fi
    if ! getent group docker >/dev/null 2>&1; then
        log_info "  Docker group not found - skipping (install Docker to enable container collection)"
        return 0
    fi
    if id -nG "$AGENT_USER" 2>/dev/null | tr ' ' '\n' | grep -qx docker; then
        log_info "  $AGENT_USER already in docker group"
    else
        log_info "  Adding $AGENT_USER to docker group..."
        usermod -aG docker "$AGENT_USER" || log_warn "  Failed to add to docker group (non-fatal)"
    fi
    # Container deploys (pipeline `type: deploy` with a compose file → `docker compose up -d --wait`)
    # need the Compose v2 plugin. The Ubuntu docker.io package has no compose plugin; Ubuntu ships it
    # separately as docker-compose-v2. Best-effort + idempotent (only when `docker compose` is missing).
    if ! docker compose version >/dev/null 2>&1; then
        log_info "  Installing Docker Compose v2 plugin (docker-compose-v2) for container deploys..."
        install_package docker-compose-v2 || log_warn "  Could not install docker-compose-v2 - container deploys (compose) will not work until it is present."
    fi
}

ensure_teamspeak_access() {
    # Opt-in only (--enable-teamspeak / --module server-management). Grants no sudo.
    # Detection of a running TeamSpeak works WITHOUT this (pidof + default query port 10011);
    # this only (a) lets the collector read ts3server.ini for a NON-default query port by joining
    # the 'teamspeak' group, and (b) scaffolds the agent-local credentials dir so the operator can
    # drop the serveradmin ServerQuery password the typed ServerQuery operation (S-TECH-87) needs.
    if [ "$ENABLE_TEAMSPEAK" -ne 1 ]; then
        return 0
    fi

    if getent group teamspeak >/dev/null 2>&1; then
        if id -nG "$AGENT_USER" 2>/dev/null | tr ' ' '\n' | grep -qx teamspeak; then
            log_info "  $AGENT_USER already in teamspeak group"
        else
            log_info "  Adding $AGENT_USER to teamspeak group (read ts3server.ini for query port)..."
            usermod -aG teamspeak "$AGENT_USER" || log_warn "  Failed to add to teamspeak group (non-fatal)"
        fi
    else
        log_info "  No 'teamspeak' group found - skipping group membership (default port 10011 still works)"
    fi

    # Scaffold the agent-local credentials dir (inside INSTALL_DIR, owned by the agent, 0700).
    _ts_dir="$INSTALL_DIR/teamspeak"
    _ts_cred="$_ts_dir/query-credentials"
    mkdir -p "$_ts_dir"
    chown "$AGENT_USER:$AGENT_GROUP" "$_ts_dir" 2>/dev/null || true
    chmod 700 "$_ts_dir" 2>/dev/null || true
    if [ -s "$_ts_cred" ]; then
        chown "$AGENT_USER:$AGENT_GROUP" "$_ts_cred" 2>/dev/null || true
        chmod 600 "$_ts_cred" 2>/dev/null || true
        log_info "  TeamSpeak query credential present."
    else
        log_warn "  TeamSpeak ServerQuery credential missing - drop the serveradmin password into:"
        log_warn "    $_ts_cred   (then: chown $AGENT_USER $_ts_cred; chmod 600 it)"
        log_warn "  Until then, TeamSpeak ServerQuery actions will report a missing-credential error."
    fi
}

# Minimal JSON string escaper (backslash + double-quote) for values written into
# appsettings.json. Defeats config-structure injection from operator-supplied
# URL/token/name containing " or \.
json_escape() {
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

# Drives the "Last update" field shown on the agent detail page. Stamped on every fresh
# install AND every --upgrade so the UI always reflects the most recent binary swap.
# Format: ISO-8601 UTC ("2026-05-21T14:33:07Z"). Owned by $AGENT_USER (the agent reads it
# at startup), mode 644 so the marker is readable even if the agent is later moved to a
# different account. Safe to call when $WORK_DIR doesn't exist yet - we create it.
write_installed_at_marker() {
    _iat_path="$WORK_DIR/.installed-at"
    mkdir -p "$WORK_DIR"
    date -u +"%Y-%m-%dT%H:%M:%SZ" > "$_iat_path"
    if id "$AGENT_USER" >/dev/null 2>&1; then
        chown "$AGENT_USER:$AGENT_GROUP" "$_iat_path" 2>/dev/null || true
    fi
    chmod 644 "$_iat_path" 2>/dev/null || true
    log_info "  Recorded install timestamp: $(cat "$_iat_path")"
}

# Refresh the apt package index exactly once per run. A freshly-imaged box (the blank
# VPS-sim, or a cloud image whose /var/lib/apt/lists was never populated) ships no package
# index, so `apt-get install` would fail to locate git/sshpass/acl/dotnet. Best-effort: a
# failed refresh falls back to whatever cached index exists (a warning, not fatal).
# Set to 1 by grant_sudoers_read_acls when a hash-derived capability was requested but setfacl
# (the `acl` package) could not be made available, meaning the sudo grant IS installed but the
# non-root agent cannot read the drop-in to hash it, so the capability reports OFF in the UI.
# Surfaced loudly in print_posture_summary so a `-y` operator can't miss it (the exact symptom that
# made "everything enabled" silently yield package-manage = unavailable on the live box).
ACL_GRANT_DEGRADED=0

APT_UPDATED=0
ensure_apt_updated() {
    if [ "$APT_UPDATED" -eq 1 ]; then
        return 0
    fi
    APT_UPDATED=1
    if command -v apt-get >/dev/null 2>&1; then
        log_info "  Refreshing apt package index..."
        apt-get update >/dev/null 2>&1 || log_warn "  apt-get update failed (continuing with cached index)."
    fi
}

# Install a system package by name via the detected package manager.
# Args: $1 = package name. Returns 0 on success, 1 on failure.
install_package() {
    _ip_pkg="$1"
    if command -v apt-get >/dev/null 2>&1; then
        ensure_apt_updated
        apt-get install -y "$_ip_pkg" >/dev/null 2>&1 && log_info "  '$_ip_pkg' installed." && return 0
    elif command -v yum >/dev/null 2>&1; then
        yum install -y "$_ip_pkg" >/dev/null 2>&1 && log_info "  '$_ip_pkg' installed." && return 0
    fi
    log_warn "  Failed to install '$_ip_pkg'."
    return 1
}

# Ask user confirmation and install a package if missing.
# Args: $1 = command to check, $2 = package name, $3 = reason description.
# Respects ASSUME_YES. Returns 0 if command available after, 1 otherwise.
ask_install_package() {
    _aip_cmd="$1"; _aip_pkg="$2"; _aip_reason="$3"
    if command -v "$_aip_cmd" >/dev/null 2>&1; then
        return 0
    fi
    if [ "$ASSUME_YES" -eq 1 ]; then
        log_info "  Installing '$_aip_pkg' ($ASSUME_YES mode)..."
        install_package "$_aip_pkg"
        return $?
    fi
    printf "  '%s' is not installed (%s). Install it? [Y/n] " "$_aip_pkg" "$_aip_reason"
    read _aip_answer
    case "$_aip_answer" in
        [nN]*) log_info "  Skipping '$_aip_pkg'."; return 1 ;;
        *) install_package "$_aip_pkg"; return $? ;;
    esac
}

# Ensure the exact .NET SDK required by global.json is available for the pipeline-runner
# module. Microsoft's official installer adds it side-by-side in /usr/share/dotnet. Best-effort:
# a failure is a warning, not fatal - pipelines that don't build .NET still run.
ensure_dotnet_sdk() {
    if command -v dotnet >/dev/null 2>&1 \
        && dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_SDK_VERSION} "; then
        log_info "  Required .NET SDK ${DOTNET_SDK_VERSION} already present."
        return 0
    fi
    log_info "  Installing exact .NET SDK ${DOTNET_SDK_VERSION} (pipeline-runner module)..."
    if command -v curl >/dev/null 2>&1 \
        && command -v bash >/dev/null 2>&1 \
        && command -v sha256sum >/dev/null 2>&1; then
        _ds_tmp="$(mktemp)"
        if curl -fsSL "$DOTNET_INSTALLER_URL" -o "$_ds_tmp" 2>/dev/null \
            && printf '%s  %s\n' "$DOTNET_INSTALLER_SHA256" "$_ds_tmp" | sha256sum -c - >/dev/null 2>&1; then
            bash "$_ds_tmp" --version "$DOTNET_SDK_VERSION" --install-dir /usr/share/dotnet >/dev/null 2>&1 \
                && ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet 2>/dev/null \
                && dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_SDK_VERSION} " \
                && { log_info "  .NET SDK ${DOTNET_SDK_VERSION} installed via dotnet-install.sh."; rm -f "$_ds_tmp"; return 0; }
        fi
        rm -f "$_ds_tmp"
    fi
    log_warn "  Could not install .NET SDK ${DOTNET_SDK_VERSION} automatically - install that exact SDK if your pipelines build Aetheus."
    return 1
}

# Install the toolchain the pipeline-runner module needs (git, sshpass, dotnet SDK).
# No elevation is granted here - these are ordinary packages the agent invokes as
# its own unprivileged user when executing pipeline steps.
ensure_pipeline_runner_packages() {
    log_info "Pipeline-runner module: ensuring build/deploy toolchain..."
    ask_install_package "git" "git" "required to clone pipeline repositories" || true
    ask_install_package "sshpass" "sshpass" "used by SSH-based pipeline deployments" || true
    # S-FEAT-BLDX: buildx is the modern (non-legacy) docker build backend. Best-effort: on a box without
    # the plugin, docker build still works via the legacy builder, so a missing plugin never blocks install.
    ask_install_package "docker-buildx" "docker-buildx-plugin" "used for BuildKit / multi-platform container builds" || true
    # Best-effort, per ensure_dotnet_sdk's contract: a missing .NET SDK only blocks
    # .NET pipeline builds, not enrollment - never fail the whole install (set -e) over it.
    ensure_dotnet_sdk || true
}

# The deployment module also owns explicit, verified database restores during a release rollback.
# Install client-only tools: no database server or daemon is added to the host. These commands run as
# the unprivileged agent and authenticate with task-scoped protected environment variables.
ensure_backup_restore_packages() {
    log_info "Deployment module: ensuring database backup/restore clients..."
    if command -v apt-get >/dev/null 2>&1; then
        ask_install_package "pg_restore" "postgresql-client" "required for verified PostgreSQL backup restore" || true
        ask_install_package "mysql" "default-mysql-client" "required for verified MySQL backup restore" || true
    elif command -v yum >/dev/null 2>&1; then
        ask_install_package "pg_restore" "postgresql" "required for verified PostgreSQL backup restore" || true
        ask_install_package "mysql" "mysql" "required for verified MySQL backup restore" || true
    fi
}

# Install optional dependencies before the main setup begins.
ensure_optional_packages() {
    if [ "$MODULE_PIPELINE_RUNNER" -eq 1 ]; then
        ensure_pipeline_runner_packages
    fi
    if [ "$ENABLE_DEPLOYMENT" -eq 1 ]; then
        ensure_backup_restore_packages
    fi
    _eop_need_acl=0
    # ACL is needed for the Apache/Certbot read trees AND for granting the agent read access to the
    # sudoers drop-ins it hashes each heartbeat to self-report its capabilities (package-manage,
    # deployment, …). So require it whenever ANY elevation that writes a sudoers drop-in is enabled -
    # otherwise the grant exists but the non-root agent can't read it and the capability stays OFF.
    if [ "$ENABLE_APACHE" -eq 1 ] || [ "$ENABLE_APACHE_MANAGE" -eq 1 ] || [ "$ENABLE_CERTBOT" -eq 1 ] \
        || [ "$ENABLE_SERVICE_CONTROL" -eq 1 ] || [ "$ENABLE_RKHUNTER_MANAGE" -eq 1 ] \
        || [ "$ENABLE_CRON_MANAGE" -eq 1 ] || [ "$ENABLE_PORTSENTRY_MANAGE" -eq 1 ] \
        || [ "$ENABLE_SERVICE_ENABLE" -eq 1 ] || [ "$ENABLE_PACKAGE_MANAGE" -eq 1 ] \
        || [ "$ENABLE_PATCH_MANAGE" -eq 1 ] || [ "$ENABLE_FIREWALL_MANAGE" -eq 1 ] \
        || [ "$ENABLE_MAIL_SETUP" -eq 1 ] || [ "$ENABLE_TEAMSPEAK_SETUP" -eq 1 ] || [ "$ENABLE_DEPLOYMENT" -eq 1 ]; then
        _eop_need_acl=1
    fi
    if [ "$_eop_need_acl" -eq 1 ]; then
        ask_install_package "setfacl" "acl" "required for file ACL management"
        # S-TECH-ACLB: you explicitly requested an elevation that writes a sudoers drop-in (package-manage,
        # deployment, apache/certbot, mail, teamspeak, service-control, ...). Without 'acl' the non-root
        # agent cannot read that drop-in to self-report the capability, so it would silently stay OFF. Fail
        # hard here instead of degrading in silence - the operator asked for the capability, so a missing
        # setfacl is a hard error, not a best-effort skip.
        if ! command -v setfacl >/dev/null 2>&1; then
            log_error "'acl' (setfacl) is required for the elevation you requested but could not be installed. Run 'apt-get install -y acl' then re-run, or drop the elevation flags."
            exit 1
        fi
    fi
}

# Ensure the ICU runtime is present. The self-contained .NET agent does NOT bundle ICU, so a bare
# Ubuntu/Debian image (the blank VPS-sim, or a minimal cloud image) makes the runtime FailFast at
# startup ("Couldn't find a valid ICU package") - which the pre-start probe then misreports as a
# backend-connectivity failure. Mandatory regardless of capability modules: without ICU the agent
# never boots. Best-effort (like ensure_dotnet_sdk); the runtime probe stays the hard gate.
ensure_icu() {
    if command -v ldconfig >/dev/null 2>&1 && ldconfig -p 2>/dev/null | grep -Eq 'libicuuc|libicui18n'; then
        log_info "  ICU runtime already present."
        return 0
    fi
    log_info "Ensuring ICU runtime (required by the .NET agent)..."
    if command -v apt-get >/dev/null 2>&1; then
        ensure_apt_updated
        # Debian/Ubuntu ship no unversioned 'libicu' runtime package - pick the highest libicuNN.
        _icu_pkg="$(apt-cache --names-only search '^libicu[0-9][0-9]*$' 2>/dev/null | awk '{print $1}' | sort -V | tail -n1)"
        if [ -n "$_icu_pkg" ] && apt-get install -y "$_icu_pkg" >/dev/null 2>&1; then
            log_info "  ICU runtime installed ($_icu_pkg)."
            return 0
        fi
    elif command -v yum >/dev/null 2>&1; then
        # RHEL/CentOS/Fedora provide the unversioned 'libicu'.
        if yum install -y libicu >/dev/null 2>&1; then
            log_info "  ICU runtime installed (libicu)."
            return 0
        fi
    fi
    log_warn "  Could not install ICU automatically - the agent may FailFast at startup."
    log_warn "  Install it manually (e.g. 'apt-get install libicu74') or publish with InvariantGlobalization."
    return 1
}

# Grant the agent READ-ONLY access to a config tree via POSIX ACL - no sudo, no
# group, no root execution. Falls back to a clear warning if setfacl is absent.
# Args: $1 = human label, $2.. = directories.
grant_read_acl() {
    _gra_label="$1"; shift
    if ! command -v setfacl >/dev/null 2>&1; then
        log_warn "  setfacl not available - skipping ACL for ${_gra_label}."
        return 0
    fi
    for _gra_dir in "$@"; do
        [ -d "$_gra_dir" ] || continue
        log_info "  Granting $AGENT_USER read access to $_gra_dir (${_gra_label})..."
        # rX = read + dir-traverse only; default ACL so new files inherit it.
        setfacl -R  -m "u:$AGENT_USER:rX" "$_gra_dir" 2>/dev/null \
            || log_warn "  setfacl failed on $_gra_dir (non-fatal)"
        setfacl -R -d -m "u:$AGENT_USER:rX" "$_gra_dir" 2>/dev/null || true
    done
}

apply_read_acls() {
    if [ "$ENABLE_APACHE" -eq 1 ]; then
        grant_read_acl "Apache introspection" /etc/apache2 /etc/httpd
    fi
    if [ "$ENABLE_CERTBOT" -eq 1 ]; then
        grant_read_acl "Certbot introspection" /etc/letsencrypt
    fi
    # The recursive read-only grant above (rX on all of /etc/apache2) would CLOBBER the write ACL
    # write_apache_manage_sudoers set on the two sites dirs down to r-x. Re-assert rwx LAST so the
    # apache-proxy step can still write vhosts + symlink them (apply_read_acls runs after the
    # capability provisioning in the main flow).
    if [ "$ENABLE_APACHE_MANAGE" -eq 1 ] && command -v setfacl >/dev/null 2>&1; then
        for d in /etc/apache2/sites-available /etc/apache2/sites-enabled; do
            if [ -d "$d" ]; then
                setfacl -m "u:$AGENT_USER:rwx" "$d" 2>/dev/null || true
                setfacl -d -m "u:$AGENT_USER:rwx" "$d" 2>/dev/null || true
            fi
        done
    fi
}

# On --upgrade, re-derive the elevation posture from what is already on the box so
# an upgrade never silently widens *or* drops what the operator chose. Explicit
# --enable-* flags on the upgrade command take precedence over the detected state.
derive_existing_posture() {
    if [ "$POSTURE_EXPLICIT" -eq 1 ]; then
        return 0
    fi
    if [ -f "$SUDOERS_FILE" ]; then
        ENABLE_SERVICE_CONTROL=1
        log_info "  Preserving existing service-control sudoers rule."
    fi
    if [ -f "$APACHE_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_APACHE_MANAGE=1
        ENABLE_APACHE=1  # implies the read ACL too
        log_info "  Preserving existing Apache-manage sudoers rule."
    fi
    if [ -f "$RKHUNTER_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_RKHUNTER_MANAGE=1
        log_info "  Preserving existing RKHunter-manage sudoers rule."
    fi
    if [ -f "$CRON_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_CRON_MANAGE=1
        log_info "  Preserving existing cron-manage sudoers rule."
    fi
    if [ -f "$PORTSENTRY_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_PORTSENTRY_MANAGE=1
        log_info "  Preserving existing portsentry-manage sudoers rule."
    fi
    if [ -f "$SERVICE_ENABLE_SUDOERS_FILE" ]; then
        ENABLE_SERVICE_ENABLE=1
        log_info "  Preserving existing service-enable sudoers rule."
    fi
    if [ -f "$PACKAGE_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_PACKAGE_MANAGE=1
        log_info "  Preserving existing package-manage sudoers rule."
    fi
    if [ -f "$PATCH_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_PATCH_MANAGE=1
        log_info "  Preserving existing patch-manage sudoers rule."
    fi
    if [ -f "$FIREWALL_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_FIREWALL_MANAGE=1
        log_info "  Preserving existing firewall-manage sudoers rule."
    fi
    if [ -f "$MAIL_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_MAIL_SETUP=1
        log_info "  Preserving existing mail-setup sudoers rule."
    fi
    if [ -f "$TEAMSPEAK_SETUP_SUDOERS_FILE" ]; then
        ENABLE_TEAMSPEAK_SETUP=1
        log_info "  Preserving existing teamspeak-setup sudoers rule."
    fi
    if [ -f "$DEPLOY_MANAGE_SUDOERS_FILE" ]; then
        ENABLE_DEPLOYMENT=1
        log_info "  Preserving existing deployment-module sudoers rule."
    fi
    if command -v getfacl >/dev/null 2>&1; then
        if getfacl -p /etc/apache2 2>/dev/null | grep -q "user:$AGENT_USER:"; then
            ENABLE_APACHE=1; log_info "  Preserving existing Apache read ACL."
        fi
        if getfacl -p /etc/letsencrypt 2>/dev/null | grep -q "user:$AGENT_USER:"; then
            ENABLE_CERTBOT=1; log_info "  Preserving existing Certbot read ACL."
        fi
    fi
    if id -nG "$AGENT_USER" 2>/dev/null | tr ' ' '\n' | grep -qx docker; then
        ENABLE_DOCKER=1; log_info "  Preserving existing docker-group membership."
    fi
    if id -nG "$AGENT_USER" 2>/dev/null | tr ' ' '\n' | grep -qx teamspeak || [ -d "$INSTALL_DIR/teamspeak" ]; then
        ENABLE_TEAMSPEAK=1; log_info "  Preserving existing TeamSpeak access provisioning."
    fi
}

write_sudoers() {
    # Only ever called when --enable-service-control is active. Service start/stop
    # genuinely needs privilege and cannot be done via file ACLs, so this is the
    # single remaining sudo grant. Hardened: exact commands (no wildcards - shell
    # meta-chars would bypass the restriction), runas root only (not ALL), env
    # reset + fixed secure_path, and Docker/Apache/Certbot are NEVER added here
    # (docker = group; apache2ctl/certbot are GTFOBins root-escalation wrappers;
    #  apache/certbot use read-only ACLs instead).
    # Note on the docker.service entries below: this rule grants ONLY the systemctl start/stop/restart/status
    # lifecycle of the docker DAEMON (argv-exact, no wildcard) - NOT the docker CLI, which is the real
    # container-escape surface (`docker run -v /:/host`) and stays behind the docker group. The whole rule is
    # opt-in (written only when service-control is enabled), so the daemon-lifecycle grant is an explicit
    # choice, scoped so the agent can bounce a wedged daemon without gaining a root shell.
    log_info "Configuring narrow service-control sudoers rule..."
    cat > "$SUDOERS_FILE" <<EOF
# Aetheus Agent - service control only (no wildcards, runas root only)
Defaults:$AGENT_USER !requiretty, env_reset, secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
Cmnd_Alias AETHEUS_SYSTEMCTL = \\
    /bin/systemctl start apache2, /bin/systemctl stop apache2, /bin/systemctl restart apache2, /bin/systemctl status apache2, \\
    /bin/systemctl start docker, /bin/systemctl stop docker, /bin/systemctl restart docker, /bin/systemctl status docker, \\
    /bin/systemctl start nginx, /bin/systemctl stop nginx, /bin/systemctl restart nginx, /bin/systemctl status nginx, \\
    /bin/systemctl start postfix, /bin/systemctl stop postfix, /bin/systemctl restart postfix, /bin/systemctl status postfix, \\
    /bin/systemctl start dovecot, /bin/systemctl stop dovecot, /bin/systemctl restart dovecot, /bin/systemctl status dovecot, \\
    /bin/systemctl start ts3server, /bin/systemctl stop ts3server, /bin/systemctl restart ts3server, /bin/systemctl status ts3server, \\
    /bin/systemctl start portsentry, /bin/systemctl stop portsentry, /bin/systemctl restart portsentry, /bin/systemctl status portsentry, \\
    /bin/systemctl start rkhunter, /bin/systemctl stop rkhunter, /bin/systemctl restart rkhunter, /bin/systemctl status rkhunter, \\
    /bin/systemctl start cron, /bin/systemctl stop cron, /bin/systemctl restart cron, /bin/systemctl status cron, \\
    /bin/systemctl start crond, /bin/systemctl stop crond, /bin/systemctl restart crond, /bin/systemctl status crond
$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_SYSTEMCTL
EOF
    chmod 440 "$SUDOERS_FILE"
    visudo -cf "$SUDOERS_FILE" || {
        log_error "Invalid sudoers syntax"
        rm -f "$SUDOERS_FILE"
        exit 1
    }
}

# Controlled sudo escalation for Apache management:
#   - exact argv (no wildcards) so apache2ctl -f <arbitrary> and friends are unreachable
#   - Defaults!CMDS noexec so a successful exec cannot spawn a shell-escape
#   - timestamp_timeout=0 so a concurrent process cannot inherit the sudo cache
#   - env_reset + fixed secure_path to neutralise PATH-style injection vectors
#   - one justification comment per Cmnd_Alias entry - forces manual review on extension
write_apache_manage_sudoers() {
    log_info "Configuring Apache-manage sudoers (item #6, ultra-strict allow-list)..."
    cat > "$APACHE_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - Apache management. Every rule below must remain argv-exact.

# 1. Test the config (apache2ctl configtest is non-destructive but reads /etc/apache2
#    and must run as root to read mod-restricted include files).
# 2. Graceful reload (apache2ctl graceful re-reads config without dropping connections).
# 3. systemd reload + lifecycle (preferred over apache2ctl on hosts where systemd owns it).
# Every entry uses an EXACT fully-resolved binary path + fixed argv string. No wildcards.
Cmnd_Alias AETHEUS_APACHE_MANAGE = \\
    /usr/sbin/apache2ctl configtest, \\
    /usr/sbin/apache2ctl graceful, \\
    /bin/systemctl reload apache2.service, \\
    /bin/systemctl start apache2.service, \\
    /bin/systemctl stop apache2.service, \\
    /bin/systemctl restart apache2.service

Defaults!AETHEUS_APACHE_MANAGE noexec
Defaults!AETHEUS_APACHE_MANAGE timestamp_timeout=0
Defaults!AETHEUS_APACHE_MANAGE env_reset
Defaults!AETHEUS_APACHE_MANAGE secure_path="/usr/sbin:/usr/bin:/sbin:/bin"

$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_APACHE_MANAGE
EOF
    chmod 440 "$APACHE_MANAGE_SUDOERS_FILE"
    visudo -cf "$APACHE_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid Apache-manage sudoers syntax"
        rm -f "$APACHE_MANAGE_SUDOERS_FILE"
        exit 1
    }

    # ACL write on sites-available AND sites-enabled so the agent can edit vhost configs and enable
    # them (the apache-proxy pipeline step symlinks into sites-enabled) without sudo. Reload is the
    # only piece that still needs root - keeps the surface minimal.
    if command -v setfacl >/dev/null 2>&1; then
        for d in /etc/apache2/sites-available /etc/apache2/sites-enabled; do
            if [ -d "$d" ]; then
                setfacl -m "u:$AGENT_USER:rwx" "$d" || true
                setfacl -d -m "u:$AGENT_USER:rwx" "$d" || true
                log_info "  ACL +rw on $d granted to $AGENT_USER."
            fi
        done
    else
        log_warn "  setfacl not available - cannot set write ACL on Apache sites dirs."
    fi

    # The apache-proxy step renders a reverse-proxy vhost (ProxyPass), so the proxy modules must be
    # loaded. a2enmod is idempotent; enable them at install time (configtest later confirms).
    if command -v a2enmod >/dev/null 2>&1; then
        a2enmod proxy proxy_http headers >/dev/null 2>&1 \
            && log_info "  Enabled Apache modules: proxy, proxy_http, headers." \
            || log_warn "  Could not enable Apache proxy modules (a2enmod) - reverse proxy may need them."
    fi
}

# Certbot management - root-owned issue helper + NOPASSWD sudoers. certbot's argv is domain-variable
# (can't be argv-exact granted), so the helper IS the security boundary: it re-validates each domain,
# performs real ACME issuance through a dedicated webroot in production. The explicit local mode
# creates a self-signed certificate for the isolated release lab without contacting ACME.
# Domains/email arrive via env (env_keep), never on the argv list.
write_certbot_manage() {
    log_info "Configuring certbot management: root-owned production ACME/local TLS helper + sudoers..."
    ensure_helper_dir

    cat > "$CERTBOT_ISSUE_HELPER_PATH" <<'HELPER_EOF'
#!/bin/sh
# Aetheus Agent certbot-issue helper - root-owned, invoked via /etc/sudoers.d/aetheus-certbot.
# Args: $1 = primary domain. Env: AETHEUS_CERTBOT_DOMAINS (comma list), AETHEUS_CERTBOT_EMAIL,
# AETHEUS_CERTBOT_MODE (production or local).
set -eu
ACME_WEBROOT=/var/lib/aetheus-agent/acme-webroot
ACME_CONF=/etc/apache2/conf-available/aetheus-acme-webroot.conf
LIVE_DIR=/etc/letsencrypt/live
SITES_AVAIL=/etc/apache2/sites-available
SITES_ENABLED=/etc/apache2/sites-enabled
valid_domain() { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9._-]{1,253}$'; }

primary="${1:-}"
valid_domain "$primary" || { echo "invalid primary domain" >&2; exit 2; }
domains="${AETHEUS_CERTBOT_DOMAINS:-$primary}"
email="${AETHEUS_CERTBOT_EMAIL:-}"
mode="${AETHEUS_CERTBOT_MODE:-production}"
case "$mode" in production|local) ;; *) echo "invalid certificate mode" >&2; exit 2;; esac
# The email is agent-influenced env input to a root-run certbot: validate it
# with the same discipline as domains (basic shape, no leading '-', no spaces).
if [ -n "$email" ]; then
    printf '%s' "$email" | grep -Eq '^[^@[:space:]-][^@[:space:]]*@[^@[:space:]]+$' \
        || { echo "invalid email" >&2; exit 2; }
fi

# Build validated -d args.
set --
san_names=""
OLDIFS=$IFS; IFS=','
for d in $domains; do
    d=$(printf '%s' "$d" | tr -d ' ')
    [ -n "$d" ] || continue
    valid_domain "$d" || { echo "invalid domain in list: $d" >&2; exit 2; }
    set -- "$@" -d "$d"
    if [ -n "$san_names" ]; then san_names="$san_names,DNS:$d"; else san_names="DNS:$d"; fi
done
IFS=$OLDIFS
if [ "$#" -eq 0 ]; then set -- -d "$primary"; san_names="DNS:$primary"; fi

# Real ACME through a stable webroot shared by every managed vhost.
# --expand: when a lineage already covers a SUBSET of the requested names (e.g. adding an api host to an
#   existing apex+app cert), reissue and REPLACE it to cover all of them instead of erroring on the
#   interactive "expand?" prompt (which -n turns into a hard failure).
# --keep-until-expiring: when the requested names EXACTLY match a still-valid cert, keep it (idempotent -
#   no needless reissue, so repeated runs don't burn the Let's Encrypt duplicate-cert rate limit).
if [ "$mode" = local ]; then
    echo "local certificate mode selected - ACME is not contacted."
fi
if [ "$mode" = production ]; then
    command -v certbot >/dev/null 2>&1 || { echo "certbot not installed" >&2; exit 1; }
    mkdir -p "$ACME_WEBROOT/.well-known/acme-challenge"
    cat > "$ACME_CONF" <<CONF
Alias /.well-known/acme-challenge/ $ACME_WEBROOT/.well-known/acme-challenge/
<Directory "$ACME_WEBROOT/.well-known/acme-challenge">
    Require all granted
</Directory>
CONF
    a2enconf aetheus-acme-webroot >/dev/null
    apache2ctl configtest
    systemctl reload apache2
    if [ -n "$email" ]; then set -- "$@" -m "$email"; else set -- "$@" --register-unsafely-without-email; fi
    certbot certonly --webroot --webroot-path "$ACME_WEBROOT" --non-interactive --agree-tos --expand --keep-until-expiring "$@" \
        || { echo "certbot ACME validation failed for $primary" >&2; exit 1; }
    echo "certbot: real certificate obtained for $primary"
    exit 0
fi

# Local-only certificate in the LE layout + an SSL vhost proxying to the same upstream as :80.
command -v openssl >/dev/null 2>&1 || { echo "openssl missing - cannot create local cert" >&2; exit 1; }
dir="$LIVE_DIR/$primary"
# Never replace an existing CA-issued certificate, even in an explicitly local environment.
if [ -f "$dir/fullchain.pem" ]; then
    _subj=$(openssl x509 -in "$dir/fullchain.pem" -noout -subject 2>/dev/null)
    _iss=$(openssl x509 -in "$dir/fullchain.pem" -noout -issuer 2>/dev/null)
    if [ -n "$_subj" ] && [ "${_subj#subject}" != "${_iss#issuer}" ]; then
        echo "keeping the existing CA-issued certificate for $primary (refusing to overwrite a real cert with self-signed)." >&2
        exit 1
    fi
fi
mkdir -p "$dir"
openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
    -keyout "$dir/privkey.pem" -out "$dir/fullchain.pem" \
    -subj "/CN=$primary" -addext "subjectAltName=$san_names" 2>/dev/null
chmod 600 "$dir/privkey.pem"

http_site="$SITES_AVAIL/${primary}.conf"
ssl_site="$SITES_AVAIL/${primary}-ssl.conf"
proxy_line=$(grep -E '^[[:space:]]*ProxyPass[[:space:]]+/' "$http_site" 2>/dev/null | head -1 | sed -E 's/^[[:space:]]*//')
[ -n "$proxy_line" ] || proxy_line="ProxyPass / http://127.0.0.1:8090/"
proxy_rev=$(printf '%s' "$proxy_line" | sed 's/^ProxyPass/ProxyPassReverse/')
cat > "$ssl_site" <<VHOST
# Managed by Aetheus - local self-signed HTTPS for $primary
<VirtualHost *:443>
    ServerName $primary
    SSLEngine on
    SSLCertificateFile $dir/fullchain.pem
    SSLCertificateKeyFile $dir/privkey.pem
    ProxyPreserveHost On
    ProxyRequests Off
    $proxy_line
    $proxy_rev
</VirtualHost>
VHOST
a2enmod ssl >/dev/null 2>&1 || true
ln -sfn "$ssl_site" "$SITES_ENABLED/${primary}-ssl.conf"
if apache2ctl configtest 2>&1; then
    apache2ctl graceful 2>/dev/null || systemctl reload apache2
    echo "local self-signed HTTPS configured for $primary."
    exit 0
fi
echo "apache configtest failed after writing the SSL vhost for $primary" >&2
exit 1
HELPER_EOF
    chown root:root "$CERTBOT_ISSUE_HELPER_PATH"
    chmod 755 "$CERTBOT_ISSUE_HELPER_PATH"

    # Lifecycle helper: renew/delete/revoke a named lineage (or renew all). The cert name is the only
    # variable and is re-validated here against the SAME shape the backend/agent enforce, so a fixed
    # certbot verb runs argv-exact - certbot stays GTFOBins-forbidden as a free-form sudo target.
    cat > "$CERTBOT_MANAGE_HELPER_PATH" <<'MANAGE_EOF'
#!/bin/sh
# Aetheus Agent certbot-manage helper - root-owned, invoked via /etc/sudoers.d/aetheus-certbot.
# Args: $1 = verb (renew|renew-all|delete|revoke), $2 = cert lineage name (omitted for renew-all).
set -eu
valid_name() { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,199}$'; }
command -v certbot >/dev/null 2>&1 || { echo "certbot not installed" >&2; exit 1; }
verb="${1:-}"
case "$verb" in
  renew-all)
    certbot renew --non-interactive ;;
  renew)
    name="${2:-}"; valid_name "$name" || { echo "invalid cert name" >&2; exit 2; }
    certbot renew --cert-name "$name" --non-interactive ;;
  delete)
    name="${2:-}"; valid_name "$name" || { echo "invalid cert name" >&2; exit 2; }
    certbot delete --cert-name "$name" --non-interactive ;;
  revoke)
    name="${2:-}"; valid_name "$name" || { echo "invalid cert name" >&2; exit 2; }
    certbot revoke --cert-name "$name" --delete-after-revoke --non-interactive ;;
  *)
    echo "unknown certbot-manage verb: $verb" >&2; exit 2 ;;
esac
MANAGE_EOF
    chown root:root "$CERTBOT_MANAGE_HELPER_PATH"
    chmod 755 "$CERTBOT_MANAGE_HELPER_PATH"

    cat > "$CERTBOT_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - certbot management. The helpers are the security boundary: root-owned,
# agent-non-writable, re-validate every domain/cert name, and only issue/renew/delete/revoke certs.
# noexec is intentionally omitted (the helpers are vetted scripts that must exec certbot/openssl/apache2ctl).
# Domains/email travel in env (kept off argv) and are preserved through sudo via env_keep.
Cmnd_Alias AETHEUS_CERTBOT = $CERTBOT_ISSUE_HELPER_PATH, $CERTBOT_MANAGE_HELPER_PATH
Defaults!AETHEUS_CERTBOT timestamp_timeout=0
Defaults!AETHEUS_CERTBOT env_reset
Defaults!AETHEUS_CERTBOT env_keep += "AETHEUS_CERTBOT_DOMAINS AETHEUS_CERTBOT_EMAIL AETHEUS_CERTBOT_MODE"
Defaults!AETHEUS_CERTBOT secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_CERTBOT
EOF
    chmod 440 "$CERTBOT_MANAGE_SUDOERS_FILE"
    visudo -cf "$CERTBOT_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid certbot-manage sudoers syntax"
        rm -f "$CERTBOT_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

# Migration cleanup: a host previously provisioned by the PRE-RENAME "prometheus" agent still carries
# /etc/sudoers.d/prometheus-* drop-ins (defining the old PROM_* aliases) and a /usr/local/lib/prometheus
# helper dir. Left in place they (a) DUPLICATE alias names the sudo parser reads across all drop-ins ->
# "Alias already defined" warnings on every sudo call, and (b) leave orphaned root grants for the defunct
# prometheus-agent user. Remove the old agent's artifacts before writing ours. Best-effort, never blocks.
cleanup_legacy_prometheus_artifacts() {
    # EXACT names only - never a blind `prometheus-*` glob: an unrelated third party (the Prometheus
    # monitoring stack) ships its OWN sudoers drop-ins (prometheus-node-exporter, prometheus-alertmanager,
    # ...) and must never be deleted here. This list mirrors the aetheus-* drop-ins this installer writes.
    for _legacy in agent apache certbot cron deploy mail package portsentry rkhunter service-enable teamspeak; do
        _f="/etc/sudoers.d/prometheus-${_legacy}"
        if [ -f "$_f" ]; then
            log_info "Removing legacy ${_f} (pre-rename migration)..."
            rm -f "$_f" 2>/dev/null || true
        fi
    done
    # Old root-owned helper dir of the pre-rename agent (its own path, not shared with any third party).
    if [ -d /usr/local/lib/prometheus ]; then
        log_info "Removing legacy /usr/local/lib/prometheus helper dir..."
        rm -rf /usr/local/lib/prometheus 2>/dev/null || true
    fi
}

# Apply the configured sudoers state. With service-control disabled there is NO
# /etc/sudoers.d/aetheus-agent at all (the agent runs with zero sudo).
apply_sudoers() {
    cleanup_legacy_prometheus_artifacts
    if [ "$ENABLE_SERVICE_CONTROL" -eq 1 ]; then
        write_sudoers
    else
        rm -f "$SUDOERS_FILE"
        log_info "No sudoers rule installed (service control disabled - fully sandboxed)."
    fi
    if [ "$ENABLE_APACHE_MANAGE" -eq 1 ]; then
        write_apache_manage_sudoers
    else
        rm -f "$APACHE_MANAGE_SUDOERS_FILE"
    fi
    # Certbot management: root-owned webroot ACME helper + NOPASSWD sudoers.
    if [ "$ENABLE_CERTBOT_MANAGE" -eq 1 ]; then
        write_certbot_manage
    else
        rm -f "$CERTBOT_MANAGE_SUDOERS_FILE" "$CERTBOT_ISSUE_HELPER_PATH" "$CERTBOT_MANAGE_HELPER_PATH"
    fi
    if [ "$ENABLE_RKHUNTER_MANAGE" -eq 1 ]; then
        write_rkhunter_manage_sudoers
    else
        rm -f "$RKHUNTER_MANAGE_SUDOERS_FILE"
    fi
    # Phase 3 - cron management: root-owned helper + NOPASSWD sudoers for that one binary.
    if [ "$ENABLE_CRON_MANAGE" -eq 1 ]; then
        write_cron_manage
    else
        rm -f "$CRON_MANAGE_SUDOERS_FILE" "$CRON_HELPER_PATH"
    fi
    # Phase 3 - portsentry management: root-owned unblock-ip + portsentry-setup helpers + NOPASSWD sudoers.
    if [ "$ENABLE_PORTSENTRY_MANAGE" -eq 1 ]; then
        write_portsentry_manage
    else
        rm -f "$PORTSENTRY_MANAGE_SUDOERS_FILE" "$UNBLOCK_HELPER_PATH" "$PORTSENTRY_SETUP_HELPER_PATH"
    fi
    # Phase 3 (option B) - fixed-unit systemctl enable --now allow-list (argv-exact, no helper).
    if [ "$ENABLE_SERVICE_ENABLE" -eq 1 ]; then
        write_service_enable_sudoers
    else
        rm -f "$SERVICE_ENABLE_SUDOERS_FILE"
    fi
    # S-FEAT-W8KN - fixed-package apt-get install/remove allow-list (argv-exact, no helper).
    if [ "$ENABLE_PACKAGE_MANAGE" -eq 1 ]; then
        write_package_manage_sudoers
    else
        rm -f "$PACKAGE_MANAGE_SUDOERS_FILE"
    fi
    # PLAN-006 4.1 - fixed apt-get upgrade allow-list (argv-exact, no helper) for fleet OS patching.
    if [ "$ENABLE_PATCH_MANAGE" -eq 1 ]; then
        write_patch_manage_sudoers
    else
        rm -f "$PATCH_MANAGE_SUDOERS_FILE"
    fi
    # PLAN-006 4.2 - root-owned ufw helper + path-only sudoers for firewall control.
    if [ "$ENABLE_FIREWALL_MANAGE" -eq 1 ]; then
        write_firewall_manage
    else
        rm -f "$FIREWALL_MANAGE_SUDOERS_FILE" "$FIREWALL_HELPER_PATH"
    fi
    # S-FEAT-W8KN - mail setup: root-owned helper + NOPASSWD sudoers for that one binary.
    if [ "$ENABLE_MAIL_SETUP" -eq 1 ]; then
        write_mail_setup
    else
        rm -f "$MAIL_MANAGE_SUDOERS_FILE" "$MAIL_SETUP_HELPER_PATH" "$MAIL_MANAGE_HELPER_PATH"
    fi
    # TeamSpeak setup: root-owned helper + NOPASSWD sudoers for that one binary.
    if [ "$ENABLE_TEAMSPEAK_SETUP" -eq 1 ]; then
        write_teamspeak_setup
    else
        rm -f "$TEAMSPEAK_SETUP_SUDOERS_FILE" "$TEAMSPEAK_SETUP_HELPER_PATH"
    fi
    # Deployment module - unit template + root-owned deploy-restart helper + NOPASSWD sudoers + base dir.
    if [ "$ENABLE_DEPLOYMENT" -eq 1 ]; then
        write_deploy_module
    else
        # Teardown leaves $DEPLOY_BASE_DIR in place (it holds deployed apps); only the privileged
        # bits are removed. daemon-reload only when the template actually went away.
        if [ -f "$DEPLOY_UNIT_TEMPLATE_PATH" ]; then
            rm -f "$DEPLOY_MANAGE_SUDOERS_FILE" "$DEPLOY_RESTART_HELPER_PATH" "$DEPLOY_UNIT_TEMPLATE_PATH"
            systemctl daemon-reload 2>/dev/null || true
        else
            rm -f "$DEPLOY_MANAGE_SUDOERS_FILE" "$DEPLOY_RESTART_HELPER_PATH"
        fi
    fi
    grant_sudoers_read_acls
}

# The agent hashes the sudoers drop-ins each heartbeat to self-report its elevation posture (the
# backend derives PackageManagementAvailable / DeploymentTargetAvailable from those hashes - and
# gates the install/uninstall UI on them). Drop-ins are root:root 0440, so the non-root agent cannot
# read them without an explicit grant - give it READ-ONLY POSIX ACL on each aetheus-* drop-in.
# Read only, never write: sudo still owns the files and visudo still accepts them (a read ACL adds no
# group/other WRITE bit, which is all sudo refuses). Without this the capability stays OFF even when
# the grant is present - the file exists but is unreadable to the agent, so no hash is reported.
grant_sudoers_read_acls() {
    # A hash-derived capability is dead-on-arrival without setfacl, so try harder than the best-effort
    # ensure_optional_packages pass (which can race a not-yet-refreshed apt index on a freshly-imaged
    # box): one last-chance install before we give up. install_package refreshes the index itself.
    if ! command -v setfacl >/dev/null 2>&1; then
        log_warn "  setfacl missing, attempting to install 'acl' (needed to self-report capabilities)..."
        install_package acl || true
    fi
    if ! command -v setfacl >/dev/null 2>&1; then
        # Which REQUESTED capabilities the backend derives from a drop-in hash; these are the ones the
        # operator will see as "unavailable" in the UI despite the sudo grant being correctly installed.
        _gsr_blocked=""
        [ "$ENABLE_PACKAGE_MANAGE" -eq 1 ] && _gsr_blocked="$_gsr_blocked package-manage"
        [ "$ENABLE_PATCH_MANAGE" -eq 1 ] && _gsr_blocked="$_gsr_blocked patch-manage"
        [ "$ENABLE_FIREWALL_MANAGE" -eq 1 ] && _gsr_blocked="$_gsr_blocked firewall-manage"
        [ "$ENABLE_DEPLOYMENT" -eq 1 ]     && _gsr_blocked="$_gsr_blocked deployment"
        if [ -n "$_gsr_blocked" ]; then
            ACL_GRANT_DEGRADED=1
            log_warn "  ============================================================"
            log_warn "  WARNING: 'acl' (setfacl) is unavailable and could not be installed."
            log_warn "  The sudo grant IS installed, but the non-root agent cannot read the"
            log_warn "  /etc/sudoers.d/aetheus-* drop-ins to hash them, so these REQUESTED"
            log_warn "  capabilities will report UNAVAILABLE in the UI:"
            log_warn "    ${_gsr_blocked# }"
            log_warn "  Fix: install acl, then re-run this installer (idempotent):"
            log_warn "    apt-get update && apt-get install -y acl"
            log_warn "  ============================================================"
        else
            log_warn "  setfacl not available; sudoers drop-ins won't be hashable. No hash-derived"
            log_warn "  capability was requested, so this is non-blocking."
        fi
        return 0
    fi
    _gsr_any=0
    for _gsr_file in /etc/sudoers.d/aetheus-*; do
        [ -f "$_gsr_file" ] || continue
        if setfacl -m "u:$AGENT_USER:r" "$_gsr_file" 2>/dev/null; then
            _gsr_any=1
        else
            log_warn "  setfacl read grant failed on $_gsr_file (non-fatal)"
        fi
    done
    if [ "$_gsr_any" -eq 1 ]; then
        log_info "  Granted $AGENT_USER read access to sudoers drop-ins (capability self-report)."
    fi
}

# Create the root-owned helper directory (root:root, 0755 - agent can traverse + exec but NOT
# write, so it cannot tamper with the helpers it is allowed to sudo).
ensure_helper_dir() {
    mkdir -p "$AETHEUS_HELPER_DIR"
    chown root:root "$AETHEUS_HELPER_DIR" 2>/dev/null || true
    chmod 755 "$AETHEUS_HELPER_DIR" 2>/dev/null || true
}

# Phase 3 - cron management. Deploys the root-owned cron-apply helper (the security boundary: it
# re-validates every argument and can ONLY write /etc/cron.d/aetheus-<id>) and a NOPASSWD sudoers
# grant for exactly that binary. noexec is intentionally NOT set on this Cmnd_Alias: the helper is a
# vetted shell script that must exec coreutils (chmod/chown) - it is itself the boundary, not a
# general GTFOBins tool.
write_cron_manage() {
    log_info "Configuring cron-manage helper + sudoers (Phase 3, root-owned helper boundary)..."
    ensure_helper_dir
    cat > "$CRON_HELPER_PATH" <<'HELPER_EOF'
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Aetheus Agent cron helper - root-owned, invoked via /etc/sudoers.d/aetheus-cron (NOPASSWD).
# Writes/removes /etc/cron.d/aetheus-<id> ONLY. Re-validates every argument; never evals input.
set -eu
CRON_DIR="/etc/cron.d"
die() { echo "$1" >&2; exit 1; }
valid_id()    { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9_-]{1,64}$'; }
valid_user()  { printf '%s' "$1" | grep -Eiq '^[a-z_][a-z0-9_-]{0,31}$'; }
valid_sched() { printf '%s' "$1" | grep -Eq '^[0-9* /,-]+$'; }
valid_cmd()   { printf '%s' "$1" | grep -Eq '^[A-Za-z0-9 _/.:@=,-]+$'; }

action="${1:-}"
id="${2:-}"
valid_id "$id" || die "invalid id"
target="$CRON_DIR/aetheus-$id"

case "$action" in
    save)
        user="${3:-}"; sched="${4:-}"; cmd="${5:-}"
        valid_user "$user"  || die "invalid user"
        # A root cron job turns agent compromise into a root-cron escalation - refuse it here too
        # (the backend + agent already reject it; this is the last line).
        [ "$user" != root ] || die "root user not permitted"
        valid_sched "$sched" || die "invalid schedule"
        # /etc/cron.d is strictly 5-field - a 6th field would be read as the run-as user, corrupting
        # the entry. Reject anything that is not exactly 5 whitespace-separated fields.
        [ "$(printf '%s' "$sched" | wc -w)" -eq 5 ] || die "schedule must be 5 fields"
        valid_cmd "$cmd"    || die "invalid command"
        umask 022
        printf '# Managed by Aetheus (job %s)\n%s %s %s\n' "$id" "$sched" "$user" "$cmd" > "$target"
        chown root:root "$target"
        chmod 644 "$target"
        echo "wrote $target"
        ;;
    delete)
        rm -f "$target"
        echo "removed $target"
        ;;
    *)
        die "unknown action"
        ;;
esac
HELPER_EOF
    chown root:root "$CRON_HELPER_PATH"
    chmod 755 "$CRON_HELPER_PATH"

    cat > "$CRON_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - cron management (Phase 3). The helper is the security boundary: root-owned,
# agent-non-writable, re-validates every argument, and can only write /etc/cron.d/aetheus-<id>.
# noexec is intentionally omitted (the helper is a vetted script that must exec coreutils).
Cmnd_Alias AETHEUS_CRON = $CRON_HELPER_PATH
Defaults!AETHEUS_CRON timestamp_timeout=0
Defaults!AETHEUS_CRON env_reset
Defaults!AETHEUS_CRON secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_CRON
EOF
    chmod 440 "$CRON_MANAGE_SUDOERS_FILE"
    visudo -cf "$CRON_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid cron-manage sudoers syntax"
        rm -f "$CRON_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

# Deployment module - makes this host a cross-agent deploy target. Three pieces, all posed at install
# time so nothing is ever generated from a pipeline YAML (anti-injection):
#   1. A systemd unit TEMPLATE (aetheus-app@.service). The instance name %i is the validated <app>;
#      ExecStart points at the agent-owned $DEPLOY_BASE_DIR/<app>/current/run script SHIPPED INSIDE THE
#      ARTIFACT - the "how to start" never travels in the task, only the app name does.
#   2. A root-owned deploy-restart helper (the security boundary: re-validates <app>, can ONLY restart
#      aetheus-app@<app>.service) + a NOPASSWD sudoers grant for exactly that binary. noexec is
#      omitted (the helper is a vetted script that must exec systemctl) - same recipe as cron-apply.
#   3. The agent-owned $DEPLOY_BASE_DIR tree (it writes releases/ and atomically flips current).
# The helper remains the narrow privilege boundary.
# S-FEAT-DPU2 (v2 isolation - IMPLEMENTED): the unit template below runs each deployed app under a
# transient per-app systemd identity (DynamicUser) with a StateDirectory for its writable state,
# instead of the shared agent user. The ownership split that makes this work: $DEPLOY_BASE_DIR/%i
# stays AGENT-managed (the agent writes releases/ and flips 'current' as $AGENT_USER) but its group is
# the dedicated read-only $DEPLOY_READ_GROUP (setgid base dir, mode 0750 on each release - GROUP-readable,
# never world: the payload carries cleartext substituted secrets). The unit grants the app that group via
# SupplementaryGroups, so the transient uid reads the payload without joining the agent's group. The app's
# writable HOME moves off the deploy tree onto /var/lib/aetheus-app-%i (systemd StateDirectory,
# auto-owned by the transient uid, persisted across redeploys).
# Residual (documented limitation): apps sharing $DEPLOY_READ_GROUP can read each other's release trees -
# closing that fully needs per-app groups (in tension with the anti-injection no-useradd-at-deploy model).
# NOTE: smoke-test on a real systemd box (DataProtection-key persistence + release-read under the
# transient uid) when first enabling the module on a host - the VPS-sim is the place to validate it.
write_deploy_module() {
    log_info "Configuring deployment module: unit template + deploy-restart helper + sudoers + base dir..."
    ensure_helper_dir

    cat > "$DEPLOY_UNIT_TEMPLATE_PATH" <<EOF
# Managed by Aetheus - deploy unit TEMPLATE (anti-injection: posed at install, never YAML-generated).
# %i is the validated app instance name. The 'run' launcher ships inside the deployed artifact.
[Unit]
Description=Aetheus deployed app %i
After=network-online.target
Wants=network-online.target

[Service]
Type=exec
# S-FEAT-DPU2 (v2 isolation): each deployed app runs under a TRANSIENT, per-app system identity
# (systemd DynamicUser) instead of the shared agent user - so a third-party build shares no uid,
# no \$HOME, and no process/file surface with the agent or with sibling deployed apps. No account
# management is needed (the uid is allocated per start), which keeps this module's anti-injection
# model intact (the app name never spawns a useradd).
DynamicUser=yes
# The transient uid joins the dedicated read-only group that owns the deploy trees (setgid below), so
# it can read its agent-written, secret-bearing release payload WITHOUT that payload being world-readable
# and WITHOUT joining the agent's own group (no agent-surface sharing).
SupplementaryGroups=$DEPLOY_READ_GROUP
WorkingDirectory=$DEPLOY_BASE_DIR/%i/current
ExecStart=$DEPLOY_BASE_DIR/%i/current/run
Restart=always
RestartSec=5
# Writable state lives on a systemd-managed StateDirectory - /var/lib/aetheus-app-<app>, created
# and chowned to the transient uid on every start and PERSISTED across restarts/redeploys (not wiped
# like RuntimeDirectory). HOME points here so the ASP.NET Core Data Protection key ring
# (~/.aspnet/DataProtection-Keys) and any \$HOME cache survive - without the app ever writing into the
# agent-owned deploy tree. This is the ownership split that makes DynamicUser compatible with the
# agent-manages-releases / app-reads-releases model.
StateDirectory=aetheus-app-%i
Environment=HOME=/var/lib/aetheus-app-%i
# PLAN-001 phase 2 (zero-config OTLP): the deploy executor may drop a .aetheus-env (OTEL endpoint +
# ingestion key) into the flipped release. The leading '-' makes it OPTIONAL, so a deploy that injects
# nothing (feature off / no linked MonitoredApp) leaves the unit unchanged.
EnvironmentFile=-$DEPLOY_BASE_DIR/%i/current/.aetheus-env

# Hardening - the DEPLOYED app is a third-party build; it must NOT inherit the agent user's sudo
# surface (the agent unit runs NoNewPrivileges=false to use deploy-restart, but the app never needs
# it). ProtectSystem=full keeps /usr,/boot,/etc read-only. The read-only payload
# ($DEPLOY_BASE_DIR/%i/current -> releases/) is agent-owned and GROUP-readable by $DEPLOY_READ_GROUP
# (mode 0750, NEVER world - it carries pipeline-substituted secrets in cleartext): the deploy executor
# chmods each release 0750 and the setgid base dir stamps the group, so the transient uid reads via its
# SupplementaryGroups membership while other host users cannot. No ReadWritePaths into the deploy tree:
# the app's only writable area is its StateDirectory above.
NoNewPrivileges=true
ProtectSystem=full
ProtectHome=true
PrivateTmp=true
ProtectControlGroups=true
ProtectKernelTunables=true
ProtectKernelModules=true
RestrictSUIDSGID=true
RestrictRealtime=true

[Install]
WantedBy=multi-user.target
EOF
    chmod 644 "$DEPLOY_UNIT_TEMPLATE_PATH"

    cat > "$DEPLOY_RESTART_HELPER_PATH" <<'HELPER_EOF'
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Aetheus Agent deploy-restart helper - root-owned, invoked via /etc/sudoers.d/aetheus-deploy
# (NOPASSWD). (Re)starts ONLY aetheus-app@<app>.service for a re-validated <app>. Never evals input.
set -eu
app="${1:-}"
printf '%s' "$app" | grep -Eq '^[a-zA-Z0-9_-]{1,64}$' || { echo "invalid app" >&2; exit 1; }
unit="aetheus-app@${app}.service"
# `enable` makes the FIRST deploy of a template instance persist across reboot (idempotent on later
# deploys); `restart` then starts it (fresh) or restarts it (already running). Together they cover the
# first-instantiation case the bare `restart` missed.
systemctl enable "$unit"
systemctl restart "$unit"
echo "restarted $unit"
HELPER_EOF
    chown root:root "$DEPLOY_RESTART_HELPER_PATH"
    chmod 755 "$DEPLOY_RESTART_HELPER_PATH"

    cat > "$DEPLOY_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - cross-agent deployment. The helper is the security boundary: root-owned,
# agent-non-writable, re-validates <app>, and can only restart aetheus-app@<app>.service.
# noexec is intentionally omitted (the helper is a vetted script that must exec systemctl).
Cmnd_Alias AETHEUS_DEPLOY = $DEPLOY_RESTART_HELPER_PATH
Defaults!AETHEUS_DEPLOY timestamp_timeout=0
Defaults!AETHEUS_DEPLOY env_reset
Defaults!AETHEUS_DEPLOY secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_DEPLOY
EOF
    chmod 440 "$DEPLOY_MANAGE_SUDOERS_FILE"
    visudo -cf "$DEPLOY_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid deploy sudoers syntax"
        rm -f "$DEPLOY_MANAGE_SUDOERS_FILE"
        exit 1
    }

    # S-FEAT-DPU2: dedicated read-only group for deployed apps to read their payload via SupplementaryGroups.
    getent group "$DEPLOY_READ_GROUP" >/dev/null 2>&1 || groupadd --system "$DEPLOY_READ_GROUP" 2>/dev/null || true

    # Agent-owned base dir: the agent writes releases/ and flips the 'current' symlink here (no sudo).
    # Group = the deploy-read group with setgid (2750) so every release dir/file the agent extracts under
    # it inherits that group - the transient DynamicUser then reads the payload via its group membership,
    # owner-agent keeps write, and other host users get nothing (o---).
    mkdir -p "$DEPLOY_BASE_DIR"
    chown "$AGENT_USER:$DEPLOY_READ_GROUP" "$DEPLOY_BASE_DIR" 2>/dev/null || true
    chmod 2750 "$DEPLOY_BASE_DIR" 2>/dev/null || true

    # $WORK_DIR is also the agent's systemd StateDirectory. It stays private (no read/list access),
    # but the deployed app's DynamicUser must be able to traverse this one parent component before
    # reaching the group-readable deploy tree. A named ACL grants exactly execute to the dedicated
    # deploy group; StateDirectoryMode=0710 below preserves the ACL mask across agent restarts.
    # Never widen this to world execute: the parent also contains credentials, keys and backups.
    if ! command -v setfacl >/dev/null 2>&1; then
        log_error "Deployment module requires 'setfacl' to grant safe traverse-only access on $WORK_DIR."
        exit 1
    fi
    setfacl -m "g:$DEPLOY_READ_GROUP:--x" "$WORK_DIR"

    # Static web root: make it agent-writable via ACL so a pipeline can publish a static site
    # (e.g. the vitrine) there without sudo, while Apache (www-data) still reads it. Recursive +
    # default ACL so files created later inherit the grant. Requires 'acl' (setfacl); the installer
    # already depends on it for other hash-derived capabilities.
    if [ -n "$DEPLOY_WEB_ROOT" ]; then
        mkdir -p "$DEPLOY_WEB_ROOT"
        if command -v setfacl >/dev/null 2>&1; then
            setfacl -R  -m "u:$AGENT_USER:rwx" "$DEPLOY_WEB_ROOT" 2>/dev/null || true
            setfacl -R -d -m "u:$AGENT_USER:rwx" "$DEPLOY_WEB_ROOT" 2>/dev/null || true
            log_info "Web root $DEPLOY_WEB_ROOT is agent-writable (ACL) for static-site publishing."
        else
            log_warn "setfacl missing: cannot grant agent write on $DEPLOY_WEB_ROOT (install 'acl' and re-run)."
        fi
    fi

    systemctl daemon-reload 2>/dev/null || true
}

# Phase 3 - portsentry IP unblock. Deploys the root-owned unblock-ip helper (validates the IP, then
# removes it from iptables/ip6tables + /etc/hosts.deny) and a NOPASSWD sudoers grant for exactly that
# binary. Keeps /etc/hosts.deny (a 'spawn'-capable root-RCE file) out of any standing agent ACL.
write_portsentry_manage() {
    log_info "Configuring portsentry-unblock helper + sudoers (Phase 3, root-owned helper boundary)..."
    ensure_helper_dir
    cat > "$UNBLOCK_HELPER_PATH" <<'HELPER_EOF'
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Aetheus Agent portsentry unblock helper - root-owned, invoked via
# /etc/sudoers.d/aetheus-portsentry (NOPASSWD). Removes ONE validated IP from iptables/ip6tables
# INPUT DROP rules and /etc/hosts.deny. Never evals input.
set -u
ip="${1:-}"
# IPv4 or IPv6: hex digits, dots, colons only - no shell metachars, no spaces.
printf '%s' "$ip" | grep -Eq '^[0-9a-fA-F.:]{2,45}$' || { echo "invalid ip" >&2; exit 1; }

# Firewall removal is best-effort (the rule may not exist). secure_path resolves the binaries.
iptables  -D INPUT -s "$ip" -j DROP 2>/dev/null || true
ip6tables -D INPUT -s "$ip" -j DROP 2>/dev/null || true

# Drop matching lines from /etc/hosts.deny (anchored token-boundary filter to a temp, then replace).
# We only ever DELETE lines - never interpret the file - so no 'spawn' directive can be triggered here.
# Anchored (dots escaped) so unblocking 10.0.0.1 cannot also strip a deny rule for 10.0.0.10 / 110.0.0.1.
if [ -f /etc/hosts.deny ]; then
    tmp="$(mktemp)"
    esc_ip="$(printf '%s' "$ip" | sed 's/[.]/\\./g')"
    grep -vE "(^|[^0-9A-Fa-f.:])${esc_ip}([^0-9A-Fa-f.:]|\$)" /etc/hosts.deny > "$tmp" 2>/dev/null || true
    cat "$tmp" > /etc/hosts.deny
    rm -f "$tmp"
fi
echo "unblocked $ip"
HELPER_EOF
    chown root:root "$UNBLOCK_HELPER_PATH"
    chmod 755 "$UNBLOCK_HELPER_PATH"

    # Root-owned portsentry-setup helper: apt-installs portsentry and writes the scan mode + TCP/UDP port
    # lists into the config, then enables the service. Re-validates every positional arg (the agent already
    # validated, but the helper is the boundary) and never evals input. Replaces the old back-side sed/apt
    # shell chain the agent CommandValidator rejected.
    cat > "$PORTSENTRY_SETUP_HELPER_PATH" <<'SETUP_EOF'
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Aetheus Agent portsentry-setup helper - root-owned, invoked via
# /etc/sudoers.d/aetheus-portsentry (NOPASSWD). Positional args: <mode> <tcp_ports> <udp_ports>.
set -u
mode="${1:-}"
tcp_ports="${2:-}"
udp_ports="${3:-}"

# Mode: 1-16 alphanumerics. Port lists: comma-separated digit groups. No shell metachars, no spaces.
printf '%s' "$mode"      | grep -Eq '^[a-zA-Z0-9]{1,16}$'   || { echo "invalid mode" >&2; exit 1; }
printf '%s' "$tcp_ports" | grep -Eq '^[0-9]{1,5}(,[0-9]{1,5})*$' || { echo "invalid tcp ports" >&2; exit 1; }
printf '%s' "$udp_ports" | grep -Eq '^[0-9]{1,5}(,[0-9]{1,5})*$' || { echo "invalid udp ports" >&2; exit 1; }

# Advanced UDP scan mode mirrors the mode, forced to the 'a' (advanced) family for parity with the
# previous behaviour (e.g. mode "tcp"/"atcp" -> UDP mode "atcp").
udp_mode="a$(printf '%s' "$mode" | sed 's/^a*//')"

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq portsentry

# Scan modes live in /etc/default/portsentry; the port lists / block toggles in portsentry.conf.
if [ -f /etc/default/portsentry ]; then
    sed -i "s/^TCP_MODE=.*/TCP_MODE=\"$mode\"/"     /etc/default/portsentry
    sed -i "s/^UDP_MODE=.*/UDP_MODE=\"$udp_mode\"/" /etc/default/portsentry
fi
if [ -f /etc/portsentry/portsentry.conf ]; then
    sed -i "s/^TCP_PORTS=.*/TCP_PORTS=\"$tcp_ports\"/" /etc/portsentry/portsentry.conf
    sed -i "s/^UDP_PORTS=.*/UDP_PORTS=\"$udp_ports\"/" /etc/portsentry/portsentry.conf
    sed -i "s/^BLOCK_TCP=.*/BLOCK_TCP=\"1\"/"           /etc/portsentry/portsentry.conf
    sed -i "s/^BLOCK_UDP=.*/BLOCK_UDP=\"1\"/"           /etc/portsentry/portsentry.conf
fi

systemctl enable portsentry
systemctl restart portsentry
echo "PortSentry setup completed (mode $mode)"
SETUP_EOF
    chown root:root "$PORTSENTRY_SETUP_HELPER_PATH"
    chmod 755 "$PORTSENTRY_SETUP_HELPER_PATH"

    cat > "$PORTSENTRY_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - portsentry management (Phase 3). Two root-owned helpers, each the security
# boundary: unblock-ip validates + removes ONE IP from iptables/ip6tables + /etc/hosts.deny;
# portsentry-setup validates + installs/configures portsentry. noexec is intentionally omitted (the
# helpers are vetted scripts that must exec iptables/grep/apt-get/sed/systemctl).
Cmnd_Alias AETHEUS_PORTSENTRY = $UNBLOCK_HELPER_PATH, $PORTSENTRY_SETUP_HELPER_PATH
Defaults!AETHEUS_PORTSENTRY timestamp_timeout=0
Defaults!AETHEUS_PORTSENTRY env_reset
Defaults!AETHEUS_PORTSENTRY secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_PORTSENTRY
EOF
    chmod 440 "$PORTSENTRY_MANAGE_SUDOERS_FILE"
    visudo -cf "$PORTSENTRY_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid portsentry-manage sudoers syntax"
        rm -f "$PORTSENTRY_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

# Phase 3 (option B) - systemctl enable --now on a FIXED list of pre-existing units. `systemctl
# enable` is otherwise forbidden (mints new root services); permitted here ONLY argv-exact, no
# wildcards, against units that already exist. Mirrors the AETHEUS_SYSTEMCTL unit set.
# The allow-list remains argv-exact and unit-scoped.
write_service_enable_sudoers() {
    log_info "Configuring service-enable sudoers (Phase 3 option B, fixed-unit allow-list)..."
    cat > "$SERVICE_ENABLE_SUDOERS_FILE" <<EOF
# Aetheus Agent - enable-on-boot for the server-config YAML deploy. Each entry is an EXACT
# fully-resolved argv (systemctl enable --now <unit>) for a pre-existing managed unit. No wildcards.
Cmnd_Alias AETHEUS_SERVICE_ENABLE = \\
    /bin/systemctl enable --now apache2, \\
    /bin/systemctl enable --now nginx, \\
    /bin/systemctl enable --now postfix, \\
    /bin/systemctl enable --now dovecot, \\
    /bin/systemctl enable --now ts3server, \\
    /bin/systemctl enable --now portsentry, \\
    /bin/systemctl enable --now rkhunter, \\
    /bin/systemctl enable --now cron, \\
    /bin/systemctl enable --now crond

Defaults!AETHEUS_SERVICE_ENABLE noexec
Defaults!AETHEUS_SERVICE_ENABLE timestamp_timeout=0
Defaults!AETHEUS_SERVICE_ENABLE env_reset
Defaults!AETHEUS_SERVICE_ENABLE secure_path="/usr/sbin:/usr/bin:/sbin:/bin"

$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_SERVICE_ENABLE
EOF
    chmod 440 "$SERVICE_ENABLE_SUDOERS_FILE"
    visudo -cf "$SERVICE_ENABLE_SUDOERS_FILE" || {
        log_error "Invalid service-enable sudoers syntax"
        rm -f "$SERVICE_ENABLE_SUDOERS_FILE"
        exit 1
    }
}

# S-FEAT-W8KN - install/uninstall a managed OS package via apt-get. Each entry is an EXACT argv for a
# package in the closed allow-list (kept in lockstep with Aetheus.Shared.Constants.ManageablePackages
# by ManageablePackagesSudoersAuditTests). Names are the real apt package names (docker.io / mysql-server
# / mariadb-server / mongodb-org), NOT the service keys. `apt-get update` is allow-listed so a fresh box
# with an empty index can refresh before install. INSTALL covers the full list; REMOVE is restricted to
# the Removable subset (protected packages - databases, ufw/fail2ban, docker - cannot be uninstalled). No
# wildcards: anything outside the list is refused by sudo at the OS level. noexec is intentionally NOT set
# - apt-get must exec dpkg + maintainer scripts; the argv-exact allow-list (no -o / config-file flags) is
# the boundary.
write_package_manage_sudoers() {
    log_info "Configuring package-manage sudoers (S-FEAT-W8KN, fixed-package allow-list)..."
    cat > "$PACKAGE_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - managed-package install/uninstall. Each entry is an EXACT fully-resolved argv;
# no wildcards. The package set mirrors Aetheus.Shared.Constants.ManageablePackages (All for install,
# Removable for remove).
Cmnd_Alias AETHEUS_PACKAGE = \\
    /usr/bin/apt-get update, \\
    /usr/bin/apt-get install -y nginx, \\
    /usr/bin/apt-get install -y apache2, \\
    /usr/bin/apt-get install -y docker.io, \\
    /usr/bin/apt-get install -y fail2ban, \\
    /usr/bin/apt-get install -y rkhunter, \\
    /usr/bin/apt-get install -y ufw, \\
    /usr/bin/apt-get install -y portsentry, \\
    /usr/bin/apt-get install -y postfix, \\
    /usr/bin/apt-get install -y dovecot-core, \\
    /usr/bin/apt-get install -y mysql-server, \\
    /usr/bin/apt-get install -y mariadb-server, \\
    /usr/bin/apt-get install -y postgresql, \\
    /usr/bin/apt-get install -y redis-server, \\
    /usr/bin/apt-get install -y mongodb-org, \\
    /usr/bin/apt-get install -y certbot, \\
    /usr/bin/apt-get remove -y nginx, \\
    /usr/bin/apt-get remove -y apache2, \\
    /usr/bin/apt-get remove -y rkhunter, \\
    /usr/bin/apt-get remove -y portsentry, \\
    /usr/bin/apt-get remove -y postfix, \\
    /usr/bin/apt-get remove -y dovecot-core, \\
    /usr/bin/apt-get remove -y certbot

Defaults!AETHEUS_PACKAGE timestamp_timeout=0
Defaults!AETHEUS_PACKAGE env_reset
Defaults!AETHEUS_PACKAGE secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
# Keep DEBIAN_FRONTEND so the agent can run apt non-interactively: postfix/dovecot (and anything that
# pulls them, e.g. rkhunter) otherwise block on a debconf prompt with no controlling tty and exit 100.
Defaults!AETHEUS_PACKAGE env_keep += "DEBIAN_FRONTEND"

$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_PACKAGE
EOF
    chmod 440 "$PACKAGE_MANAGE_SUDOERS_FILE"
    visudo -cf "$PACKAGE_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid package-manage sudoers syntax"
        rm -f "$PACKAGE_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

write_patch_manage_sudoers() {
    log_info "Configuring patch-manage sudoers (PLAN-006 4.1, apt-get upgrade, argv-exact)..."
    cat > "$PATCH_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - fleet OS patching. A single EXACT argv (no wildcards): whole-box apt-get upgrade.
# The agent runs a non-mutating 'apt-get -s upgrade' simulation FIRST (unprivileged, no sudo) and
# aborts on any critical package (Aetheus.Shared.Constants.CriticalPackages) before ever calling this.
Cmnd_Alias AETHEUS_PATCH = \\
    /usr/bin/apt-get upgrade -y

Defaults!AETHEUS_PATCH timestamp_timeout=0
Defaults!AETHEUS_PATCH env_reset
Defaults!AETHEUS_PATCH secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
# Keep DEBIAN_FRONTEND so the upgrade runs non-interactively (no debconf tty prompt -> exit 100).
Defaults!AETHEUS_PATCH env_keep += "DEBIAN_FRONTEND"

$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_PATCH
EOF
    chmod 440 "$PATCH_MANAGE_SUDOERS_FILE"
    visudo -cf "$PATCH_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid patch-manage sudoers syntax"
        rm -f "$PATCH_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

write_firewall_manage() {
    log_info "Configuring firewall-manage helper + sudoers (PLAN-006 4.2, ufw)..."
    ensure_helper_dir
    # Root-owned helper = the security boundary: it re-validates every argument, re-enforces anti-lockout
    # with the REAL SSH port, and never evals. Quoted heredoc marker => no expansion inside.
    cat > "$FIREWALL_HELPER_PATH" <<'FWEOF'
#!/bin/sh
# Aetheus firewall helper (PLAN-006 4.2). Root-owned; the agent may exec but not modify it.
set -u
UFW=/usr/sbin/ufw
# Detect the real SSH port so anti-lockout protects the actual admin port, not just 22.
# `sshd -T` is authoritative and includes the /etc/ssh/sshd_config.d/*.conf drop-ins (Ubuntu 22.04+
# ships the effective Port there, not in the main file); fall back to scanning the main config only.
ADMIN_PORT="$(/usr/sbin/sshd -T 2>/dev/null | awk '/^port[[:space:]]/ {print $2; exit}')"
[ -n "$ADMIN_PORT" ] || ADMIN_PORT="$(awk '/^[Pp]ort[[:space:]]+[0-9]+/ {print $2; exit}' /etc/ssh/sshd_config 2>/dev/null)"
[ -n "$ADMIN_PORT" ] || ADMIN_PORT=22

fw_valid_source() {
    [ "$1" = "any" ] && return 0
    echo "$1" | grep -Eq '^[0-9A-Fa-f:.]+(/[0-9]{1,3})?$'
}

action="${1:-}"
case "$action" in
    status)
        exec "$UFW" status numbered
        ;;
    enable)
        # Never lock out: allow the admin port before the default-deny policy takes effect.
        "$UFW" allow "${ADMIN_PORT}/tcp" >/dev/null 2>&1
        exec "$UFW" --force enable
        ;;
    disable)
        exec "$UFW" --force disable
        ;;
    allow|deny|delete)
        port="${2:-}"; proto="${3:-}"; source="${4:-any}"
        echo "$port" | grep -Eq '^[0-9]{1,5}$' || { echo "invalid port" >&2; exit 2; }
        [ "$port" -ge 1 ] && [ "$port" -le 65535 ] || { echo "port out of range" >&2; exit 2; }
        echo "$proto" | grep -Eq '^(tcp|udp)$' || { echo "invalid protocol" >&2; exit 2; }
        fw_valid_source "$source" || { echo "invalid source" >&2; exit 2; }
        # Anti-lockout: refuse to close (deny/delete) the administration port.
        if [ "$action" != "allow" ] && [ "$port" = "$ADMIN_PORT" ]; then
            echo "refused: would close the administration port $ADMIN_PORT" >&2
            exit 3
        fi
        if [ "$source" = "any" ]; then
            case "$action" in
                allow) exec "$UFW" allow "${port}/${proto}" ;;
                deny)  exec "$UFW" deny  "${port}/${proto}" ;;
                delete)
                    "$UFW" --force delete allow "${port}/${proto}" 2>/dev/null
                    exec "$UFW" --force delete deny "${port}/${proto}"
                    ;;
            esac
        else
            case "$action" in
                allow) exec "$UFW" allow from "$source" to any port "$port" proto "$proto" ;;
                deny)  exec "$UFW" deny  from "$source" to any port "$port" proto "$proto" ;;
                delete)
                    "$UFW" --force delete allow from "$source" to any port "$port" proto "$proto" 2>/dev/null
                    exec "$UFW" --force delete deny from "$source" to any port "$port" proto "$proto"
                    ;;
            esac
        fi
        ;;
    *)
        echo "unknown firewall command" >&2
        exit 2
        ;;
esac
FWEOF
    chown root:root "$FIREWALL_HELPER_PATH"
    chmod 755 "$FIREWALL_HELPER_PATH"

    cat > "$FIREWALL_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - firewall (ufw) management. Path-only grant of the root-owned aetheus-firewall helper,
# which IS the security boundary: it re-validates port/protocol/source and re-enforces anti-lockout.
Cmnd_Alias AETHEUS_FIREWALL = $FIREWALL_HELPER_PATH
Defaults!AETHEUS_FIREWALL timestamp_timeout=0
Defaults!AETHEUS_FIREWALL env_reset
Defaults!AETHEUS_FIREWALL secure_path="/usr/sbin:/usr/bin:/sbin:/bin"

$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_FIREWALL
EOF
    chmod 440 "$FIREWALL_MANAGE_SUDOERS_FILE"
    visudo -cf "$FIREWALL_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid firewall-manage sudoers syntax"
        rm -f "$FIREWALL_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

# S-FEAT-W8KN - mail setup. Deploys the root-owned mail-setup helper (the security boundary: it
# re-validates every argument, reads the admin password from stdin, and performs the full
# postfix/dovecot/opendkim install+configure for ONE domain) and a NOPASSWD sudoers grant for exactly
# that binary. The old MailService.SetupAsync enqueued a free-form apt/postconf/systemctl shell pipeline
# that always failed for the non-root agent; this is the typed OperationKind.MailSetup path. noexec is
# intentionally NOT set on this Cmnd_Alias: the helper is a vetted script that must exec apt-get /
# postconf / systemctl / doveadm - it is itself the boundary.
write_mail_setup() {
    log_info "Configuring mail-setup helper + sudoers (S-FEAT-W8KN, root-owned helper boundary)..."
    ensure_helper_dir
    cat > "$MAIL_SETUP_HELPER_PATH" <<'HELPER_EOF'
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Aetheus Agent mail-setup helper - root-owned, invoked via /etc/sudoers.d/aetheus-mail (NOPASSWD).
# Installs + configures postfix/dovecot/opendkim for ONE domain. Re-validates every argument; reads the
# admin password from stdin (never argv). Never evals input.
set -eu

die() { echo "$1" >&2; exit 1; }
valid_domain() { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$'; }
valid_email()  { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$'; }
valid_sel()    { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9_-]+$'; }
valid_quota()  { printf '%s' "$1" | grep -Eq '^[0-9]{1,7}$'; }

hostname="${1:-}"
domain="${2:-}"
selector="${3:-}"
email="${4:-}"
quota="${5:-}"

valid_domain "$hostname" || die "invalid hostname"
valid_domain "$domain"   || die "invalid domain"
valid_sel    "$selector" || die "invalid selector"
valid_email  "$email"    || die "invalid email"
valid_quota  "$quota"    || die "invalid quota"

# Admin password from stdin (one newline-terminated line). Never on the argv.
IFS= read -r password || die "no password on stdin"
[ -n "$password" ] || die "empty password"
printf '%s' "$password" | LC_ALL=C grep -q '[[:cntrl:]]' && die "password has control chars"
[ "${#password}" -ge 8 ] || die "password too short"

localpart="${email%@*}"

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq || true
apt-get install -y -qq postfix dovecot-core dovecot-imapd dovecot-pop3d dovecot-lmtpd opendkim opendkim-tools

# Postfix main.cf (values are validated identifiers; postconf -e takes a single key=value argv token).
postconf -e "myhostname = $hostname"
postconf -e "mydomain = $domain"
postconf -e "mydestination = localhost"
postconf -e "virtual_mailbox_domains = $domain"
postconf -e "virtual_mailbox_base = /var/mail/vhosts"
postconf -e "virtual_mailbox_maps = hash:/etc/postfix/vmailbox"
postconf -e "virtual_minimum_uid = 1000"
postconf -e "virtual_uid_maps = static:5000"
postconf -e "virtual_gid_maps = static:5000"
postconf -e "virtual_transport = lmtp:unix:private/dovecot-lmtp"
postconf -e "smtpd_tls_cert_file = /etc/ssl/certs/ssl-cert-snakeoil.pem"
postconf -e "smtpd_tls_key_file = /etc/ssl/private/ssl-cert-snakeoil.key"
postconf -e "smtpd_tls_security_level = may"
postconf -e "smtp_tls_security_level = may"
postconf -e "smtpd_sasl_type = dovecot"
postconf -e "smtpd_sasl_path = private/auth"
postconf -e "smtpd_sasl_auth_enable = yes"
postconf -e "smtpd_recipient_restrictions = permit_sasl_authenticated,permit_mynetworks,reject_unauth_destination"
postconf -e "milter_default_action = accept"
postconf -e "milter_protocol = 6"
postconf -e "smtpd_milters = inet:localhost:8891"
postconf -e "non_smtpd_milters = inet:localhost:8891"

# vmail user (idempotent).
groupadd -g 5000 vmail 2>/dev/null || true
useradd -g vmail -u 5000 -d /var/mail/vhosts -s /usr/sbin/nologin vmail 2>/dev/null || true
mkdir -p "/var/mail/vhosts/$domain"
chown -R vmail:vmail /var/mail/vhosts

# Virtual mailbox map.
umask 022
printf '%s %s/%s/\n' "$email" "$domain" "$localpart" > /etc/postfix/vmailbox
postmap /etc/postfix/vmailbox

# Dovecot users file. Feed the password to `doveadm pw` over stdin (it prompts twice) rather than
# via `-p`: an argv token lands in /proc/<pid>/cmdline and is visible to `ps`/journald for the life
# of the process. The pipe keeps the cleartext off the process list entirely.
hash="$(printf '%s\n%s\n' "$password" "$password" | doveadm pw -s SHA512-CRYPT)"
umask 077
printf '%s:%s\n' "$email" "$hash" > /etc/dovecot/users
chmod 600 /etc/dovecot/users

# OpenDKIM.
mkdir -p /etc/opendkim/keys
opendkim-genkey -s "$selector" -d "$domain" -D /etc/opendkim/keys/
printf '%s._domainkey.%s %s:%s:/etc/opendkim/keys/%s.private\n' "$selector" "$domain" "$domain" "$selector" "$selector" > /etc/opendkim/KeyTable
printf '*@%s %s._domainkey.%s\n' "$domain" "$selector" "$domain" > /etc/opendkim/SigningTable
printf '127.0.0.1\nlocalhost\n' > /etc/opendkim/TrustedHosts
chown -R opendkim:opendkim /etc/opendkim/keys
chmod 600 "/etc/opendkim/keys/$selector.private"

mkdir -p /etc/dovecot/conf.d

# Enable + (re)start services.
systemctl enable postfix dovecot opendkim
systemctl restart opendkim
systemctl restart postfix
systemctl restart dovecot

echo "Mail server setup completed successfully for $domain (quota ${quota}MB)"
HELPER_EOF
    chown root:root "$MAIL_SETUP_HELPER_PATH"
    chmod 755 "$MAIL_SETUP_HELPER_PATH"

    # S-FEAT-W8KN incremental ops helper (add-domain / add-account / add-alias / dkim-rotate). Same
    # root-owned boundary recipe as mail-setup: re-validates every argument, reads an account password
    # from stdin (never argv), and operates on the stack mail-setup provisioned. Never evals input. The
    # agent (MailOperationExecutor.ManageHelperPath) shells out to exactly this binary via sudo -n.
    cat > "$MAIL_MANAGE_HELPER_PATH" <<'MANAGE_EOF'
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Aetheus Agent mail-manage helper - root-owned, invoked via /etc/sudoers.d/aetheus-mail (NOPASSWD).
# Incremental management of an already-provisioned mail stack: add-domain, add-account, add-alias,
# dkim-rotate, change-password, remove-domain, delete-account, remove-alias, dkim-read. Re-validates
# every argument; reads an account password from stdin (never argv). Never evals input. First argv token
# is a fixed sub-command.
set -eu

die() { echo "$1" >&2; exit 1; }
valid_domain() { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$'; }
valid_email()  { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$'; }
valid_sel()    { printf '%s' "$1" | grep -Eq '^[a-zA-Z0-9_-]+$'; }
valid_quota()  { printf '%s' "$1" | grep -Eq '^[0-9]{1,7}$'; }

cmd="${1:-}"
[ -n "$cmd" ] || die "no sub-command"

case "$cmd" in
  add-domain)
    domain="${2:-}"
    valid_domain "$domain" || die "invalid domain"
    mkdir -p "/var/mail/vhosts/$domain"
    chown -R vmail:vmail "/var/mail/vhosts/$domain"
    cur="$(postconf -h virtual_mailbox_domains 2>/dev/null || echo '')"
    case " $cur " in
      *" $domain "*) : ;;
      *) if [ -n "$cur" ]; then postconf -e "virtual_mailbox_domains = $cur, $domain"; else postconf -e "virtual_mailbox_domains = $domain"; fi ;;
    esac
    systemctl reload postfix
    echo "Domain $domain added"
    ;;
  add-account)
    email="${2:-}"; domain="${3:-}"; quota="${4:-}"
    valid_email "$email"   || die "invalid email"
    valid_domain "$domain" || die "invalid domain"
    valid_quota "$quota"   || die "invalid quota"
    case "$email" in *"@$domain") : ;; *) die "email not in domain" ;; esac
    IFS= read -r password || die "no password on stdin"
    [ -n "$password" ] || die "empty password"
    printf '%s' "$password" | LC_ALL=C grep -q '[[:cntrl:]]' && die "password has control chars"
    [ "${#password}" -ge 8 ] || die "password too short"
    localpart="${email%@*}"
    mkdir -p "/var/mail/vhosts/$domain/$localpart"
    chown -R vmail:vmail "/var/mail/vhosts/$domain"
    umask 022
    touch /etc/postfix/vmailbox
    grep -v "^$email " /etc/postfix/vmailbox > /etc/postfix/vmailbox.tmp 2>/dev/null || true
    printf '%s %s/%s/\n' "$email" "$domain" "$localpart" >> /etc/postfix/vmailbox.tmp
    mv /etc/postfix/vmailbox.tmp /etc/postfix/vmailbox
    postmap /etc/postfix/vmailbox
    hash="$(printf '%s\n%s\n' "$password" "$password" | doveadm pw -s SHA512-CRYPT)"
    umask 077
    touch /etc/dovecot/users
    grep -v "^$email:" /etc/dovecot/users > /etc/dovecot/users.tmp 2>/dev/null || true
    printf '%s:%s\n' "$email" "$hash" >> /etc/dovecot/users.tmp
    mv /etc/dovecot/users.tmp /etc/dovecot/users
    chmod 600 /etc/dovecot/users
    echo "Account $email added (quota ${quota}MB)"
    ;;
  add-alias)
    email="${2:-}"; destination="${3:-}"
    valid_email "$email"       || die "invalid alias"
    valid_email "$destination" || die "invalid destination"
    umask 022
    touch /etc/postfix/virtual
    grep -v "^$email " /etc/postfix/virtual > /etc/postfix/virtual.tmp 2>/dev/null || true
    printf '%s %s\n' "$email" "$destination" >> /etc/postfix/virtual.tmp
    mv /etc/postfix/virtual.tmp /etc/postfix/virtual
    postmap /etc/postfix/virtual
    cur="$(postconf -h virtual_alias_maps 2>/dev/null || echo '')"
    case "$cur" in
      *hash:/etc/postfix/virtual*) : ;;
      *) postconf -e "virtual_alias_maps = hash:/etc/postfix/virtual" ;;
    esac
    systemctl reload postfix
    echo "Alias $email -> $destination added"
    ;;
  dkim-rotate)
    domain="${2:-}"; selector="${3:-}"
    valid_domain "$domain" || die "invalid domain"
    valid_sel "$selector"  || die "invalid selector"
    mkdir -p /etc/opendkim/keys
    opendkim-genkey -s "$selector" -d "$domain" -D /etc/opendkim/keys/
    chown opendkim:opendkim "/etc/opendkim/keys/$selector.private"
    chmod 600 "/etc/opendkim/keys/$selector.private"
    touch /etc/opendkim/KeyTable /etc/opendkim/SigningTable
    grep -v "\.${domain} " /etc/opendkim/KeyTable > /etc/opendkim/KeyTable.tmp 2>/dev/null || true
    printf '%s._domainkey.%s %s:%s:/etc/opendkim/keys/%s.private\n' "$selector" "$domain" "$domain" "$selector" "$selector" >> /etc/opendkim/KeyTable.tmp
    mv /etc/opendkim/KeyTable.tmp /etc/opendkim/KeyTable
    grep -v "^\*@$domain " /etc/opendkim/SigningTable > /etc/opendkim/SigningTable.tmp 2>/dev/null || true
    printf '*@%s %s._domainkey.%s\n' "$domain" "$selector" "$domain" >> /etc/opendkim/SigningTable.tmp
    mv /etc/opendkim/SigningTable.tmp /etc/opendkim/SigningTable
    systemctl restart opendkim
    echo "DKIM rotated for $domain (selector $selector)"
    ;;
  change-password)
    # S-TECH-MCPW: change an existing mailbox password. The new password arrives on stdin (never argv),
    # is validated, hashed via doveadm pw over stdin, and rewritten idempotently into /etc/dovecot/users.
    email="${2:-}"
    valid_email "$email" || die "invalid email"
    IFS= read -r password || die "no password on stdin"
    [ -n "$password" ] || die "empty password"
    printf '%s' "$password" | LC_ALL=C grep -q '[[:cntrl:]]' && die "password has control chars"
    [ "${#password}" -ge 8 ] || die "password too short"
    grep -q "^$email:" /etc/dovecot/users 2>/dev/null || die "account does not exist"
    hash="$(printf '%s\n%s\n' "$password" "$password" | doveadm pw -s SHA512-CRYPT)"
    umask 077
    touch /etc/dovecot/users
    grep -v "^$email:" /etc/dovecot/users > /etc/dovecot/users.tmp 2>/dev/null || true
    printf '%s:%s\n' "$email" "$hash" >> /etc/dovecot/users.tmp
    mv /etc/dovecot/users.tmp /etc/dovecot/users
    chmod 600 /etc/dovecot/users
    echo "Password changed for $email"
    ;;
  remove-domain)
    # Removal counterpart of add-domain: drop the domain from virtual_mailbox_domains, reload postfix.
    # Rebuilds the list by field so no sed/regex address is interpolated.
    domain="${2:-}"
    valid_domain "$domain" || die "invalid domain"
    cur="$(postconf -h virtual_mailbox_domains 2>/dev/null || echo '')"
    new=""
    for d in $(printf '%s' "$cur" | tr ',' ' '); do
      [ "$d" = "$domain" ] && continue
      if [ -n "$new" ]; then new="$new, $d"; else new="$d"; fi
    done
    postconf -e "virtual_mailbox_domains = $new"
    systemctl reload postfix
    echo "Domain $domain removed"
    ;;
  delete-account)
    # Removal counterpart of add-account: drop the mailbox line from vmailbox + dovecot users, re-map.
    email="${2:-}"; domain="${3:-}"
    valid_email "$email"   || die "invalid email"
    valid_domain "$domain" || die "invalid domain"
    case "$email" in *"@$domain") : ;; *) die "email not in domain" ;; esac
    if [ -f /etc/postfix/vmailbox ]; then
      grep -v "^$email " /etc/postfix/vmailbox > /etc/postfix/vmailbox.tmp 2>/dev/null || true
      mv /etc/postfix/vmailbox.tmp /etc/postfix/vmailbox
      postmap /etc/postfix/vmailbox
    fi
    if [ -f /etc/dovecot/users ]; then
      grep -v "^$email:" /etc/dovecot/users > /etc/dovecot/users.tmp 2>/dev/null || true
      mv /etc/dovecot/users.tmp /etc/dovecot/users
      chmod 600 /etc/dovecot/users
    fi
    echo "Account $email deleted"
    ;;
  remove-alias)
    # Removal counterpart of add-alias: drop the alias line from /etc/postfix/virtual, re-map, reload.
    email="${2:-}"
    valid_email "$email" || die "invalid alias"
    if [ -f /etc/postfix/virtual ]; then
      grep -v "^$email " /etc/postfix/virtual > /etc/postfix/virtual.tmp 2>/dev/null || true
      mv /etc/postfix/virtual.tmp /etc/postfix/virtual
      postmap /etc/postfix/virtual
      systemctl reload postfix
    fi
    echo "Alias $email removed"
    ;;
  dkim-read)
    # Read a domain's DKIM public key (DNS TXT) by selector. Root reads the world-relevant .txt and
    # prints it to stdout; the backend surfaces it as the DNS record to publish.
    selector="${2:-}"
    valid_sel "$selector" || die "invalid selector"
    keyfile="/etc/opendkim/keys/$selector.txt"
    [ -f "$keyfile" ] || die "no DKIM key for selector $selector"
    cat "$keyfile"
    ;;
  *)
    die "unknown sub-command: $cmd"
    ;;
esac
MANAGE_EOF
    chown root:root "$MAIL_MANAGE_HELPER_PATH"
    chmod 755 "$MAIL_MANAGE_HELPER_PATH"

    cat > "$MAIL_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - mail setup + incremental management (S-FEAT-W8KN). Each helper is the security
# boundary: root-owned, agent-non-writable, re-validates every argument, reads passwords from stdin.
# noexec is intentionally omitted (the helpers must exec apt-get / postconf / postmap / systemctl /
# doveadm / opendkim-genkey). mail-setup provisions one domain; mail-manage does the incremental ops.
Cmnd_Alias AETHEUS_MAIL = $MAIL_SETUP_HELPER_PATH
Cmnd_Alias AETHEUS_MAIL_MANAGE = $MAIL_MANAGE_HELPER_PATH
Defaults!AETHEUS_MAIL timestamp_timeout=0
Defaults!AETHEUS_MAIL env_reset
Defaults!AETHEUS_MAIL secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
Defaults!AETHEUS_MAIL_MANAGE timestamp_timeout=0
Defaults!AETHEUS_MAIL_MANAGE env_reset
Defaults!AETHEUS_MAIL_MANAGE secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_MAIL, AETHEUS_MAIL_MANAGE
EOF
    chmod 440 "$MAIL_MANAGE_SUDOERS_FILE"
    visudo -cf "$MAIL_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid mail-setup sudoers syntax"
        rm -f "$MAIL_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

# TeamSpeak setup. Deploys the root-owned teamspeak-setup helper (the security boundary: it re-validates
# every argument and performs the full TS3 server install - system user, tarball download/extract,
# license, systemd unit, ports, and serveradmin-credential extraction for the unprivileged agent) and a
# NOPASSWD sudoers grant for exactly that binary. The old TeamspeakService.SetupAsync enqueued a free-form
# useradd/cat>/etc/systemd/systemctl shell pipeline that always failed for the non-root agent; this is the
# typed OperationKind.TeamspeakSetup path. noexec is intentionally NOT set: the helper must exec apt-get /
# curl / tar / systemctl / useradd - it is itself the boundary.
write_teamspeak_setup() {
    log_info "Configuring teamspeak-setup helper + sudoers (root-owned helper boundary)..."
    ensure_helper_dir
    cat > "$TEAMSPEAK_SETUP_HELPER_PATH" <<'HELPER_EOF'
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Aetheus Agent teamspeak-setup helper - root-owned, invoked via /etc/sudoers.d/aetheus-teamspeak
# (NOPASSWD). Installs a TeamSpeak 3 server at <install_path> with <voice_port>/<query_port>. Re-validates
# every argument. Never evals input. Real operations only - exits non-zero on any failure (no fake success).
set -eu

TS_VERSION="3.13.7"
TS_URL="https://files.teamspeak-services.com/releases/server/${TS_VERSION}/teamspeak3-server_linux_amd64-${TS_VERSION}.tar.bz2"
CRED_DIR="/opt/aetheus-agent/teamspeak"
CRED_FILE="$CRED_DIR/query-credentials"

die() { echo "$1" >&2; exit 1; }
# S-TECH-TSPR: reject any ".." segment (path traversal) before the charclass check - POSIX ERE has no
# lookahead, so the "no .." rule is a separate guard, mirroring the backend TeamspeakInstallPathRegex.
valid_path() { case "$1" in *..*) return 1 ;; esac; printf '%s' "$1" | grep -Eq '^/[a-zA-Z0-9._/-]{1,127}$'; }
valid_port() { printf '%s' "$1" | grep -Eq '^[0-9]{1,5}$' && [ "$1" -ge 1 ] && [ "$1" -le 65535 ]; }

install_path="${1:-}"
voice_port="${2:-9987}"
query_port="${3:-10011}"

valid_path "$install_path" || die "invalid install path"
valid_port "$voice_port"   || die "invalid voice port"
valid_port "$query_port"   || die "invalid query port"

# Whoever invoked sudo is the unprivileged agent user; the credential file must end up readable by it
# (the agent's TeamspeakServerQuery executor opens the TCP query connection itself). Fall back to the
# stock user name if SUDO_USER is somehow unset.
agent_user="${SUDO_USER:-aetheus-agent}"

# S-TECH-TSID: idempotent re-run guard. If a server is already installed at this path (its binary is
# present) or the unit is live, do NOT re-download/re-extract - that would clobber a running TS3 server
# and its data. A retry from the UI (or a detection false-positive) must be a no-op, not destructive.
if [ -x "$install_path/ts3server" ] || systemctl is-active --quiet teamspeak3 2>/dev/null; then
    echo "TeamSpeak already installed at $install_path - leaving it untouched (idempotent)."
    systemctl start teamspeak3 2>/dev/null || true
    exit 0
fi

# 0. Ensure the tools the download/extract need are present. The TS3 archive is .tar.bz2, so tar needs
# the bzip2 binary - a minimal/bare box may ship neither it nor curl. Install only what's missing.
# S-TECH-APTG: DPkg::Lock::Timeout makes apt WAIT for a held dpkg/apt lock (a concurrent batch install)
# instead of failing immediately with exit 100.
if ! command -v bzip2 >/dev/null 2>&1 || ! command -v curl >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get -o DPkg::Lock::Timeout=300 update -qq || true
    apt-get -o DPkg::Lock::Timeout=300 install -y bzip2 curl ca-certificates
fi

# 1. System user for the daemon (idempotent).
useradd -r -m -d /opt/teamspeak -s /usr/sbin/nologin teamspeak 2>/dev/null || true

# 2. Download the server tarball to a temp file, optionally verify its integrity, then extract.
# S-TECH-TSCK: TS_VERSION is the single pinned-version constant; download to a temp file (instead of
# piping curl straight into tar) so the archive CAN be checksum-verified before it is trusted. Set
# TS_SHA256 to the published SHA-256 of the tarball to fail-closed on a tampered/rotted download; left
# empty it skips the check (HTTPS + a version-pinned URL still apply) but logs that it is unverified.
TS_SHA256=""
mkdir -p "$install_path"
ts_tmp="$(mktemp)"
trap 'rm -f "$ts_tmp"' EXIT
curl -fsSL "$TS_URL" -o "$ts_tmp"
if [ -n "$TS_SHA256" ]; then
    echo "$TS_SHA256  $ts_tmp" | sha256sum -c - || die "TeamSpeak tarball checksum mismatch - refusing to install"
else
    echo "WARNING: TS_SHA256 is unset - installing TeamSpeak $TS_VERSION without checksum verification." >&2
fi
tar xjf "$ts_tmp" -C "$install_path" --strip-components=1
chown -R teamspeak:teamspeak "$install_path"

# 3. Accept the license non-interactively (required since 3.1).
touch "$install_path/.ts3server_license_accepted"

# 4. ts3server.ini with the requested ports.
printf 'default_voice_port=%s\nquery_port=%s\n' "$voice_port" "$query_port" > "$install_path/ts3server.ini"
chown teamspeak:teamspeak "$install_path/ts3server.ini"

# 5. systemd unit (matches the TeamspeakCollector's detection of the teamspeak3 unit).
cat > /etc/systemd/system/teamspeak3.service <<UNIT
[Unit]
Description=TeamSpeak 3 Server
After=network.target

[Service]
Type=forking
User=teamspeak
WorkingDirectory=$install_path
ExecStart=$install_path/ts3server_startscript.sh start
ExecStop=$install_path/ts3server_startscript.sh stop
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
UNIT

# 6. Enable + start.
systemctl daemon-reload
systemctl enable teamspeak3
systemctl start teamspeak3

# 7. Extract the auto-generated ServerQuery admin password from the first-run log and hand it to the
# agent user. Best-effort: a miss must not fail the install (the server is up either way), so relax -e
# for this block. TS3 prints: loginname= "serveradmin", password= "XXXX".
mkdir -p "$CRED_DIR"
set +e
for _ in 1 2 3 4 5 6; do
    pw="$(grep -hoP 'password=\s*"\K[^"]+' "$install_path"/logs/ts3server_*.log 2>/dev/null | head -1)"
    [ -n "$pw" ] && break
    sleep 2
done
set -e
if [ -n "${pw:-}" ]; then
    printf '%s' "$pw" > "$CRED_FILE"
    chmod 600 "$CRED_FILE"
fi
chown -R "$agent_user:$agent_user" "$CRED_DIR" 2>/dev/null || true

echo "TeamSpeak 3 server ${TS_VERSION} installed at $install_path (voice ${voice_port}, query ${query_port})"
HELPER_EOF
    chown root:root "$TEAMSPEAK_SETUP_HELPER_PATH"
    chmod 755 "$TEAMSPEAK_SETUP_HELPER_PATH"

    cat > "$TEAMSPEAK_SETUP_SUDOERS_FILE" <<EOF
# Aetheus Agent - TeamSpeak install. The helper is the security boundary: root-owned,
# agent-non-writable, re-validates every argument, and installs exactly one TS3 server. noexec is
# intentionally omitted (the helper must exec apt-get / curl / tar / systemctl / useradd).
Cmnd_Alias AETHEUS_TEAMSPEAK = $TEAMSPEAK_SETUP_HELPER_PATH
Defaults!AETHEUS_TEAMSPEAK timestamp_timeout=0
Defaults!AETHEUS_TEAMSPEAK env_reset
Defaults!AETHEUS_TEAMSPEAK secure_path="/usr/sbin:/usr/bin:/sbin:/bin"
$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_TEAMSPEAK
EOF
    chmod 440 "$TEAMSPEAK_SETUP_SUDOERS_FILE"
    visudo -cf "$TEAMSPEAK_SETUP_SUDOERS_FILE" || {
        log_error "Invalid teamspeak-setup sudoers syntax"
        rm -f "$TEAMSPEAK_SETUP_SUDOERS_FILE"
        exit 1
    }
}

# Items #10.2/#10.3 - same recipe as Apache: argv-exact allow-list + Defaults noexec /
# timestamp_timeout=0 / env_reset / secure_path. Three commands strictly: check, update,
# propupd. Any other rkhunter flag (e.g. --logfile, --configfile) is refused by sudo.
write_rkhunter_manage_sudoers() {
    log_info "Configuring RKHunter-manage sudoers (items #10.2/#10.3, ultra-strict allow-list)..."
    cat > "$RKHUNTER_MANAGE_SUDOERS_FILE" <<EOF
# Aetheus Agent - RKHunter management with an argv-exact privilege boundary.

# 1. Run scan (rkhunter --check needs root to read /var/lib/rkhunter/db + scan certain paths).
# 2. Update signature DB (rkhunter --update fetches new mirror data, writes /var/lib/rkhunter).
# 3. Refresh baseline (rkhunter --propupd recomputes file checksums after legitimate upgrades).
# Every entry uses an EXACT fully-resolved binary path + fixed argv string. No wildcards.
Cmnd_Alias AETHEUS_RKHUNTER_MANAGE = \\
    /usr/bin/rkhunter --check --skip-keypress --nocolors --report-warnings-only, \\
    /usr/bin/rkhunter --update --nocolors, \\
    /usr/bin/rkhunter --propupd --nocolors

Defaults!AETHEUS_RKHUNTER_MANAGE noexec
Defaults!AETHEUS_RKHUNTER_MANAGE timestamp_timeout=0
Defaults!AETHEUS_RKHUNTER_MANAGE env_reset
Defaults!AETHEUS_RKHUNTER_MANAGE secure_path="/usr/sbin:/usr/bin:/sbin:/bin"

$AGENT_USER ALL=(root) NOPASSWD: AETHEUS_RKHUNTER_MANAGE
EOF
    chmod 440 "$RKHUNTER_MANAGE_SUDOERS_FILE"
    visudo -cf "$RKHUNTER_MANAGE_SUDOERS_FILE" || {
        log_error "Invalid RKHunter-manage sudoers syntax"
        rm -f "$RKHUNTER_MANAGE_SUDOERS_FILE"
        exit 1
    }
}

print_posture_summary() {
    log_info ""
    [ "$MODULE_PIPELINE_RUNNER" -eq 1 ] \
        && log_info "  Module pipeline-runner: ON (dotnet/git/sshpass installed; no elevation)" \
        || log_info "  Module pipeline-runner: off"
    log_info "  Elevation posture (server-management; all OFF by default - opt in with --module server-management or --enable-*):"
    if [ "$ENABLE_SERVICE_CONTROL" -eq 1 ]; then
        log_info "    service-control : ON  (sudoers rule installed; NoNewPrivileges=false)"
    else
        log_info "    service-control : off (no sudoers; NoNewPrivileges=true, full sandbox)"
    fi
    [ "$ENABLE_APACHE"  -eq 1 ] && log_info "    apache read ACL : ON" || log_info "    apache read ACL : off"
    if [ "$ENABLE_APACHE_MANAGE" -eq 1 ]; then
        log_info "    apache manage   : ON  (sudoers ${APACHE_MANAGE_SUDOERS_FILE}; argv-exact, noexec; ACL +rw on sites-available)"
    else
        log_info "    apache manage   : off (no Apache write/reload privileges)"
    fi
    if [ "$ENABLE_RKHUNTER_MANAGE" -eq 1 ]; then
        log_info "    rkhunter manage : ON  (sudoers ${RKHUNTER_MANAGE_SUDOERS_FILE}; argv-exact, noexec)"
    else
        log_info "    rkhunter manage : off (cannot run rkhunter scan/update/propupd)"
    fi
    [ "$ENABLE_CERTBOT" -eq 1 ] && log_info "    certbot read ACL: ON" || log_info "    certbot read ACL: off"
    [ "$ENABLE_DOCKER"  -eq 1 ] && log_info "    docker group    : ON" || log_info "    docker group    : off"
    [ "$ENABLE_TEAMSPEAK" -eq 1 ] && log_info "    teamspeak access: ON" || log_info "    teamspeak access: off"
    if [ "$ENABLE_CRON_MANAGE" -eq 1 ]; then
        log_info "    cron manage     : ON  (root helper ${CRON_HELPER_PATH}; sudoers ${CRON_MANAGE_SUDOERS_FILE})"
    else
        log_info "    cron manage     : off (cannot write cron jobs)"
    fi
    if [ "$ENABLE_PORTSENTRY_MANAGE" -eq 1 ]; then
        log_info "    portsentry mgmt : ON  (root helpers ${UNBLOCK_HELPER_PATH} + ${PORTSENTRY_SETUP_HELPER_PATH}; sudoers ${PORTSENTRY_MANAGE_SUDOERS_FILE})"
    else
        log_info "    portsentry mgmt : off (cannot unblock IPs or install/configure portsentry)"
    fi
    if [ "$ENABLE_SERVICE_ENABLE" -eq 1 ]; then
        log_info "    service enable  : ON  (sudoers ${SERVICE_ENABLE_SUDOERS_FILE}; fixed-unit, argv-exact, noexec)"
    else
        log_info "    service enable  : off (config-deploy cannot enable units on boot)"
    fi
    if [ "$ENABLE_PACKAGE_MANAGE" -eq 1 ]; then
        log_info "    package manage  : ON  (sudoers ${PACKAGE_MANAGE_SUDOERS_FILE}; fixed-package, argv-exact)"
    else
        log_info "    package manage  : off (cannot install/uninstall managed packages)"
    fi
    if [ "$ENABLE_PATCH_MANAGE" -eq 1 ]; then
        log_info "    patch manage    : ON  (sudoers ${PATCH_MANAGE_SUDOERS_FILE}; apt-get upgrade, argv-exact)"
    else
        log_info "    patch manage    : off (can see pending updates; cannot apply them)"
    fi
    if [ "$ENABLE_FIREWALL_MANAGE" -eq 1 ]; then
        log_info "    firewall manage : ON  (helper ${FIREWALL_HELPER_PATH}; sudoers ${FIREWALL_MANAGE_SUDOERS_FILE})"
    else
        log_info "    firewall manage : off (cannot view/control ufw rules)"
    fi
    if [ "$ENABLE_MAIL_SETUP" -eq 1 ]; then
        log_info "    mail setup      : ON  (root helper ${MAIL_SETUP_HELPER_PATH}; sudoers ${MAIL_MANAGE_SUDOERS_FILE})"
    else
        log_info "    mail setup      : off (Mail section 'Setup' cannot run)"
    fi
    if [ "$ENABLE_TEAMSPEAK_SETUP" -eq 1 ]; then
        log_info "    teamspeak setup : ON  (root helper ${TEAMSPEAK_SETUP_HELPER_PATH}; sudoers ${TEAMSPEAK_SETUP_SUDOERS_FILE})"
    else
        log_info "    teamspeak setup : off (TeamSpeak 'Install' cannot run)"
    fi
    if [ "$ENABLE_DEPLOYMENT" -eq 1 ]; then
        log_info "    deployment      : ON  (unit template ${DEPLOY_UNIT_TEMPLATE_PATH}; root helper ${DEPLOY_RESTART_HELPER_PATH}; sudoers ${DEPLOY_MANAGE_SUDOERS_FILE})"
    else
        log_info "    deployment      : off (cannot be a cross-agent deploy target)"
    fi

    # Loud, unmissable re-statement of the acl/setfacl degradation: the per-capability lines above
    # print "ON" (the grant is genuinely installed) but without setfacl the agent can't hash the
    # drop-in, so the UI will still report them OFF. Repeat it here at the very end of the run so a
    # `-y` operator scrolling to the success banner sees it.
    if [ "$ACL_GRANT_DEGRADED" -eq 1 ]; then
        log_warn ""
        log_warn "  NOTE: capabilities above marked ON depend on the 'acl' package, which is missing."
        log_warn "        Until you 'apt-get install -y acl' and re-run this installer, the agent"
        log_warn "        cannot self-report them and they stay UNAVAILABLE in the dashboard."
    fi

    # Tip: hint the user they can grant more capabilities later
    if [ "$ENABLE_SERVICE_CONTROL" -eq 0 ] && [ "$ENABLE_APACHE" -eq 0 ] && \
       [ "$ENABLE_APACHE_MANAGE" -eq 0 ] && [ "$ENABLE_RKHUNTER_MANAGE" -eq 0 ] && \
       [ "$ENABLE_CERTBOT" -eq 0 ] && [ "$ENABLE_DOCKER" -eq 0 ] && [ "$ENABLE_TEAMSPEAK" -eq 0 ] && \
       [ "$ENABLE_CRON_MANAGE" -eq 0 ] && [ "$ENABLE_PORTSENTRY_MANAGE" -eq 0 ] && \
       [ "$ENABLE_SERVICE_ENABLE" -eq 0 ] && [ "$ENABLE_PACKAGE_MANAGE" -eq 0 ] && [ "$ENABLE_PATCH_MANAGE" -eq 0 ] && [ "$ENABLE_FIREWALL_MANAGE" -eq 0 ] && \
       [ "$ENABLE_MAIL_SETUP" -eq 0 ] && [ "$ENABLE_TEAMSPEAK_SETUP" -eq 0 ] && [ "$ENABLE_DEPLOYMENT" -eq 0 ]; then
        log_info ""
        log_info "  Tip: re-run with --upgrade --enable-<flag> to grant more capabilities."
        log_info "       Use $0 -h for the full flag list."
    fi
}

write_systemd_unit() {
    log_info "Writing systemd unit: $SYSTEMD_UNIT_PATH"
    EXEC_START="$INSTALL_DIR/Aetheus.Agent.Linux"
    if [ ! -f "$EXEC_START" ]; then
        EXEC_START="/usr/bin/dotnet $INSTALL_DIR/Aetheus.Agent.Linux.dll"
    fi

    # NoNewPrivileges neutralises ALL sudo/setuid escalation - so it can only be true when no sudo
    # grant is in play. service-control is the canonical one; S-FEAT-W8KN's package-manage also runs
    # `sudo -n apt-get`, so it must relax NNP too (otherwise the grant is dead on arrival). When
    # relaxed we document the regression; the rest of the sandbox still applies.
    if [ "$ENABLE_SERVICE_CONTROL" -eq 1 ] || [ "$ENABLE_PACKAGE_MANAGE" -eq 1 ] || [ "$ENABLE_PATCH_MANAGE" -eq 1 ] || [ "$ENABLE_FIREWALL_MANAGE" -eq 1 ] || [ "$ENABLE_MAIL_SETUP" -eq 1 ] || [ "$ENABLE_TEAMSPEAK_SETUP" -eq 1 ] || [ "$ENABLE_DEPLOYMENT" -eq 1 ] || [ "$ENABLE_APACHE_MANAGE" -eq 1 ] || [ "$ENABLE_CERTBOT_MANAGE" -eq 1 ]; then
        NNP_BLOCK="# RELAXED: a sudo grant (--enable-service-control / --enable-package-manage /
# --enable-mail-setup / --enable-teamspeak-setup / --enable-apache-manage / --enable-certbot-manage /
# --module deployment) requires NoNewPrivileges=false (sudo needs setgid(0) for 'systemctl reload
# apache2', the certbot helper, etc.). This is a deliberate trade-off (sudo escalation surface) -
# disable those capabilities to restore NoNewPrivileges=true.
NoNewPrivileges=false"
        # A sudo grant is dead on arrival if the elevated (root) process is left with no
        # capabilities: the setuid sudo binary execs as uid 0 but, with an empty bounding set, can't
        # even setgid(0) ('sudo: unable to change to root gid') let alone let apt/systemctl do their
        # work. So when a grant is in play, leave CapabilityBoundingSet at its default (full set) and
        # don't block setuid execution. The remaining sandbox (syscall filter, kernel protections,
        # and ProtectSystem where it still applies) stays; the argv-exact sudoers allow-list is the
        # real boundary.
        SANDBOX_PRIV_BLOCK="RestrictSUIDSGID=false
# CapabilityBoundingSet intentionally left at the default (full set) so the sudo grant can elevate."
    else
        # Fully sandboxed collector: no sudo grant, so strip every capability and block setuid exec.
        SANDBOX_PRIV_BLOCK="RestrictSUIDSGID=true
# CapabilityBoundingSet emptied - a non-root collector needs no capabilities.
CapabilityBoundingSet="
    fi

    # ProtectSystem=strict mounts /usr, /boot and /etc (and the rest of the FS) read-only for the
    # whole service - including sudo's elevated children. Package installs (apt/dpkg write /usr,
    # /var, /etc), mail provisioning and deploy restarts then fail with 'Read-only file system'
    # (apt exits 100) even though sudo elevated correctly. apache-manage writes vhosts under
    # /etc/apache2, and certbot-manage's root helper writes /etc/apache2 + /etc/letsencrypt (on
    # demand, so a targeted ReadWritePaths would also have to pre-create non-existent dirs or the
    # unit refuses to start). Those grants therefore need the read-only system mount relaxed.
    # service-control only drives systemd over dbus (no FS writes), so it keeps ProtectSystem=strict.
    if [ "$ENABLE_PACKAGE_MANAGE" -eq 1 ] || [ "$ENABLE_PATCH_MANAGE" -eq 1 ] || [ "$ENABLE_FIREWALL_MANAGE" -eq 1 ] || [ "$ENABLE_MAIL_SETUP" -eq 1 ] || [ "$ENABLE_DEPLOYMENT" -eq 1 ] \
       || [ "$ENABLE_APACHE_MANAGE" -eq 1 ] || [ "$ENABLE_CERTBOT_MANAGE" -eq 1 ]; then
        PROTECT_SYSTEM_BLOCK="# RELAXED: package/mail/deploy/apache-manage/certbot-manage grants write system paths, so the read-only system mount is off.
ProtectSystem=false"
    else
        PROTECT_SYSTEM_BLOCK="ProtectSystem=strict"
    fi

    # A deployment target needs an ACL mask with execute on the private state directory so the
    # named aetheus-deploy ACL can traverse to $DEPLOY_BASE_DIR. Without deployment, keep 0700.
    STATE_DIRECTORY_MODE="0700"
    if [ "$ENABLE_DEPLOYMENT" -eq 1 ]; then
        STATE_DIRECTORY_MODE="0710"
    fi

    cat > "$SYSTEMD_UNIT_PATH" <<EOF
[Unit]
Description=Aetheus Infrastructure Agent
After=network-online.target
Wants=network-online.target

[Service]
# Type=exec - the unit is "active" as soon as the binary execs. We previously
# used Type=notify (with .NET's AddSystemd sending READY=1), but on some hosts
# the sd_notify signal does not reach systemd (env propagation / wrapped
# ExecStart / NotifyAccess quirks), so systemd would kill the agent at
# TimeoutStartSec ON EVERY START - the process worked, sent its first heartbeat,
# then got terminated mid-work in a constant restart loop. Type=exec sidesteps
# that entirely; the agent's actual readiness is then verified out-of-band by
# the install script's HTTP health check. AddSystemd() stays in Program.cs for
# clean SIGTERM handling.
Type=exec
User=$AGENT_USER
Group=$AGENT_GROUP
WorkingDirectory=$INSTALL_DIR
# StateDirectory: systemd creates/owns /var/lib/aetheus-agent (= \$WORK_DIR) for the service user
# on every start, and auto-adds it to ReadWritePaths under ProtectSystem=strict. Deployment targets
# use mode 0710 solely as the ACL mask for the named aetheus-deploy traverse-only entry; other hosts
# remain 0700. This
# is the canonical home for the agent's credential-encryption key material
# (/var/lib/aetheus-agent/keys, hardened 0700). LinuxCredentialProtector still migrates any
# legacy keys from the old install-dir location on first run, so existing enrollments survive.
StateDirectory=aetheus-agent
StateDirectoryMode=$STATE_DIRECTORY_MODE
ExecStart=$EXEC_START
Restart=always
RestartSec=10
TimeoutStartSec=120
TimeoutStopSec=30
# KillMode=mixed: sends SIGTERM to the main process only; child processes
# (e.g. the self-update script) survive the service stop and finish their work.
# Default 'control-group' would kill ALL processes in the cgroup, breaking updates.
KillMode=mixed
Environment=DOTNET_ENVIRONMENT=Production
# Explicit PATH so pipeline steps and the Docker capability probe find tools regardless of how
# they were installed. /snap/bin is included because snap-packaged Docker lives there and the
# systemd default PATH omits it (a snap Docker would otherwise be invisible to the agent).
Environment=PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/snap/bin

# --- Security hardening ---
$NNP_BLOCK
$PROTECT_SYSTEM_BLOCK
ProtectHome=true
ReadWritePaths=$WORK_DIR $INSTALL_DIR
PrivateTmp=true
ProtectControlGroups=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectKernelLogs=true
ProtectClock=true
ProtectHostname=true
ProtectProc=invisible
RestrictNamespaces=true
RestrictRealtime=true
$SANDBOX_PRIV_BLOCK
LockPersonality=true
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6
SystemCallFilter=@system-service
SystemCallErrorNumber=EPERM
# NOTE: MemoryDenyWriteExecute is intentionally NOT set - the .NET JIT needs
# W^X-violating mappings and it would crash the runtime.
# Phase 3 hardening: cgroup-level backstops covering the agent AND every pipeline step it spawns.
# TasksMax caps total processes/threads (a fork bomb in a step can't exhaust the host's PID space);
# 4096 is far above any real build's needs. MemoryMax is left commented because build memory varies
# wildly and a low cap would OOM-kill legitimate compiles - uncomment and tune per runner if desired.
TasksMax=4096
#MemoryMax=8G

[Install]
WantedBy=multi-user.target
EOF
}

# Faithful "can the agent itself reach the backend?" probe - runs the agent
# binary in --probe-backend mode (one-shot HTTP GET /health/live using the
# same .NET HTTP stack the running agent will use) so TLS/DNS/proxy issues
# invisible to curl surface NOW instead of inside a systemd start timeout.
# Args: $1 = server URL (logged for context only - the probe reads appsettings).
# Returns 0 on reachable, 1 otherwise. Hard-fails by default (the caller may
# choose to demote to a warning via PROBE_BACKEND_SOFT=1).
run_backend_probe() {
    _pb_url="${1:-}"
    _pb_dll="$INSTALL_DIR/Aetheus.Agent.Linux.dll"
    _pb_bin="$INSTALL_DIR/Aetheus.Agent.Linux"

    log_info "Probing backend with the agent runtime (TLS/DNS faithfulness check)..."

    # Pick the deployment shape: single-file binary if present, else dotnet + dll.
    if [ -f "$_pb_bin" ]; then
        _pb_cmd="$_pb_bin --probe-backend"
    elif [ -f "$_pb_dll" ] && command -v dotnet >/dev/null 2>&1; then
        _pb_cmd="dotnet $_pb_dll --probe-backend"
    else
        log_warn "  No agent binary found at $INSTALL_DIR - skipping runtime probe."
        return 0
    fi

    # Run as the agent user with the install dir as CWD so the probe loads the
    # same appsettings.json the running service will read.
    if id "$AGENT_USER" >/dev/null 2>&1; then
        _pb_output="$(cd "$INSTALL_DIR" && runuser -u "$AGENT_USER" -- sh -c "$_pb_cmd" 2>&1)" || _pb_rc=$?
    else
        _pb_output="$(cd "$INSTALL_DIR" && sh -c "$_pb_cmd" 2>&1)" || _pb_rc=$?
    fi
    _pb_rc="${_pb_rc:-0}"

    # Print the probe output verbatim - it already says what happened.
    [ -n "$_pb_output" ] && printf '  %s\n' "$_pb_output"

    if [ "$_pb_rc" -eq 0 ]; then
        return 0
    fi

    # Distinguish a missing .NET runtime from an actual network failure. The .NET
    # apphost returns exit 150 ("framework not found") and prints "No frameworks
    # were found" / "must install or update .NET" - that is NOT a connectivity
    # problem, so reporting it as one (and pointing at TLS/DNS/proxy) sends the
    # operator down the wrong path. This agent build is framework-dependent; the
    # box needs the .NET runtime, or a self-contained agent build.
    if [ "$_pb_rc" -eq 150 ] || printf '%s' "$_pb_output" | grep -qE 'No frameworks were found|must install or update \.NET'; then
        log_error ".NET runtime missing - the agent could not start (exit $_pb_rc)."
        log_error "  This build is framework-dependent and the .NET 10 runtime is not installed here."
        log_error "  This is NOT a backend connectivity problem (the URL above is irrelevant for now)."
        log_error "  Fix: install the .NET 10 runtime, then re-run this installer:"
        log_error "    curl -fsSL ${DOTNET_INSTALLER_URL} -o /tmp/dotnet-install.sh \\"
        log_error "      && echo '${DOTNET_INSTALLER_SHA256}  /tmp/dotnet-install.sh' | sha256sum -c - \\"
        log_error "      && sudo bash /tmp/dotnet-install.sh --version ${DOTNET_RUNTIME_VERSION} --runtime dotnet --install-dir /usr/share/dotnet \\"
        log_error "      && sudo ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet"
        log_error "  Or use a self-contained agent build (no runtime needed on the target)."
        return 1
    fi

    log_error "Agent runtime cannot reach the backend (exit $_pb_rc)."
    if [ -n "$_pb_url" ]; then
        log_error "  Configured ServerUrl: $_pb_url"
    fi
    log_error "  This is the failure that would have eaten the systemd start timeout."
    log_error "  Common causes: TLS cert chain missing for .NET runtime, HTTPS proxy required,"
    log_error "  IPv6-only DNS that the .NET resolver picks differently from curl."
    return 1
}

# Print the most useful diagnostics when something goes wrong.
diagnose_failure() {
    _df_url="${1:-}"
    printf "\n"
    log_info "=== Diagnostics ==="
    log_info "Last 40 log lines:"
    journalctl -u "$SERVICE_NAME" -n 40 --no-pager 2>/dev/null || true
    printf "\n"
    log_info "Service status:"
    systemctl status "$SERVICE_NAME" --no-pager -l 2>/dev/null || true
    printf "\n"
    log_info "Next steps:"
    if [ -n "$_df_url" ]; then
        log_info "  - Verify API reachable: curl -s -o /dev/null -w '%{http_code}\\n' ${_df_url}/health/live"
    fi
    log_info "  - Tail logs: journalctl -u $SERVICE_NAME -f"
    log_info "  - If the registration token is stale or already consumed, generate a"
    log_info "    fresh one in the web UI and re-run: sudo sh $0"
    log_info "  - Config: $INSTALL_DIR/appsettings.json"
}

# Wait for service to become active, then scan journal for enrollment outcome.
# Args: $1 = server URL (for diagnostic hint), $2 = since timestamp (ISO, optional).
# Returns 0 on healthy, 1 otherwise.
run_health_check() {
    _hc_url="${1:-}"
    _hc_since="${2:-}"
    printf "\n"
    log_info "Running health check..."

    # Phase 1 - wait for "active" state. Type=notify means the host signals ready
    # as soon as ExecuteAsync starts, typically within 2-5s. Give it up to 20s.
    _hc_waited=0
    _hc_max=20
    while [ "$_hc_waited" -lt "$_hc_max" ]; do
        if systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
            break
        fi
        sleep 1
        _hc_waited=$((_hc_waited + 1))
    done

    if ! systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
        log_error "Service did not reach 'active' within ${_hc_max}s."
        diagnose_failure "$_hc_url"
        return 1
    fi
    log_info "  Service active after ${_hc_waited}s."

    # Phase 2 - wait for the enrollment handshake. AgentStartupService logs
    # "Enrolled successfully" or "Agent is enrolled and ready" within ~5s on
    # success. On failure it retries with backoff, so we don't need to wait the
    # full 10-retry cycle to detect the first error.
    log_info "  Waiting 8s for enrollment handshake..."
    sleep 20

    if [ -n "$_hc_since" ]; then
        _hc_journal="$(journalctl -u "$SERVICE_NAME" --since "$_hc_since" --no-pager 2>/dev/null || echo '')"
    else
        _hc_journal="$(journalctl -u "$SERVICE_NAME" -n 200 --no-pager 2>/dev/null || echo '')"
    fi

    # Negative signals (most recent match wins).
    _hc_err="$(printf '%s\n' "$_hc_journal" \
        | grep -E 'Enrollment failed|Enrollment HTTP call.*failed|No registration token configured|credentials is missing or unreadable|Enrollment failed after [0-9]+ attempts' \
        | tail -1 || true)"

    if [ -n "$_hc_err" ]; then
        log_error "Enrollment problem detected in logs:"
        log_error "  $_hc_err"
        diagnose_failure "$_hc_url"
        return 1
    fi

    # Positive signals.
    if printf '%s\n' "$_hc_journal" | grep -qE 'Enrolled successfully|Agent already enrolled|Agent is enrolled and ready'; then
        _hc_server_id="$(printf '%s\n' "$_hc_journal" \
            | grep -oE 'Server ID [a-zA-Z0-9-]+' | tail -1 || true)"
        if [ -n "$_hc_server_id" ]; then
            log_info "  Enrollment OK (${_hc_server_id})."
        else
            log_info "  Enrollment OK."
        fi
        if ! systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
            log_warn "  ...but service is no longer active. Investigating."
            diagnose_failure "$_hc_url"
            return 1
        fi
        return 0
    fi

    # No clear signal either way - the first attempt may still be in flight.
    log_error "  No successful enrollment outcome was observed; installation fails closed."
    diagnose_failure "$_hc_url"
    return 1
}

# =============================================================================
# Mode: PURGE
# =============================================================================
if [ "$MODE" = "purge" ]; then
    log_warn "PURGE mode: this will remove the service, $INSTALL_DIR, $WORK_DIR, and the $AGENT_USER user."
    if [ "$ASSUME_YES" -ne 1 ]; then
        printf "Type 'PURGE' to confirm: "
        read _confirm
        if [ "$_confirm" != "PURGE" ]; then
            log_info "Aborted."
            exit 0
        fi
    fi

    log_info "Stopping and disabling service..."
    stop_and_disable_service
    remove_systemd_unit

    if [ -f "$SUDOERS_FILE" ]; then
        log_info "Removing sudoers file..."
        rm -f "$SUDOERS_FILE"
    fi
    if [ -f "$APACHE_MANAGE_SUDOERS_FILE" ]; then
        log_info "Removing Apache-manage sudoers file..."
        rm -f "$APACHE_MANAGE_SUDOERS_FILE"
    fi
    if [ -f "$RKHUNTER_MANAGE_SUDOERS_FILE" ]; then
        log_info "Removing RKHunter-manage sudoers file..."
        rm -f "$RKHUNTER_MANAGE_SUDOERS_FILE"
    fi
    if [ -f "$CRON_MANAGE_SUDOERS_FILE" ] || [ -f "$PORTSENTRY_MANAGE_SUDOERS_FILE" ] || \
       [ -f "$SERVICE_ENABLE_SUDOERS_FILE" ] || [ -f "$PACKAGE_MANAGE_SUDOERS_FILE" ] || \
       [ -f "$PATCH_MANAGE_SUDOERS_FILE" ] || [ -f "$FIREWALL_MANAGE_SUDOERS_FILE" ] || \
       [ -f "$MAIL_MANAGE_SUDOERS_FILE" ] || [ -f "$DEPLOY_MANAGE_SUDOERS_FILE" ] || \
       [ -f "$CERTBOT_MANAGE_SUDOERS_FILE" ] || [ -f "$TEAMSPEAK_SETUP_SUDOERS_FILE" ] || \
       [ -d "$AETHEUS_HELPER_DIR" ]; then
        log_info "Removing privileged-capability sudoers files + helpers..."
        rm -f "$CRON_MANAGE_SUDOERS_FILE" "$PORTSENTRY_MANAGE_SUDOERS_FILE" "$SERVICE_ENABLE_SUDOERS_FILE"
        rm -f "$PACKAGE_MANAGE_SUDOERS_FILE" "$PATCH_MANAGE_SUDOERS_FILE" "$FIREWALL_MANAGE_SUDOERS_FILE" "$MAIL_MANAGE_SUDOERS_FILE" "$DEPLOY_MANAGE_SUDOERS_FILE"
        rm -f "$CERTBOT_MANAGE_SUDOERS_FILE" "$TEAMSPEAK_SETUP_SUDOERS_FILE"
        rm -f "$CRON_HELPER_PATH" "$UNBLOCK_HELPER_PATH" "$PORTSENTRY_SETUP_HELPER_PATH" "$MAIL_SETUP_HELPER_PATH" "$MAIL_MANAGE_HELPER_PATH" "$DEPLOY_RESTART_HELPER_PATH" "$FIREWALL_HELPER_PATH"
        rm -f "$CERTBOT_ISSUE_HELPER_PATH" "$CERTBOT_MANAGE_HELPER_PATH" "$TEAMSPEAK_SETUP_HELPER_PATH"
        rmdir "$AETHEUS_HELPER_DIR" 2>/dev/null || true
    fi
    # Deployment unit template lives under /etc/systemd/system (not $AETHEUS_HELPER_DIR); remove it too.
    # Deployed app instances under $WORK_DIR/deploy go away with the $WORK_DIR rm -rf below.
    if [ -f "$DEPLOY_UNIT_TEMPLATE_PATH" ]; then
        log_info "Removing deploy unit template..."
        rm -f "$DEPLOY_UNIT_TEMPLATE_PATH"
        systemctl daemon-reload 2>/dev/null || true
    fi

    # cd out of INSTALL_DIR in case the script is running from inside it;
    # rm -rf is fine for open file descriptors but some shells get upset
    # if their CWD disappears.
    cd /

    if [ -d "$INSTALL_DIR" ]; then
        log_info "Removing $INSTALL_DIR..."
        rm -rf "$INSTALL_DIR"
    fi

    if [ -d "$WORK_DIR" ]; then
        log_info "Removing $WORK_DIR..."
        rm -rf "$WORK_DIR"
    fi

    if id "$AGENT_USER" >/dev/null 2>&1; then
        gpasswd -d "$AGENT_USER" docker >/dev/null 2>&1 || true
        gpasswd -d "$AGENT_USER" teamspeak >/dev/null 2>&1 || true
        log_info "Deleting user $AGENT_USER..."
        userdel "$AGENT_USER" 2>/dev/null || log_warn "userdel reported issues (non-fatal)"
        # Group is auto-removed by userdel on distros using USERGROUPS_ENAB yes;
        # clean up explicitly just in case.
        if getent group "$AGENT_GROUP" >/dev/null 2>&1; then
            groupdel "$AGENT_GROUP" 2>/dev/null || true
        fi
    fi

    log_info ""
    log_info "Purge complete. The agent is fully removed from this system."
    exit 0
fi

# =============================================================================
# Mode: UPGRADE
# =============================================================================
if [ "$MODE" = "upgrade" ]; then
    log_info "UPGRADE mode: replacing binaries + service, preserving config and data."

    # Preconditions: existing install must be present.
    if [ ! -f "$INSTALL_DIR/appsettings.json" ]; then
        log_error "No existing config at $INSTALL_DIR/appsettings.json."
        log_error "Upgrade requires a prior installation. Use the default mode to install fresh."
        exit 1
    fi
    if ! systemctl list-unit-files 2>/dev/null | grep -q "^${SERVICE_NAME}\.service"; then
        log_error "Service ${SERVICE_NAME} not installed. Use the default mode to install fresh."
        exit 1
    fi

    SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

    # Refuse to run from INSIDE $INSTALL_DIR. If the user extracted the new
    # tarball there, the shipped default appsettings.json has already clobbered
    # the configured one - we can't recover it from this script.
    if [ "$SCRIPT_DIR" = "$INSTALL_DIR" ]; then
        log_error "Upgrade must be run from outside $INSTALL_DIR."
        log_error "Extract the new tarball to a temp dir first, then re-run from there:"
        log_error "  mkdir -p /tmp/aetheus-agent-upgrade && cd /tmp/aetheus-agent-upgrade"
        log_error "  tar -xzf /path/to/aetheus-agent-linux-x64-v*.tar.gz"
        log_error "  sudo sh ./install-agent-linux.sh --upgrade"
        exit 1
    fi

    if [ ! -f "$SCRIPT_DIR/Aetheus.Agent.Linux.dll" ] && [ ! -f "$SCRIPT_DIR/Aetheus.Agent.Linux" ]; then
        log_error "New agent binaries not found in $SCRIPT_DIR."
        exit 1
    fi

    # Capture the server URL from the existing config for the health-check hint.
    EXISTING_URL="$(grep -oE '"ServerUrl"[[:space:]]*:[[:space:]]*"[^"]+"' "$INSTALL_DIR/appsettings.json" \
        | sed -E 's/.*"ServerUrl"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/' | head -1 || true)"

    log_info "Stopping service before swapping binaries..."
    stop_and_disable_service

    # Copy everything from the new tree EXCEPT appsettings.json (preserve config).
    # Using find ... -exec cp -r is portable (no rsync dependency).
    log_info "Copying new binaries to $INSTALL_DIR (preserving appsettings.json)..."
    find "$SCRIPT_DIR" -maxdepth 1 -mindepth 1 \
        ! -name 'appsettings.json' \
        -exec cp -r {} "$INSTALL_DIR/" \;

    # Re-chown in case new files came in as root.
    if id "$AGENT_USER" >/dev/null 2>&1; then
        chown -R "$AGENT_USER:$AGENT_GROUP" "$INSTALL_DIR"
        chown "$AGENT_USER:$AGENT_GROUP" "$INSTALL_DIR/appsettings.json"
        chmod 600 "$INSTALL_DIR/appsettings.json"
    else
        log_warn "User $AGENT_USER missing - upgrade continued but service will fail to start. Run default install mode."
    fi

    # Self-contained apphost may have lost its +x bit if the tarball was packed on
    # Windows - restore it so the swapped-in binary stays runnable. No-op otherwise.
    if [ -f "$INSTALL_DIR/Aetheus.Agent.Linux" ]; then
        chmod +x "$INSTALL_DIR/Aetheus.Agent.Linux"
    fi

    # Preserve the operator's existing elevation posture across the upgrade
    # (never silently widen or drop it); explicit --enable-* flags override.
    derive_existing_posture
    ensure_optional_packages
    ensure_icu || true
    apply_sudoers
    write_systemd_unit
    if id "$AGENT_USER" >/dev/null 2>&1; then
        apply_read_acls
        ensure_docker_group
        ensure_teamspeak_access
    fi

    # Pre-start runtime probe: an upgraded binary can ship with a newer .NET
    # runtime that handles TLS differently, so re-verify the agent itself can
    # reach the backend BEFORE we ask systemd to start it.
    if ! run_backend_probe "$EXISTING_URL"; then
        exit 1
    fi

    write_installed_at_marker

    _service_start_ts="$(date -u +"%Y-%m-%d %H:%M:%S UTC")"
    log_info "Reloading systemd and starting service..."
    systemctl daemon-reload
    systemctl enable "$SERVICE_NAME"
    systemctl start "$SERVICE_NAME"

    if ! run_health_check "$EXISTING_URL" "$_service_start_ts"; then
        exit 1
    fi

    log_info ""
    log_info "Upgrade complete."
    log_info "  Config preserved: $INSTALL_DIR/appsettings.json"
    log_info "  Data preserved:   $WORK_DIR"
    log_info "  Logs:             journalctl -u $SERVICE_NAME -f"
    print_posture_summary
    exit 0
fi

# =============================================================================
# Mode: INSTALL (default)
# =============================================================================

# --- Interactive prompts ---
if [ -z "$SERVER_URL" ]; then
    if [ -n "$URL_FROM_MARKER" ] && [ "$URL_FROM_MARKER" != "https://aetheus.example.com" ]; then
        SERVER_URL="$URL_FROM_MARKER"
        log_info "Using server URL from marker: $SERVER_URL"
    else
        printf "Aetheus server URL [%s]: " "$DEFAULT_SERVER_URL"
        read SERVER_URL
        if [ -z "$SERVER_URL" ]; then
            SERVER_URL="$DEFAULT_SERVER_URL"
        fi
    fi
fi

if [ -z "$REG_TOKEN" ]; then
    printf "Registration token (input hidden): "
    stty -echo 2>/dev/null || true
    read REG_TOKEN
    stty echo 2>/dev/null || true
    printf "\n"
fi

if [ -z "$REG_TOKEN" ]; then
    log_error "Registration token cannot be empty. Generate one in the Aetheus web UI."
    exit 1
fi

TOKEN_LEN=${#REG_TOKEN}
if [ "$TOKEN_LEN" -gt 10 ]; then
    TOKEN_FIRST="$(printf '%s' "$REG_TOKEN" | cut -c1-5)"
    TOKEN_LAST="$(printf '%s' "$REG_TOKEN" | cut -c$((TOKEN_LEN - 4))-)"
    printf "  Token received: %s...%s (%d chars)\n" "$TOKEN_FIRST" "$TOKEN_LAST" "$TOKEN_LEN"
else
    printf "  Token received: %d chars\n" "$TOKEN_LEN"
fi

# --- Validate that ServerUrl points to the backend API, not the frontend ---
# The backend exposes GET /health/live (anonymous liveness probe); the frontend
# (Blazor WASM) does not. This catches the classic mistake of using
# https://aetheus.example.com (frontend) instead of
# https://aetheus-api.example.com (backend API).
log_info "Validating server URL reachability: ${SERVER_URL}/health/live"
if command -v curl >/dev/null 2>&1; then
    # Honour --allow-insecure-certs here too: a self-signed backend (dev / localhost)
    # would otherwise make this pre-flight curl fail TLS verification (code 000) even
    # though the agent itself is configured to bypass it.
    CURL_K=""
    [ "$ALLOW_INSECURE_CERTS" -eq 1 ] && CURL_K="-k"
    HEALTH_HTTP_CODE="$(curl -s $CURL_K -o /dev/null -w '%{http_code}' --max-time 10 "${SERVER_URL}/health/live" || echo "000")"
    if [ "$HEALTH_HTTP_CODE" != "200" ]; then
        log_error "Server URL '${SERVER_URL}' did not respond 200 on /health/live (got ${HEALTH_HTTP_CODE})."
        log_error "Make sure you pointed at the backend API, not the frontend."
        log_error "Example: https://aetheus-api.example.com (API) - NOT https://aetheus.example.com (front)."
        exit 1
    fi
    log_info "Server URL is reachable (HTTP 200 on /health/live)."
else
    log_warn "curl not available - skipping server URL validation."
fi

if [ -z "$AGENT_NAME" ]; then
    AGENT_NAME="$(hostname)"
    log_info "Using hostname as agent name: $AGENT_NAME (changeable from the web UI)"
fi

# --- Resolve script location ---
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ ! -f "$SCRIPT_DIR/Aetheus.Agent.Linux.dll" ] && [ ! -f "$SCRIPT_DIR/Aetheus.Agent.Linux" ]; then
    log_error "Agent binaries not found in $SCRIPT_DIR. Extract the agent archive first."
    exit 1
fi

# --- Clean any previous installation ---
# Stop + remove service, sudoers, stale config, and wipe work dir so the reinstall
# starts from a known state. We never rm -rf "$INSTALL_DIR" because this script
# typically lives inside it - removing it would kill the running shell.
log_info "Cleaning previous installation (if any)..."
stop_and_disable_service
remove_systemd_unit

if [ -f "$SUDOERS_FILE" ]; then
    log_info "  Removing old sudoers rules..."
    rm -f "$SUDOERS_FILE"
fi
if [ -f "$APACHE_MANAGE_SUDOERS_FILE" ]; then
    log_info "  Removing old Apache-manage sudoers rules..."
    rm -f "$APACHE_MANAGE_SUDOERS_FILE"
fi
if [ -f "$RKHUNTER_MANAGE_SUDOERS_FILE" ]; then
    log_info "  Removing old RKHunter-manage sudoers rules..."
    rm -f "$RKHUNTER_MANAGE_SUDOERS_FILE"
fi

if [ -f "$INSTALL_DIR/appsettings.json" ]; then
    log_info "  Removing old appsettings.json (stale token/enrollment)..."
    rm -f "$INSTALL_DIR/appsettings.json"
fi

# Purge work dir contents: SQLite offline queue, cached logs, enrollment state.
# Keep the directory itself so chown below stays idempotent.
if [ -d "$WORK_DIR" ] && [ -n "$(ls -A "$WORK_DIR" 2>/dev/null)" ]; then
    log_info "  Purging work dir ($WORK_DIR)..."
    rm -rf "${WORK_DIR:?}"/* "${WORK_DIR:?}"/.[!.]* 2>/dev/null || true
fi

# --- Move files to INSTALL_DIR if needed ---
if [ "$SCRIPT_DIR" != "$INSTALL_DIR" ]; then
    log_info "Copying agent files to $INSTALL_DIR..."
    mkdir -p "$INSTALL_DIR"
    cp -r "$SCRIPT_DIR/"* "$INSTALL_DIR/"
fi

# --- Create system user ---
log_info "Creating system user: $AGENT_USER"
if id "$AGENT_USER" >/dev/null 2>&1; then
    # Re-install is normal - keep the existing system user instead of yelling.
    log_info "User $AGENT_USER already exists - reusing"
else
    useradd -r -s /bin/false -d "$WORK_DIR" "$AGENT_USER"
fi

# --- Add agent user to docker group (required for container/image collection) ---
ensure_docker_group

# --- Provision TeamSpeak access (opt-in: --enable-teamspeak / --module server-management) ---
ensure_teamspeak_access

# --- Create directories ---
log_info "Creating directories..."
mkdir -p "$WORK_DIR"
chown "$AGENT_USER:$AGENT_GROUP" "$WORK_DIR"
chown -R "$AGENT_USER:$AGENT_GROUP" "$INSTALL_DIR"

# Self-contained builds ship a native apphost (Aetheus.Agent.Linux) that the
# systemd ExecStart and the pre-start probe run directly. A tarball packed on
# Windows (.NET TarFile) loses the Unix +x bit, so restore it here. No-op for
# framework-dependent builds (the apphost file is absent there).
if [ -f "$INSTALL_DIR/Aetheus.Agent.Linux" ]; then
    chmod +x "$INSTALL_DIR/Aetheus.Agent.Linux"
fi

# --- Write configuration ---
# JSON-escape operator-supplied values so a " or \ in the URL/token/name cannot
# break out of its string and inject config structure.
log_info "Writing configuration to $INSTALL_DIR/appsettings.json..."

# Auto-enable insecure certs for localhost (self-signed dev cert) - parity with
# the Windows installer. Explicit opt-in elsewhere via --allow-insecure-certs.
case "$SERVER_URL" in
    *://localhost*|*://127.0.0.1*|*://\[::1\]*) ALLOW_INSECURE_CERTS=1 ;;
esac
ALLOW_INSECURE_JSON="false"
if [ "$ALLOW_INSECURE_CERTS" -eq 1 ]; then
    ALLOW_INSECURE_JSON="true"
    log_warn "AllowInsecureCerts enabled (DEV mode): TLS validation bypassed for backend API and pipeline git clones"
fi

SERVER_URL_J="$(json_escape "$SERVER_URL")"
REG_TOKEN_J="$(json_escape "$REG_TOKEN")"
AGENT_NAME_J="$(json_escape "$AGENT_NAME")"
WORK_DIR_J="$(json_escape "$WORK_DIR")"
DOCKER_STORAGE_DEPLOYMENT_ONLY=false
if [ "$MODULE_DEPLOYMENT" -eq 1 ]; then
    DOCKER_STORAGE_DEPLOYMENT_ONLY=true
fi
cat > "$INSTALL_DIR/appsettings.json" <<EOF
{
  "Aetheus": {
    "ServerUrl": "$SERVER_URL_J",
    "RegistrationToken": "$REG_TOKEN_J",
    "Name": "$AGENT_NAME_J",
    "PollingIntervalSeconds": 10,
    "HeartbeatIntervalSeconds": 30,
    "MaxConcurrentTasks": 2,
    "WorkDirectory": "$WORK_DIR_J",
    "LogRetentionDays": 30,
    "AllowInsecureCerts": $ALLOW_INSECURE_JSON,
    "DockerStorageMaintenance": {
      "PolicyVersion": 2,
      "Enabled": true,
      "DryRun": false,
      "DeploymentOnly": $DOCKER_STORAGE_DEPLOYMENT_ONLY,
      "AllowBuildsOnDeploymentTarget": false,
      "MaintenanceIntervalMinutes": 60,
      "MaxCacheAgeHours": 168,
      "PressureCacheAgeHours": 24,
      "ReservedSpaceGiB": 20,
      "MaxCacheGiB": 80,
      "MinFreeSpaceGiB": 20,
      "PressureUsedPercent": 80,
      "NuGetCacheRetentionDays": 30
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
EOF
chown "$AGENT_USER:$AGENT_GROUP" "$INSTALL_DIR/appsettings.json"
chmod 600 "$INSTALL_DIR/appsettings.json"

ensure_optional_packages
ensure_icu || true
apply_sudoers
write_systemd_unit
apply_read_acls

# --- Faithful pre-start probe ---
# Fails fast (well before the 120s systemd TimeoutStartSec) if the .NET HTTP
# stack can't reach the backend even though curl could - typically a TLS cert
# chain or proxy issue that only the runtime sees.
if ! run_backend_probe "$SERVER_URL"; then
    exit 1
fi

write_installed_at_marker

# --- Enable and start ---
_service_start_ts="$(date -u +"%Y-%m-%d %H:%M:%S UTC")"
log_info "Enabling and starting service..."
systemctl daemon-reload
systemctl enable "$SERVICE_NAME"
systemctl start "$SERVICE_NAME"

# --- Health check (only inspect logs since this start) ---
if ! run_health_check "$SERVER_URL" "$_service_start_ts"; then
    exit 1
fi

log_info ""
log_info "Aetheus Agent is running!"
log_info ""
log_info "  Server URL: $SERVER_URL"
log_info "  Agent name: $AGENT_NAME"
log_info ""
log_info "  Status:  systemctl status $SERVICE_NAME"
log_info "  Logs:    journalctl -u $SERVICE_NAME -f"
log_info "  Config:  $INSTALL_DIR/appsettings.json"
log_info "  Install: $INSTALL_DIR"
log_info "  Data:    $WORK_DIR"
print_posture_summary
