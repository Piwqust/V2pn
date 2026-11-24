// Wiring: configuration, database, key, the HTTP server, the housekeeping timers and a graceful stop.
import http from 'node:http';
import { createHandler } from './api.mjs';
import { Db } from './db.mjs';
import { loadKey, ensureKey } from './keys.mjs';
import { log } from './log.mjs';
import { ROOT_REFRESH_MS, ROOT_RETRY_MS } from './roots.mjs';
import { Hub } from './service.mjs';

export function createHub(config, { net } = {}) {
  const db = new Db(config.dbFile);
  ensureKey(config.keyFile);
  const key = loadKey(config.keyFile);
  return new Hub({ config, db, key, net });
}

/**
 * Runs <fn> every <ms> without holding the process open. An error is logged under <what> and the timer goes on: one
 * thrown out of a timer would end the hub (the database busy for a moment, a full disk).
 */
export function every(ms, what, fn) {
  const timer = setInterval(() => {
    try {
      fn();
    } catch (e) {
      log.error(`${what} failed`, { error: String(e?.message || e).slice(0, 500), code: e?.code });
    }
  }, ms);
  timer.unref();
  return timer;
}

export function startServer(hub) {
  const server = http.createServer(createHandler(hub));
  server.headersTimeout = 10_000;
  server.requestTimeout = 30_000;
  server.keepAliveTimeout = 5_000;
  server.maxHeadersCount = 64;

  const flush = every(60_000, 'download counters', () => hub.counter.flush());
  const maintain = every(10 * 60_000, 'maintenance', () => hub.maintain());
  setImmediate(() => hub.maintain());
  // The root-signed partner index: now, then every six hours (half an hour after a failure).
  let roots = null;
  const refreshRoots = async () => {
    let ok = false;
    try {
      ok = await hub.refreshRoots();
    } catch (e) {
      log.error('root partner index failed', { error: String(e.message || e) });
    }
    roots = setTimeout(refreshRoots, ok ? ROOT_REFRESH_MS : ROOT_RETRY_MS);
    roots.unref();
  };
  setImmediate(refreshRoots);

  let stopping = false;
  const stop = signal => {
    if (stopping) return;
    stopping = true;
    log.info('stopping', { signal });
    clearInterval(flush);
    clearInterval(maintain);
    clearTimeout(roots);
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
