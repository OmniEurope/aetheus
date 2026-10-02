const offlineCache = 'aetheus-offline';
const offlineAssets = ['/offline.html', '/css/offline.css', '/js/offline.js'];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(offlineCache)
        .then(cache => cache.addAll(offlineAssets))
        .then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.mode === 'navigate') {
        event.respondWith(fetch(request).catch(async () => {
            const cache = await caches.open(offlineCache);
            return await cache.match('/offline.html') || Response.error();
        }));
        return;
    }

    if (offlineAssets.includes(new URL(request.url).pathname)) {
        event.respondWith(fetch(request).catch(async () => {
            const cache = await caches.open(offlineCache);
            return await cache.match(request, { ignoreSearch: true }) || Response.error();
        }));
    }
});
