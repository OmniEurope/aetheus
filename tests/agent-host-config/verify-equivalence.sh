#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# R-249 - byte-for-byte proof that the host configuration templates under deploy/agent-host-config
# render exactly what the heredocs of the former deploy/scripts/install-agent-linux.sh wrote.
#
# For each of the 42 files the reference installer wrote with `cat > ... <<`:
#   - the reference heredoc is evaluated by this shell with a given set of variable values (the
#     three ACME helpers also get the reference's own sed of __AETHEUS_ACME_WEBROOT__);
#   - the template is rendered by render_host_config, extracted from the current installer, with the
#     same values;
#   - both outputs are compared with cmp.
# A file a root-owned helper writes at run time (nested heredoc) is compared with the verbatim body
# the reference helper carried. Three value sets are used: the installer defaults (sandboxed agent),
# a relaxed agent with a custom ACME web root, and hostile values (&, \, |, $, backquote, quotes,
# %s, a newline and a literal #{AGENT_USER}#) that must be inserted literally.
# One deliberate difference, decided by the user: the sandboxed agent unit gets back the
# NoNewPrivileges=true block commit df6895eb9 dropped (the reference wrote an empty line there). The
# equivalence runs use the restored value on both sides; a dedicated check proves that block is the
# only difference from the raw reference.
# Then negative checks: the renderer must refuse, leaving the destination untouched, on every
# broken input. Finally structure checks: manifest = files on disk = 42, and no heredoc left in the
# installer.
#
# Usage: tests/agent-host-config/verify-equivalence.sh [reference-installer]
# Default reference: deploy/scripts/install-agent-linux.sh at commit 557ce0f15, the last version
# that still carried the heredocs. Run it with the shell the installer runs with (dash on Debian and
# Ubuntu). Exit code 0 only when every check passed.
set -eu

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
installer="$repo/deploy/scripts/install-agent-linux.sh"
HOST_CONFIG_DIR="$repo/deploy/agent-host-config"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [ "$#" -ge 1 ]; then
    reference="$1"
else
    reference="$work/reference-install-agent-linux.sh"
    git -C "$repo" show 557ce0f1524ff529ed071851cd1a768bf3a32301:deploy/scripts/install-agent-linux.sh > "$reference"
fi

failures=0
pass() { printf 'PASS %s\n' "$1"; }
fail() { printf 'FAIL %s\n' "$1"; failures=$((failures + 1)); }

log_error() { printf '[ERROR] %s\n' "$1" >&2; }
# The production renderer itself, extracted the same way verify-agent-posture-idempotence.sh
# extracts derive_existing_posture, so this proof cannot drift from the installer.
sed -n '/^render_host_config() {$/,/^}$/p' "$installer" > "$work/render.sh"
grep -q '^render_host_config() {$' "$work/render.sh" || { echo "render_host_config not found in $installer" >&2; exit 1; }
# shellcheck disable=SC1091
. "$work/render.sh"

