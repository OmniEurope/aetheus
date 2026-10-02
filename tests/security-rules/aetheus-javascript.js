// SPDX-License-Identifier: EUPL-1.2
// Test corpus for .aetheus/security-rules/opengrep/aetheus-javascript.yml (opengrep scan --test).

const target = document.getElementById("out");
const fromUrl = location.hash.slice(1);
// ruleid: aetheus.javascript.dom-xss
target.innerHTML = fromUrl;
// ok: aetheus.javascript.dom-xss
target.textContent = fromUrl;
// ok: aetheus.javascript.dom-xss
target.innerHTML = DOMPurify.sanitize(fromUrl);

window.addEventListener("message", (event) => {
  // ruleid: aetheus.javascript.dom-xss
  document.write(event.data);
});

const stored = localStorage.getItem("draft");
// ruleid: aetheus.javascript.dom-xss
eval(stored);

// ok: aetheus.javascript.dom-xss
target.innerHTML = "<b>static</b>";

const accessToken = "value";
// ruleid: aetheus.javascript.secret-in-console
console.log("token is", accessToken);
// ok: aetheus.javascript.secret-in-console
console.log("the token expired, signing out");
// ok: aetheus.javascript.secret-in-console
console.warn("retrying", attempt);

// ruleid: aetheus.javascript.fetch-credentials-include
fetch("https://api.example.org/data", { credentials: "include" });
// ok: aetheus.javascript.fetch-credentials-include
fetch("/local", { credentials: "same-origin" });
