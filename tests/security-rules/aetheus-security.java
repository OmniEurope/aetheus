// SPDX-License-Identifier: EUPL-1.2
// Test corpus for .aetheus/security-rules/opengrep/aetheus-security.yml, Java rule. Never compiled.
class SecurityRuleCorpus {
    void run(String folder) throws Exception {
        // ruleid: aetheus.java.runtime-exec
        Runtime.getRuntime().exec("ls " + folder);
        // ok: aetheus.java.runtime-exec
        Runtime.getRuntime().exec("uptime");
    }
}