# start|end|template|kind (reference line numbers; q = quoted heredoc, u = unquoted heredoc,
# n = heredoc nested in a helper, written by that helper at run time) - 42 rows.
MAP='568|690|agent-update/agent-posture-upgrade|q
695|708|agent-update/aetheus-agent-upgrade.service|u
711|721|agent-update/aetheus-agent-upgrade.path|u
1105|1120|agent/sudoers.d/aetheus-agent|u
1140|1147|apache/aetheus-apache-configtest|q
1155|1168|apache/aetheus-apache-reload|q
1172|1202|apache/sudoers.d/aetheus-apache|u
1257|1375|certbot/aetheus-certbot-issue|q
1314|1319|certbot/aetheus-acme-webroot.conf|n
1353|1365|certbot/local-ssl-site.conf|n
1389|1471|certbot/aetheus-certbot-manage|q
1479|1490|certbot/sudoers.d/aetheus-certbot|u
1523|1528|certbot/renewal-hooks-deploy/aetheus-apache|q
1532|1541|certbot/aetheus-certbot-renewal-check.service|u
1542|1553|certbot/aetheus-certbot-renewal-check.timer|u
1760|1804|cron/cron-apply|q
1808|1817|cron/sudoers.d/aetheus-cron|u
1853|1910|deploy/aetheus-app@.service|u
1913|1932|deploy/deploy-restart|q
1936|1945|deploy/sudoers.d/aetheus-deploy|u
1999|2025|portsentry/unblock-ip|q
2033|2071|portsentry/portsentry-setup|q
2075|2085|portsentry/sudoers.d/aetheus-portsentry|u
2100|2120|service-enable/sudoers.d/aetheus-service-enable|u
2140|2177|package/sudoers.d/aetheus-package|u
2188|2202|patch/sudoers.d/aetheus-patch|u
2216|2282|firewall/aetheus-firewall|q
2286|2295|firewall/sudoers.d/aetheus-firewall|u
2315|2561|mail/mail-setup|q
2417|2445|mail/dovecot-2.3/99-aetheus.conf|n
2446|2451|mail/dovecot-2.3/99-aetheus-ssl.conf|n
2454|2492|mail/dovecot-2.4/99-aetheus.conf|n
2493|2498|mail/dovecot-2.4/99-aetheus-ssl.conf|n
2524|2539|mail/opendkim.conf|n
2569|3027|mail/mail-manage|q
3035|3049|mail/sudoers.d/aetheus-mail|u
3069|3197|teamspeak/teamspeak-setup|q
3156|3172|teamspeak/teamspeak3.service|n
3201|3210|teamspeak/sudoers.d/aetheus-teamspeak|u
3224|3243|rkhunter/sudoers.d/aetheus-rkhunter|u
3417|3493|agent/aetheus-agent.service|u
4140|4174|agent/appsettings.json|u'

# The reference post-processed three quoted helpers with sed (reference lines 1379, 1472, 3029).
acme_sed_flags() {
    case "$1" in
        certbot/aetheus-certbot-issue|certbot/aetheus-certbot-manage) printf '%s' '' ;;
        mail/mail-manage) printf '%s' 'g' ;;
        *) return 1 ;;
    esac
}

# --- Value sets --------------------------------------------------------------------------------
# Installer constants (AGENT_USER .. DEFAULT_SERVER_URL), read from the current installer.
sed -n '/^AGENT_USER=/,/^DEFAULT_SERVER_URL=/p' "$installer" > "$work/constants.sh"

scenario_defaults() {
    # shellcheck disable=SC1091
    . "$work/constants.sh"
    ACME_WEBROOT="/var/www/aetheus-acme"
    EXEC_START="$INSTALL_DIR/Aetheus.Agent.Linux"
    STATE_DIRECTORY_MODE="0700"
    # The installer's own sandboxed-branch value (restored NoNewPrivileges=true, see below).
    eval "$(sed -n '/^        NNP_BLOCK="# Fully sandboxed/,/^NoNewPrivileges=true"$/p' "$installer")"
    SANDBOX_PRIV_BLOCK="RestrictSUIDSGID=true
# CapabilityBoundingSet emptied - a non-root collector needs no capabilities.
CapabilityBoundingSet="
    PROTECT_SYSTEM_BLOCK="ProtectSystem=strict"
    READ_WRITE_PATHS_BLOCK=""
    ADDRESS_FAMILIES="AF_UNIX AF_INET AF_INET6"
    SERVER_URL_J="https://aetheus-api.example.com"
    REG_TOKEN_J="registration-token"
    AGENT_NAME_J="web-01"
    WORK_DIR_J="$WORK_DIR"
    ALLOW_INSECURE_JSON="false"
    DOCKER_STORAGE_DEPLOYMENT_ONLY="false"
    # Deliberate difference (user decision): the reference sandboxed branch never set NNP_BLOCK
    # (regression of df6895eb9), so its unit carried an empty line where NoNewPrivileges=true used
    # to be. The installer now restores that block, and the reference side is evaluated with the
    # same restored value; the "Restored NoNewPrivileges=true" section proves it is the only difference.
    OLD_UNSET=""
}

