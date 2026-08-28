// SPDX-License-Identifier: EUPL-1.2

const configurationEndpoint = "/aetheus-analytics/v1/configuration";
const analyticsModule = "/_content/Aetheus.WebAnalytics/0.1.0/aetheus-web-analytics.js";

// Only known Aetheus top-level routes are measured. Every non-static nested segment is
// replaced before it can leave the browser, covering numeric ids, SHAs, slugs, branch names,
// settings tabs, and future route parameters without maintaining a list of user values.
const topLevelSegments = new Set([
  "account", "admin", "ai-tasks", "alerts", "analysis", "api-reference", "artifacts",
  "audit", "backups", "dashboards", "environments", "git", "git-repositories", "help",
  "login", "logs", "not-found", "pipelines", "plugins", "projects", "releases",
  "servers", "service-connections", "settings", "tasks", "templates", "users",
  "variable-libraries", "vaults"
]);

const staticNestedSegments = new Set([
  "add-agent", "ai-profiles", "apache", "api-reference", "apps", "artifacts", "audit",
  "backups", "board", "branches", "certbot", "change-password", "commits", "configuration",
  "cron", "dashboards", "docker", "edit", "environments", "external-repo", "extract",
  "findings", "firewall", "fleet", "libraries", "logs", "mail", "modules", "monitoring",
  "new", "notifications", "organizations", "overview", "package-feeds", "package-registry",
  "pipelines", "plugins", "portsentry", "projects", "promote", "properties", "quality",
  "releases", "rkhunter", "roles", "runs", "servers", "services", "system-logs", "tasks",
  "teamspeak", "template", "update", "updates", "users", "vaults", "versions"
]);

export function resolveAetheusAnalyticsRoute(pathname) {
  if (pathname === "/") return "/";
  if (typeof pathname !== "string" || pathname.length > 256 || !pathname.startsWith("/")) {
    return undefined;
  }

  const segments = pathname.split("/").filter(Boolean);
  const topLevel = segments[0]?.toLowerCase();
  if (!topLevel || !topLevelSegments.has(topLevel)) return undefined;

  return `/${segments.map((segment, index) => {
    const normalized = segment.toLowerCase();
    if (index === 0 || staticNestedSegments.has(normalized)) return normalized;
    return "{value}";
  }).join("/")}`;
}

async function installAnalytics() {
  try {
    const response = await fetch(configurationEndpoint, {
      credentials: "same-origin",
      cache: "no-store",
      headers: { accept: "application/json" }
    });
    if (!response.ok || !response.headers.get("content-type")?.includes("application/json")) return;

    const configuration = await response.json();
    if (configuration.enabled !== true) return;

    const { createAetheusAnalytics } = await import(analyticsModule);
    createAetheusAnalytics({
      routeResolver: resolveAetheusAnalyticsRoute,
      captureErrors: true,
      capturePerformance: true
    }).install();
  } catch {
    // Monitoring must never affect application startup or navigation.
  }
}

void installAnalytics();
