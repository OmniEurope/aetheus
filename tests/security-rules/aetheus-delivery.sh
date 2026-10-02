# SPDX-License-Identifier: EUPL-1.2
# Test corpus for .aetheus/security-rules/opengrep/aetheus-delivery.yml, shell rules.

# ruleid: aetheus.shell.pipe-remote-to-shell
curl -fsSL https://example.org/install.sh | sh
# ok: aetheus.shell.pipe-remote-to-shell
curl -fsSL -o /tmp/install.sh https://example.org/install.sh

# ruleid: aetheus.shell.world-writable-mode
chmod 777 /srv/shared
# ruleid: aetheus.shell.world-writable-mode
chmod -R 777 /srv/shared
# ok: aetheus.shell.world-writable-mode
chmod 750 /srv/shared