scenario_relaxed() {
    scenario_defaults
    OLD_UNSET=""
    ACME_WEBROOT="/var/www/custom-acme"
    EXEC_START="/usr/bin/dotnet $INSTALL_DIR/Aetheus.Agent.Linux.dll"
    STATE_DIRECTORY_MODE="0710"
    NNP_BLOCK="# RELAXED: a sudo grant (--enable-service-control / --enable-package-manage /
# --enable-mail-setup / --enable-teamspeak-setup / --enable-apache-manage / --enable-certbot-manage /
# --module deployment) requires NoNewPrivileges=false (sudo needs setgid(0) for 'systemctl reload
# apache2', the certbot helper, etc.). This is a deliberate trade-off (sudo escalation surface) -
# disable those capabilities to restore NoNewPrivileges=true.
NoNewPrivileges=false"
    SANDBOX_PRIV_BLOCK="RestrictSUIDSGID=false
# CapabilityBoundingSet intentionally left at the default (full set) so the sudo grant can elevate."
    PROTECT_SYSTEM_BLOCK="# RELAXED: package/mail/deploy grants write broad system paths, so the read-only system mount is off.
ProtectSystem=false"
    READ_WRITE_PATHS_BLOCK=" /etc/apache2 /etc/letsencrypt /var/lib/letsencrypt /var/log/letsencrypt /var/www/custom-acme /var/lib/aetheus-certbot"
    ADDRESS_FAMILIES="AF_UNIX AF_INET AF_INET6 AF_NETLINK"
    SERVER_URL_J="http://localhost:5000"
    ALLOW_INSECURE_JSON="true"
    DOCKER_STORAGE_DEPLOYMENT_ONLY="true"
}

scenario_hostile() {
    scenario_defaults
    OLD_UNSET=""
    _h='a&b\c|d$HOME`id`"q'"'"'%s#{AGENT_USER}#}#
second line \\ &&'
    for _v in AGENT_UPDATE_WORKER_PATH AGENT_UPDATE_REQUEST_FILE AGENT_USER AGENT_GROUP \
        APACHE_CONFIGTEST_HELPER_PATH APACHE_RELOAD_HELPER_PATH CERTBOT_ISSUE_HELPER_PATH \
        CERTBOT_MANAGE_HELPER_PATH CRON_HELPER_PATH DEPLOY_READ_GROUP DEPLOY_BASE_DIR \
        DEPLOY_RESTART_HELPER_PATH UNBLOCK_HELPER_PATH PORTSENTRY_SETUP_HELPER_PATH \
        FIREWALL_HELPER_PATH MAIL_SETUP_HELPER_PATH MAIL_MANAGE_HELPER_PATH \
        TEAMSPEAK_SETUP_HELPER_PATH INSTALL_DIR WORK_DIR STATE_DIRECTORY_MODE EXEC_START NNP_BLOCK \
        PROTECT_SYSTEM_BLOCK READ_WRITE_PATHS_BLOCK SANDBOX_PRIV_BLOCK ADDRESS_FAMILIES \
        SERVER_URL_J REG_TOKEN_J AGENT_NAME_J WORK_DIR_J ALLOW_INSECURE_JSON DOCKER_STORAGE_DEPLOYMENT_ONLY; do
        eval "$_v=\"\$_h-$_v\""
    done
    # The ACME web root keeps a path shape: the reference substituted it with sed, whose own
    # metacharacters (& | \) the reference never had to survive (require_web_root bounds it).
    ACME_WEBROOT="/var/www/acme-root_2"
}

# --- Reference side ----------------------------------------------------------------------------
# Writes to $3 what the reference heredoc between lines $1 and $2 wrote, evaluated by this shell.
reference_output() {
    _ro_opener="$(sed -n "${1}p" "$reference")"
    case "$_ro_opener" in
        *'cat > '*'<<'*) ;;
        *) echo "reference line $1 is not a cat > heredoc: $_ro_opener" >&2; return 1 ;;
    esac
    _ro_delim="${_ro_opener##*<<}"
    {
        printf 'cat > "$__ro_out" <<%s\n' "$_ro_delim"
        sed -n "$(($1 + 1)),${2}p" "$reference"
    } > "$work/reference-snippet.sh"
    __ro_out="$3"
    eval "$(cat "$work/reference-snippet.sh")"
}

