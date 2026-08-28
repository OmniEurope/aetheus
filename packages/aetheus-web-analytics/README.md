# @aetheus/web-analytics

Storage-free page-view and SPA-route measurement for Aetheus. It creates no analytical cookie or
`localStorage` entry, sends no query string, fragment, DOM, form value, token, or persistent browser ID,
and honors Global Privacy Control and Do Not Track.

```js
import { createAetheusAnalytics } from "@aetheus/web-analytics";
createAetheusAnalytics({
  captureErrors: false,
  capturePerformance: false,
  routeResolver: (pathname) => pathname
}).install();
```

`captureErrors` and `capturePerformance` are independently opt-in. Error capture sends only the fixed
categories `script_error` or `unhandled_rejection`, never a message or stack. Performance capture sends
only a bounded navigation duration and normalized route. Applications may call
`trackPerformance(durationMs)` for an explicitly measured SPA transition.

If a route can contain a username, slug, document name, or other application identifier, `routeResolver`
must return the framework's route template instead of the concrete path, for example
`"/users/{id}"`. The callback receives only `pathname`, never the query string or fragment. Omitting
`routeResolver` falls back to the optional `routeTemplates` allow-list; emission is disabled only when
neither mechanism resolves the current pathname. The identity resolver shown above is appropriate only
when every non-numeric path segment is guaranteed to be public static navigation text.

The same source is packaged by the Razor Class Library at
`/_content/Aetheus.WebAnalytics/0.1.0/aetheus-web-analytics.js`. Pin the exact version and its SHA-384 SRI
value; do not use an unversioned mutable URL.
