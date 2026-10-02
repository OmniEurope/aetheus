# SPDX-License-Identifier: EUPL-1.2
# Test corpus for .aetheus/security-rules/opengrep/aetheus-security.yml, Python rule. Never run.
import subprocess

# ruleid: aetheus.python.subprocess-shell
subprocess.run("ls " + folder, shell=True)
# ok: aetheus.python.subprocess-shell
subprocess.run(["ls", folder])