# --- Equivalence -------------------------------------------------------------------------------
templates=0
for scenario in defaults relaxed hostile; do
    printf '%s\n' "$MAP" > "$work/map.txt"
    while IFS='|' read -r start end rel kind; do
        [ "$scenario" = defaults ] && templates=$((templates + 1))
        old="$work/old"
        new="$work/new"
        rm -f "$old" "$new"
        case "$kind" in
            n)
                [ "$scenario" = defaults ] || continue
                sed -n "$((start + 1)),$((end - 1))p" "$reference" > "$old"
                cp "$HOST_CONFIG_DIR/$rel" "$new"
                ;;
            q|u)
                if ! (
                    set +u
                    "scenario_$scenario"
                    for _u in $OLD_UNSET; do unset "$_u"; done
                    reference_output "$start" "$end" "$old"
                    if _flags="$(acme_sed_flags "$rel")"; then
                        sed -i "s|__AETHEUS_ACME_WEBROOT__|${ACME_WEBROOT}|$_flags" "$old"
                    fi
                ); then
                    fail "$rel [$scenario]: reference heredoc could not be evaluated"
                    continue
                fi
                if ! ( "scenario_$scenario"; render_host_config "$rel" "$new" ) > "$work/render.log" 2>&1; then
                    fail "$rel [$scenario]: render_host_config refused: $(cat "$work/render.log")"
                    continue
                fi
                ;;
        esac
        if cmp -s "$old" "$new"; then
            pass "$rel [$scenario] $(wc -c < "$new" | tr -d ' ') bytes identical"
        else
            fail "$rel [$scenario] differs:"
            diff "$old" "$new" | head -20 || true
        fi
    done < "$work/map.txt"
done

# --- Restored NoNewPrivileges=true (sandboxed agent) --------------------------------------------
# The block as the unit had it before df6895eb9 (text of df6895eb9^, same position: the line right
# after "# --- Security hardening ---").
expected_nnp='# Fully sandboxed: no sudo grant, so privilege escalation is blocked outright.
NoNewPrivileges=true'
installer_nnp="$( scenario_defaults; printf '%s' "$NNP_BLOCK" )"
if [ "$installer_nnp" = "$expected_nnp" ]; then
    pass "restored NNP_BLOCK: installer sandboxed branch = pre-df6895eb9 text"
else
    fail "restored NNP_BLOCK: installer sandboxed branch differs from the pre-df6895eb9 text"
fi
# Raw reference (NNP_BLOCK unset, as the reference really ran) versus the current render: the only
# difference allowed is the empty line after the hardening header becoming the restored block.
rm -f "$work/old" "$work/new"
( set +u; scenario_defaults; unset NNP_BLOCK; reference_output 3417 3493 "$work/old" )
( scenario_defaults; render_host_config agent/aetheus-agent.service "$work/new" ) > /dev/null
NNP_EXPECTED="$expected_nnp" awk '
    after_header && $0 == "" { print ENVIRON["NNP_EXPECTED"]; after_header = 0; next }
    { after_header = ($0 == "# --- Security hardening ---"); print }' "$work/old" > "$work/old-restored"
if cmp -s "$work/old-restored" "$work/new" && ! cmp -s "$work/old" "$work/new" \
    && grep -qx 'NoNewPrivileges=true' "$work/new"; then
    pass "agent/aetheus-agent.service [defaults] differs from the raw reference only by the restored NoNewPrivileges=true block"
else
    fail "agent/aetheus-agent.service [defaults]: difference with the raw reference is not exactly the restored block"
    diff "$work/old" "$work/new" | head -20 || true
fi
( scenario_relaxed; render_host_config agent/aetheus-agent.service "$work/new" ) > /dev/null
if grep -qx 'NoNewPrivileges=false' "$work/new" && ! grep -qx 'NoNewPrivileges=true' "$work/new"; then
    pass "agent/aetheus-agent.service [relaxed] keeps NoNewPrivileges=false only"
else
    fail "agent/aetheus-agent.service [relaxed] NoNewPrivileges lines changed"
