// SPDX-License-Identifier: EUPL-1.2
// Test corpus for .aetheus/security-rules/opengrep/aetheus-security.yml, JavaScript rule.
// ruleid: aetheus.javascript.dynamic-eval
eval(userInput);
// ruleid: aetheus.javascript.dynamic-eval
const compiled = new Function("a", body);
// ok: aetheus.javascript.dynamic-eval
const parsed = JSON.parse(userInput);
