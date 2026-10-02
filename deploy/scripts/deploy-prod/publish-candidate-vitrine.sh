# SPDX-License-Identifier: EUPL-1.2
#
# Publishes the vitrine, then the documentation site when the candidate carries one.
#
# The docs are a separate property with their own web root and their own snapshot, so a candidate
# built before they existed carries none: that case is skipped rather than failing an otherwise
# complete deployment.
#
# This was 14 lines inlined in .pipeline/aetheus-deploy-prod.yaml. The body is relocated unchanged
# apart from WORKSPACE, noted below.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE  the run's checkout, holding .delivery-vitrine. It was written `$(...)` in the YAML,
#              which the control plane substitutes in a `shell:` block; inside a script that same
#              text is a shell command substitution, and the agent already exports WORKSPACE.
set -eu
sh deploy/scripts/prod-vitrine-transaction.sh publish vitrine
# The documentation site is its own property with its own web root and its own snapshot.
# A candidate built before the docs existed carries none, so it is skipped rather than
# failing a deployment that is otherwise complete.
if [ -s "${WORKSPACE}/.delivery-vitrine/docs-site/public/index.html" ]; then
  # Publishing the files is not publishing the site: until this ran, the docs landed in
  # /var/www/aetheus-docs and no vhost served them. Provisions the certificate and the two
  # vhosts on first deployment, then does nothing on every later one.
  sh deploy/scripts/prod-docs-vhost.sh
  sh deploy/scripts/prod-vitrine-transaction.sh publish docs
else
  echo ">>> No documentation site in this candidate; skipping its publication."
fi