fi

# --- Refusals ----------------------------------------------------------------------------------
# Each case renders into a destination that already holds a sentinel, which must survive.
expect_refusal() {
    _er_label="$1"; shift
    printf 'sentinel\n' > "$work/dest"
    if ( "$@" ) > "$work/refusal.log" 2>&1; then
        fail "refusal: $_er_label (rendered instead of refusing)"
    elif [ "$(cat "$work/dest")" != sentinel ]; then
        fail "refusal: $_er_label (destination modified)"
    else
        pass "refusal: $_er_label ($(grep -m1 -o 'render_host_config: .*\|Host configuration template .*\|Invalid host configuration .*\|Refusing to install .*' "$work/refusal.log" || true))"
    fi
}

copy_tree() { rm -rf "$work/tree"; cp -R "$HOST_CONFIG_DIR" "$work/tree"; }

case_unset_value() { scenario_defaults; unset AGENT_USER; render_host_config cron/sudoers.d/aetheus-cron "$work/dest"; }
case_undeclared() {
    copy_tree; printf '#{UNDECLARED_NAME}#\n' >> "$work/tree/cron/cron-apply"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config cron/cron-apply "$work/dest"
}
case_unused() {
    copy_tree; sed -i 's|^cron/cron-apply$|cron/cron-apply AGENT_USER|' "$work/tree/manifest"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config cron/cron-apply "$work/dest"
}
case_unterminated() {
    copy_tree; printf 'x #{AGENT_USER\n' >> "$work/tree/cron/sudoers.d/aetheus-cron"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config cron/sudoers.d/aetheus-cron "$work/dest"
}
case_crlf() {
    copy_tree; sed -i 's/$/\r/' "$work/tree/cron/cron-apply"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config cron/cron-apply "$work/dest"
}
case_crlf_include() {
    copy_tree; sed -i 's/$/\r/' "$work/tree/mail/opendkim.conf"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config mail/mail-setup "$work/dest"
}
case_not_in_manifest() {
    copy_tree; sed -i '/^cron\/cron-apply$/d' "$work/tree/manifest"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config cron/cron-apply "$work/dest"
}
case_missing_template() {
    copy_tree; rm "$work/tree/cron/cron-apply"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config cron/cron-apply "$work/dest"
}
case_missing_include() {
    copy_tree; rm "$work/tree/mail/opendkim.conf"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config mail/mail-setup "$work/dest"
}
case_placeholder_in_include() {
    copy_tree; printf '#{AGENT_USER}#\n' >> "$work/tree/mail/opendkim.conf"
    scenario_defaults; HOST_CONFIG_DIR="$work/tree"; render_host_config mail/mail-setup "$work/dest"
}
case_traversal() { scenario_defaults; render_host_config ../scripts/install-agent-linux.sh "$work/dest"; }

expect_refusal "declared placeholder without value" case_unset_value
# The agent unit, in both modes: an unfilled block must stop the installation (the former empty line).
case_unit_unset_nnp_sandboxed() { scenario_defaults; unset NNP_BLOCK; render_host_config agent/aetheus-agent.service "$work/dest"; }
case_unit_unset_nnp_relaxed() { scenario_relaxed; unset NNP_BLOCK; render_host_config agent/aetheus-agent.service "$work/dest"; }
case_unit_unset_sandbox_relaxed() { scenario_relaxed; unset SANDBOX_PRIV_BLOCK; render_host_config agent/aetheus-agent.service "$work/dest"; }
expect_refusal "agent unit, sandboxed mode, NNP_BLOCK unfilled" case_unit_unset_nnp_sandboxed
expect_refusal "agent unit, elevated mode, NNP_BLOCK unfilled" case_unit_unset_nnp_relaxed
expect_refusal "agent unit, elevated mode, SANDBOX_PRIV_BLOCK unfilled" case_unit_unset_sandbox_relaxed
expect_refusal "undeclared placeholder in template" case_undeclared
expect_refusal "declared placeholder unused by template" case_unused
expect_refusal "unterminated placeholder" case_unterminated
expect_refusal "carriage return in template" case_crlf
expect_refusal "carriage return in an included template" case_crlf_include
expect_refusal "template not declared in manifest" case_not_in_manifest
expect_refusal "template file missing" case_missing_template
expect_refusal "included template missing" case_missing_include
expect_refusal "placeholder inside an included template" case_placeholder_in_include
expect_refusal "path traversal in template name" case_traversal

