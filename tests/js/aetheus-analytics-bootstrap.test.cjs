// SPDX-License-Identifier: EUPL-1.2
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const root = path.resolve(__dirname, '../..');
const bootstrapUrl = pathToFileURL(path.join(root, 'src/Aetheus.Front/wwwroot/js/aetheus-analytics-bootstrap.js')).href;
const packageUrl = pathToFileURL(path.join(root, 'packages/aetheus-web-analytics/src/aetheus-web-analytics.js')).href;

// Importing the bootstrap starts it: a configuration endpoint that answers 404 keeps it off.
globalThis.fetch = async () => ({ ok: false, headers: { get: () => null } });

function browser(pathname) {
    const payloads = [];
    const history = { pushState: () => undefined, replaceState: () => undefined };
    Object.defineProperty(globalThis, 'navigator', {
        configurable: true,
        value: {
            globalPrivacyControl: false,
            doNotTrack: '0',
            sendBeacon: (_endpoint, blob) => {
                payloads.push(blob);
                return true;
            }
        }
    });
    Object.defineProperty(globalThis, 'window', {
        configurable: true,
        value: {
            location: { pathname, search: '' },
            doNotTrack: '0',
            addEventListener: () => undefined,
            removeEventListener: () => undefined
        }
    });
    Object.defineProperty(globalThis, 'history', { configurable: true, value: history });
    return {
        navigate(nextPathname, search = '') {
            globalThis.window.location.pathname = nextPathname;
            globalThis.window.location.search = search;
            history.pushState({}, '', nextPathname + search);
        },
        events: () => Promise.all(payloads.map(payload => payload.text().then(JSON.parse)))
    };
}

test('R2-008: monitoring, notifications and the nested pages the list missed are measured', async () => {
    const { resolveAetheusAnalyticsRoute } = await import(bootstrapUrl);

    assert.equal(resolveAetheusAnalyticsRoute('/monitoring'), '/monitoring');
    assert.equal(resolveAetheusAnalyticsRoute('/notifications'), '/notifications');
    assert.equal(resolveAetheusAnalyticsRoute('/admin/performance'), '/admin/performance');
    assert.equal(resolveAetheusAnalyticsRoute('/admin/settings'), '/admin/settings');
    assert.equal(resolveAetheusAnalyticsRoute('/pipelines/setup'), '/pipelines/setup');
    assert.equal(resolveAetheusAnalyticsRoute('/servers/7/ports'), '/servers/{value}/ports');
    assert.equal(resolveAetheusAnalyticsRoute('/projects/3/ai-tasks'), '/projects/{value}/ai-tasks');
    assert.equal(resolveAetheusAnalyticsRoute('/login'), undefined);
    assert.equal(resolveAetheusAnalyticsRoute('/pipelines/42'), '/pipelines/{value}');
});

test('R2-008: installs at once, holds the page views until the identity, then sends them with it', async () => {
    const { startAetheusAnalytics } = await import(bootstrapUrl);
    const { createAetheusAnalytics } = await import(packageUrl);
    const page = browser('/projects/3/overview');
    globalThis.Aetheus = { analyticsVisitor: () => 'visitor-7', analyticsSignedIn: () => true };
    let sessionKnown;
    const waiting = new Promise(resolve => { sessionKnown = resolve; });

    const started = startAetheusAnalytics(createAetheusAnalytics, () => waiting);
    page.navigate('/projects/4/overview');
    page.navigate('/projects/4/overview', '?tab=apps');
    assert.equal((await page.events()).length, 0);

    sessionKnown();
    const analytics = await started;
    page.navigate('/monitoring');

    const events = await page.events();
    assert.deepEqual(events.map(event => event.route), [
        '/projects/{value}/overview', '/projects/{value}/overview', '/projects/{value}/overview', '/monitoring'
    ]);
    assert.equal(events.every(event => event.authenticatedUserId === 'visitor-7'), true);
    assert.equal(JSON.stringify(events).includes('tab='), false);

    // The application says the visitor changed: the next events carry the new identity.
    globalThis.Aetheus.analyticsVisitor = () => 'visitor-8';
    globalThis.Aetheus._onAnalyticsIdentity();
    page.navigate('/notifications');
    assert.equal((await page.events()).at(-1).authenticatedUserId, 'visitor-8');
    analytics.stop();
});
