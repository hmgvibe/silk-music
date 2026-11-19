'use strict';

// Entry point on Android, where the bridge runs on nodejs-mobile (Node 18) inside the app process.
// It fills in the few Node 20 globals the package uses, then starts the same server as the desktop app.

if (!globalThis.crypto) {
  globalThis.crypto = require('node:crypto').webcrypto;
}

if (typeof AbortSignal.any !== 'function') {
  AbortSignal.any = function any(signals) {
    const controller = new AbortController();
    for (const signal of signals) {
      if (signal.aborted) {
        controller.abort(signal.reason);
        break;
      }
      signal.addEventListener('abort', () => controller.abort(signal.reason), { once: true });
    }
    return controller.signal;
  };
}

process.env.SILK_MOBILE = '1';
require('./server.cjs');
