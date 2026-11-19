// Wiring: configuration, database, key, the HTTP server, the housekeeping timers and a graceful stop.
import http from 'node:http';
import { createHandler } from './api.mjs';
import { Db } from './db.mjs';
import { loadKey, ensureKey } from './keys.mjs';
import { log } from './log.mjs';
import { Hub } from './service.mjs';

export function createHub(config, { net } = {}) {
  const db = new Db(config.dbFile);
  ensureKey(config.keyFile);
  const key = loadKey(config.keyFile);
  return new Hub({ config, db, key, net });
}

export function startServer(hub) {
  const server = http.createServer(createHandler(hub));
  server.headersTimeout = 10_000;
  server.requestTimeout = 30_000;
  server.keepAliveTimeout = 5_000;
  server.maxHeadersCount = 64;

  const flush = setInterval(() => hub.counter.flush(), 60_000);
  const maintain = setInterval(() => hub.maintain(), 10 * 60_000);
  flush.unref();
  maintain.unref();
  setImmediate(() => hub.maintain());

  let stopping = false;
  const stop = signal => {
    if (stopping) return;
    stopping = true;
    log.info('stopping', { signal });
    clearInterval(flush);
    clearInterval(maintain);
    server.close(() => {
      try {
        hub.counter.flush();
        hub.db.close();
      } catch (e) {
        log.error('close failed', { error: String(e.message || e) });
      }
      log.info('stopped');
      process.exit(0);
    });
    server.closeIdleConnections();
    // Requests still running after ten seconds (a check waits eight at most) are cut.
    setTimeout(() => server.closeAllConnections(), 10_000).unref();
  };
  process.on('SIGTERM', () => stop('SIGTERM'));
  process.on('SIGINT', () => stop('SIGINT'));

  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(hub.config.port, hub.config.host, () => {
      log.info('listening', { host: hub.config.host, port: hub.config.port });
      resolve(server);
    });
  });
}
