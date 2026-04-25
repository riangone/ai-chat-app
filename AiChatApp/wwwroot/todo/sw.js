const CACHE_NAME = 'todo-app-v1';
const STATIC_ASSETS = [
  '/todo/',
  '/todo/index.html',
  '/todo/manifest.json',
  'https://cdn.jsdelivr.net/npm/daisyui@4/dist/full.min.css',
  'https://cdn.tailwindcss.com',
  'https://unpkg.com/htmx.org@1.9.12'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS).catch(() => {}))
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys =>
      Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
    )
  );
  self.clients.claim();
});

self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);

  // API calls: network-first, no cache
  if (url.pathname.startsWith('/api/')) {
    event.respondWith(fetch(event.request));
    return;
  }

  // Static assets: cache-first
  event.respondWith(
    caches.match(event.request).then(cached => cached || fetch(event.request))
  );
});
