const DEFAULT_ENDPOINT = "/aetheus-analytics/v1/events";

export function normalizeRoute(pathname) {
  if (typeof pathname !== "string"
      || pathname.length > 256
      || !pathname.startsWith("/")
      || pathname.includes("?")
      || pathname.includes("#")) {
    throw new TypeError("A rooted path without query string or fragment is required.");
  }

  const segments = pathname.split("/").filter(Boolean).map((segment) => {
    if (/^\d+$/.test(segment) || /^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(segment)) {
      return "{id}";
    }
    return segment.toLowerCase();
  });
  return `/${segments.join("/")}`;
}

export function resolveRouteTemplate(pathname, templates) {
  const normalizedPath = normalizeRoute(pathname);
  if (!Array.isArray(templates) || templates.length === 0) {
    return undefined;
  }

  const pathSegments = normalizedPath.split("/").filter(Boolean);
  for (const template of templates) {
    if (typeof template !== "string") {
      continue;
    }
    let normalizedTemplate;
    try {
      normalizedTemplate = normalizeRoute(template);
    } catch {
      continue;
    }
    const templateSegments = normalizedTemplate.split("/").filter(Boolean);
    if (templateSegments.length !== pathSegments.length) {
      continue;
    }
    const matches = templateSegments.every((segment, index) =>
      /^\{[a-z][a-z0-9_-]*\}$/i.test(segment)
      || segment === pathSegments[index]);
    if (matches) {
      return normalizedTemplate;
    }
  }
  return undefined;
}

export function createAetheusAnalytics(configuration = {}) {
  const endpoint = configuration.endpoint ?? DEFAULT_ENDPOINT;
  const routeResolver = configuration.routeResolver;
  const routeTemplates = configuration.routeTemplates;
  const respectDoNotTrack = configuration.respectDoNotTrack !== false;
  const captureErrors = configuration.captureErrors === true;
  const capturePerformance = configuration.capturePerformance === true;
  let stopped = false;
  let installed = false;
  let previousRoute = "";
  let originalPushState;
  let originalReplaceState;
  let pushStateWrapper;
  let replaceStateWrapper;

  function privacySignalEnabled() {
    return navigator.globalPrivacyControl === true
      || (respectDoNotTrack && (navigator.doNotTrack === "1" || window.doNotTrack === "1"));
  }

  function currentRoute() {
    try {
      if (typeof routeResolver === "function") {
        return normalizeRoute(routeResolver(window.location.pathname));
      }
      return resolveRouteTemplate(window.location.pathname, routeTemplates);
    } catch {
      return undefined;
    }
  }

  function emit(kind, details = {}) {
    if (stopped || privacySignalEnabled()) {
      return;
    }

    const route = currentRoute();
    if (!route) {
      return;
    }

    const event = JSON.stringify({
      schemaVersion: 1,
      eventId: crypto.randomUUID(),
      occurredAtUtc: new Date().toISOString(),
      kind,
      route,
      ...details
    });
    if (event.length > 1024) {
      return;
    }

    if (typeof navigator.sendBeacon === "function"
        && navigator.sendBeacon(endpoint, new Blob([event], { type: "application/json" }))) {
      return;
    }

    void fetch(endpoint, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: event,
      credentials: "same-origin",
      keepalive: true
    }).catch(() => undefined);
  }

  function emitPageView() {
    const route = currentRoute();
    if (!route || route === previousRoute) {
      return;
    }
    previousRoute = route;
    emit("page_view");
  }

  function emitScriptError() {
    if (captureErrors) {
      emit("browser_error", { errorType: "script_error" });
    }
  }

  function emitUnhandledRejection() {
    if (captureErrors) {
      emit("browser_error", { errorType: "unhandled_rejection" });
    }
  }

  function emitPerformance(durationMs) {
    if (!capturePerformance
        || !Number.isFinite(durationMs)
        || durationMs < 0
        || durationMs > 300000) {
      return;
    }
    emit("browser_performance", { durationMs: Math.round(durationMs) });
  }

  function emitNavigationPerformance() {
    if (!capturePerformance || typeof performance === "undefined") {
      return;
    }
    const navigation = performance.getEntriesByType?.("navigation")?.[0];
    if (navigation) {
      emitPerformance(navigation.duration);
    }
  }

  function install() {
    if (installed || stopped) {
      return;
    }
    installed = true;
    originalPushState = history.pushState;
    originalReplaceState = history.replaceState;
    pushStateWrapper = function (...args) {
      originalPushState.apply(this, args);
      emitPageView();
    };
    replaceStateWrapper = function (...args) {
      originalReplaceState.apply(this, args);
      emitPageView();
    };
    history.pushState = pushStateWrapper;
    history.replaceState = replaceStateWrapper;
    window.addEventListener("popstate", emitPageView);
    if (captureErrors) {
      window.addEventListener("error", emitScriptError);
      window.addEventListener("unhandledrejection", emitUnhandledRejection);
    }
    if (capturePerformance) {
      if (typeof document !== "undefined" && document.readyState === "complete") {
        emitNavigationPerformance();
      } else {
        window.addEventListener("load", emitNavigationPerformance);
      }
    }
    emitPageView();
  }

  function stop() {
    stopped = true;
    installed = false;
    window.removeEventListener("popstate", emitPageView);
    window.removeEventListener("error", emitScriptError);
    window.removeEventListener("unhandledrejection", emitUnhandledRejection);
    window.removeEventListener("load", emitNavigationPerformance);
    if (history.pushState === pushStateWrapper) {
      history.pushState = originalPushState;
    }
    if (history.replaceState === replaceStateWrapper) {
      history.replaceState = originalReplaceState;
    }
  }

  return {
    install,
    stop,
    trackPageView: emitPageView,
    trackPerformance: emitPerformance
  };
}
