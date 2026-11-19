'use strict';

// Hosts qq-music-api inside a private child process of the desktop app.
// The package binds every interface and allows any CORS origin by default, so the HTTP layer
// is constrained here before the package loads: loopback only, random port, per-launch key,
// and no browser-originated requests.

const crypto = require('node:crypto');
const http = require('node:http');

const bridgeKey = Buffer.from(process.env.SILK_BRIDGE_KEY || '', 'utf8');
if (bridgeKey.length < 32) {
  process.stderr.write('SILK_BRIDGE_KEY is missing\n');
  process.exit(2);
}
delete process.env.SILK_BRIDGE_KEY;

process.env.QQ_DISABLE_UPDATE_CHECK = '1';
process.env.AUTO_OPEN_EXPLORER = 'false';
process.env.LOG_LEVEL = process.env.LOG_LEVEL || 'error';

const isAuthorized = (req) => {
  if (req.headers.origin || req.headers['sec-fetch-site']) return false;
  const supplied = Buffer.from(String(req.headers['x-silk-key'] || ''), 'utf8');
  return supplied.length === bridgeKey.length && crypto.timingSafeEqual(supplied, bridgeKey);
};

const createServer = http.createServer;
http.createServer = function guardedCreateServer(...args) {
  const handler = args.pop();
  return createServer.call(http, ...args, (req, res) => {
    if (!isAuthorized(req)) {
      res.writeHead(401, { 'Content-Type': 'application/json' });
      res.end('{"code":401}');
      return;
    }
    delete req.headers['x-silk-key'];
    handler(req, res);
  });
};

const listen = http.Server.prototype.listen;
http.Server.prototype.listen = function loopbackListen(...args) {
  const callback = args.find((value) => typeof value === 'function');
  return listen.call(this, 0, '127.0.0.1', callback);
};

const { server } = require('@yakult-green-tea/qq-music-api');

server.once('listening', () => {
  process.stdout.write(`SILK_READY ${server.address().port}\n`);
});
server.once('error', (error) => {
  process.stderr.write(`bridge failed: ${error.code || 'unknown'}\n`);
  process.exit(1);
});

// The desktop app holds stdin open; when it exits for any reason the bridge goes with it.
process.stdin.resume();
process.stdin.once('end', () => process.exit(0));
process.stdin.once('close', () => process.exit(0));
