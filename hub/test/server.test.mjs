// The hub's timers: an error in one is logged and the hub goes on (lib/server.mjs every()).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { setQuiet } from '../lib/log.mjs';
import { every } from '../lib/server.mjs';
import { startHub } from './helpers.mjs';

test('the download counters failing to reach the database (busy, disk full) are logged, and the timer goes on', async () => {
  const env = await startHub();
  const lines = [];
  const write = process.stderr.write.bind(process.stderr);
  process.stderr.write = chunk => { lines.push(String(chunk)); return true; };
  setQuiet(false);
  let calls = 0;
  let timer;
  try {
    const { counter } = env.hub;
    counter.count('windows-x64');
    counter.db = { tx: () => { calls++; throw Object.assign(new Error('database is locked'), { code: 'ERR_SQLITE_ERROR' }); } };
    timer = every(5, 'download counters', () => counter.flush());
    for (let i = 0; i < 100 && calls < 2; i++) {
      counter.count('windows-x64');
      await new Promise(r => setTimeout(r, 10));
    }
  } finally {
    clearInterval(timer);
    process.stderr.write = write;
    setQuiet(true);
    await env.close();
  }
  assert.ok(calls >= 2, 'it ran again after the first failure');
  const errors = lines.filter(l => l.startsWith('{')).map(l => JSON.parse(l));
  assert.ok(errors.length >= 2);
  assert.deepEqual([errors[0].level, errors[0].msg, errors[0].error, errors[0].code], ['error', 'download counters failed', 'database is locked', 'ERR_SQLITE_ERROR']);
});
