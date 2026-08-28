import assert from "node:assert/strict";
import test from "node:test";
import {
  createAetheusAnalytics,
  normalizeRoute,
  resolveRouteTemplate
} from "../src/aetheus-web-analytics.js";

test("normalizes numeric and UUID route values", () => {
  assert.equal(normalizeRoute("/Orders/42"), "/orders/{id}");
  assert.equal(
    normalizeRoute("/users/6b40f4fd-53ac-4f84-8c9e-9fac6feec6e1"),
    "/users/{id}"
  );
});

test("rejects query strings and fragments", () => {
  assert.throws(() => normalizeRoute("/orders?id=42"));
  assert.throws(() => normalizeRoute("/orders#details"));
  assert.throws(() => normalizeRoute(`/${"a".repeat(256)}`));
});

test("resolves business identifiers only through an explicit route allow-list", () => {
  assert.equal(
    resolveRouteTemplate(
      "/Customers/acme-corporation/Orders/quarterly-renewal",
      ["/customers/{customerKey}/orders/{orderKey}"]
    ),
    "/customers/{customerkey}/orders/{orderkey}"
  );
  assert.equal(
    resolveRouteTemplate("/admin/secret-account", ["/customers/{customerKey}"]),
    undefined
  );
  assert.equal(resolveRouteTemplate("/customers/acme", []), undefined);
});

test("supports Angular-style SPA history hooks and restores them on stop", () => {
  const originalPushState = () => undefined;
  const originalReplaceState = () => undefined;
  const listeners = new Map();
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: {
      globalPrivacyControl: true,
      doNotTrack: "0",
      sendBeacon: () => true
    }
  });
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: {
      location: { pathname: "/" },
      doNotTrack: "0",
      addEventListener: (name, callback) => listeners.set(name, callback),
      removeEventListener: (name) => listeners.delete(name)
    }
  });
  Object.defineProperty(globalThis, "history", {
    configurable: true,
    value: {
      pushState: originalPushState,
      replaceState: originalReplaceState
    }
  });

  const analytics = createAetheusAnalytics({ routeResolver: (pathname) => pathname });
  analytics.install();
  const wrappedPushState = history.pushState;
  analytics.install();
  assert.equal(history.pushState, wrappedPushState);
  assert.equal(listeners.has("popstate"), true);

  analytics.stop();
  assert.equal(history.pushState, originalPushState);
  assert.equal(history.replaceState, originalReplaceState);
  assert.equal(listeners.has("popstate"), false);
});

test("tracks an initial static page without framework or persistent browser storage", async () => {
  const payloads = [];
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: {
      globalPrivacyControl: false,
      doNotTrack: "0",
      sendBeacon: (_endpoint, blob) => {
        payloads.push(blob);
        return true;
      }
    }
  });
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: {
      location: { pathname: "/About" },
      doNotTrack: "0",
      addEventListener: () => undefined,
      removeEventListener: () => undefined
    }
  });
  Object.defineProperty(globalThis, "history", {
    configurable: true,
    value: {
      pushState: () => undefined,
      replaceState: () => undefined
    }
  });
  Object.defineProperty(globalThis, "crypto", {
    configurable: true,
    value: { randomUUID: () => "6b40f4fd-53ac-4f84-8c9e-9fac6feec6e1" }
  });

  const analytics = createAetheusAnalytics({ routeResolver: (pathname) => pathname });
  analytics.install();

  const events = await Promise.all(payloads.map((payload) => payload.text().then(JSON.parse)));
  assert.equal(events.length, 1);
  assert.equal(events[0].kind, "page_view");
  assert.equal(events[0].route, "/about");
  assert.equal("cookie" in events[0], false);
  assert.equal("localStorage" in events[0], false);

  analytics.stop();
});

test("uses an application route resolver so dynamic slugs never leave the browser", async () => {
  const payloads = [];
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: {
      globalPrivacyControl: false,
      doNotTrack: "0",
      sendBeacon: (_endpoint, blob) => {
        payloads.push(blob);
        return true;
      }
    }
  });
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: {
      location: { pathname: "/users/private-account-name" },
      doNotTrack: "0",
      addEventListener: () => undefined,
      removeEventListener: () => undefined
    }
  });
  Object.defineProperty(globalThis, "history", {
    configurable: true,
    value: {
      pushState: () => undefined,
      replaceState: () => undefined
    }
  });
  Object.defineProperty(globalThis, "crypto", {
    configurable: true,
    value: { randomUUID: () => "6b40f4fd-53ac-4f84-8c9e-9fac6feec6e1" }
  });

  const analytics = createAetheusAnalytics({
    routeResolver: () => "/users/{id}"
  });
  analytics.install();

  const event = JSON.parse(await payloads[0].text());
  assert.equal(event.route, "/users/{id}");
  assert.equal(JSON.stringify(event).includes("private-account-name"), false);
  analytics.stop();
});

test("captures only explicitly enabled bounded browser signals", async () => {
  const listeners = new Map();
  const payloads = [];
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: {
      globalPrivacyControl: false,
      doNotTrack: "0",
      sendBeacon: (_endpoint, blob) => {
        payloads.push(blob);
        return true;
      }
    }
  });
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: {
      location: { pathname: "/projects/42" },
      doNotTrack: "0",
      addEventListener: (name, callback) => listeners.set(name, callback),
      removeEventListener: (name) => listeners.delete(name)
    }
  });
  Object.defineProperty(globalThis, "history", {
    configurable: true,
    value: {
      pushState: () => undefined,
      replaceState: () => undefined
    }
  });
  Object.defineProperty(globalThis, "crypto", {
    configurable: true,
    value: { randomUUID: () => "6b40f4fd-53ac-4f84-8c9e-9fac6feec6e1" }
  });

  const analytics = createAetheusAnalytics({
    routeResolver: (pathname) => pathname,
    captureErrors: true,
    capturePerformance: true
  });
  analytics.install();
  listeners.get("error")?.({ message: "must never be sent", filename: "secret.js" });
  listeners.get("unhandledrejection")?.({ reason: "must never be sent" });
  analytics.trackPerformance(123.6);
  analytics.trackPerformance(300001);

  const events = await Promise.all(payloads.map((payload) => payload.text().then(JSON.parse)));
  assert.deepEqual(
    events.map((event) => event.kind),
    ["page_view", "browser_error", "browser_error", "browser_performance"]
  );
  assert.equal(events[0].route, "/projects/{id}");
  assert.equal(events[1].errorType, "script_error");
  assert.equal(events[2].errorType, "unhandled_rejection");
  assert.equal(events[3].durationMs, 124);
  assert.equal(JSON.stringify(events).includes("must never be sent"), false);

  analytics.stop();
  assert.equal(listeners.has("error"), false);
  assert.equal(listeners.has("unhandledrejection"), false);
  assert.equal(listeners.has("load"), false);
});