# --- Structure ---------------------------------------------------------------------------------
reference_writes="$(grep -cE '^[[:space:]]*cat > .*<<' "$reference")"
manifest_entries="$(grep -cvE '^[[:space:]]*(#|$)' "$HOST_CONFIG_DIR/manifest")"
files_on_disk="$(find "$HOST_CONFIG_DIR" -type f ! -name manifest | wc -l | tr -d ' ')"
installer_heredocs="$(grep -cE '(cat|tee)[^|]*>[^|]*<<' "$installer" || true)"
printf 'COUNT reference cat> heredocs=%s map rows=%s manifest entries=%s template files=%s installer heredoc writes left=%s\n' \
    "$reference_writes" "$templates" "$manifest_entries" "$files_on_disk" "$installer_heredocs"
[ "$reference_writes" -eq 42 ] && [ "$templates" -eq 42 ] && [ "$manifest_entries" -eq 42 ] \
    && [ "$files_on_disk" -eq 42 ] && [ "$installer_heredocs" -eq 0 ] \
    && pass "structure: 42 heredocs = 42 templates = 42 manifest entries, none left in the installer" \
    || fail "structure: counts above do not all match 42 / 0"

# Every former `cat > TARGET <<` became exactly one `render_host_config <template> TARGET`, with the
# very same TARGET expression (so the same file, written the same way, at the same point).
printf '%s\n' "$MAP" > "$work/map.txt"
targets_ok=0
while IFS='|' read -r start end rel kind; do
    [ "$kind" = n ] && continue
    target="$(sed -n "${start}p" "$reference" | sed 's/^[[:space:]]*cat > //; s/ <<.*$//')"
    calls="$(grep -cxF "$(sed -n "${start}p" "$reference" | sed 's/cat > .*$//')render_host_config $rel $target" "$installer" || true)"
    if [ "$calls" -eq 1 ]; then
        targets_ok=$((targets_ok + 1))
    else
        fail "call site: render_host_config $rel $target found $calls times (expected 1)"
    fi
done < "$work/map.txt"
[ "$targets_ok" -eq 34 ] && pass "call sites: 34 render_host_config calls keep the exact target and indentation of their heredoc"

# Every file on disk is declared, every declared template exists.
find "$HOST_CONFIG_DIR" -type f ! -name manifest | sed "s|^$HOST_CONFIG_DIR/||" | sort > "$work/on-disk"
grep -vE '^[[:space:]]*(#|$)' "$HOST_CONFIG_DIR/manifest" | awk '{ print $1 }' | sort > "$work/declared"
if cmp -s "$work/on-disk" "$work/declared"; then pass "manifest lists exactly the files on disk"; else fail "manifest and files on disk differ"; diff "$work/declared" "$work/on-disk" || true; fi

# Every template the installer renders is declared, and every declared template is either
# rendered by the installer or included by a rendered one.
grep -E '^[[:space:]]*render_host_config ' "$installer" | awk '{ print $2 }' | sort -u > "$work/rendered"
grep -rhoE '^#\{include:[^}]+\}#$' "$HOST_CONFIG_DIR" | sed 's/^#{include://; s/}#$//' | sort -u > "$work/included"
sort -u "$work/rendered" "$work/included" > "$work/used"
if cmp -s "$work/used" "$work/declared"; then
    pass "installer renders $(wc -l < "$work/rendered" | tr -d ' ') templates + $(wc -l < "$work/included" | tr -d ' ') included = every declared template"
else
    fail "rendered/included templates and manifest differ"; diff "$work/declared" "$work/used" || true
fi

if [ "$failures" -eq 0 ]; then
    echo "RESULT: PASS"
    exit 0
fi
echo "RESULT: FAIL ($failures)"
exit 1
