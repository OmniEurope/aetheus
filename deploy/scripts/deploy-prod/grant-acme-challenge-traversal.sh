# SPDX-License-Identifier: EUPL-1.2
#
# Gives Apache the traversal it needs to reach the ACME challenge directory under the agent's work
# directory, so certificate renewal answers the challenge instead of 403.
#
# This was 8 lines inlined in .pipeline/aetheus-deploy-prod.yaml. The body is relocated unchanged
# apart from ACME_WEBROOT and the agent directory, noted below.
#
# Inputs, all read from the environment (none positional):
#   ACME_WEBROOT                  the challenge web root (aetheus.prod library). It was written
#                                 `$(...)` in the YAML, which the control plane substitutes in a
#                                 `shell:` block; inside a script that same text is a shell command
#                                 substitution, so the caller passes it by name.
#   AETHEUS_AGENT_WORK_DIRECTORY  published by the agent on every task: the directory the web root
#                                 of an older helper lives in (PLAN-003 2.1, no literal host path).
set -eu
AGENT_WORK_DIR="${AETHEUS_AGENT_WORK_DIRECTORY:?AETHEUS_AGENT_WORK_DIRECTORY is required}"
case "${ACME_WEBROOT:?ACME_WEBROOT is required}" in
  "$AGENT_WORK_DIR"/*)
    # The traversal bit on the agent work directory is the whole fix and must succeed.
    chmod o+x "$AGENT_WORK_DIR" ;;
  *)
    # A helper reprovisioned by a recent installer writes under /var/www, which Apache can already
    # read: there is nothing to grant.
    echo ">>> The ACME web root is not under the agent work directory; no traversal to grant." ;;
esac
# The web root itself belongs to root, created by the root-owned certbot helper, so the
# agent cannot chmod it - and does not need to: root created it world-readable, which the
# challenge answering 404 instead of 403 confirms. Best-effort, never fatal.
chmod -R o+rX "${ACME_WEBROOT}" 2>/dev/null || true
echo ">>> Traversal granted; Apache can reach the ACME challenge directory."
