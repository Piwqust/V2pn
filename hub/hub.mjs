#!/usr/bin/env node
// V2pn Hub: the server side of V2pn's partners (hub/README.md).
//
//   node hub.mjs [serve]                     the service on HUB_HOST:HUB_PORT (127.0.0.1:18190)
//   node hub.mjs owner <login>               makes the account (or promotes it) the owner; prints a new password once
//   node hub.mjs reset-password <login>      a new password for any account; prints it once
//   node hub.mjs import-partner <folder> [--login <login>]
//                                            partners/<id> as a live partner with its granted rights approved
//   node hub.mjs publish                     rebuilds and re-signs the index, the zips and the pages
//   node hub.mjs keygen                      creates the hub's partner key if missing; prints its public half
import { normalizeLogin } from './lib/auth.mjs';
import { loadConfig } from './lib/config.mjs';
import { ensureKey } from './lib/keys.mjs';
import { log, setQuiet } from './lib/log.mjs';
import { createHub, startServer } from './lib/server.mjs';

const [command = 'serve', ...rest] = process.argv.slice(2);
const option = name => {
  const i = rest.indexOf(name);
  return i >= 0 ? rest[i + 1] : undefined;
};

function usage() {
  process.stderr.write('usage: node hub.mjs [serve | owner <login> | reset-password <login> | import-partner <folder> [--login <login>] | publish | keygen]\n');
  return 2;
}

/** Runs <fn> with a hub that is closed afterwards. */
function withHub(config, fn, { quiet = false } = {}) {
  if (quiet) setQuiet(true);
  const hub = createHub(config);
  try {
    return fn(hub);
  } finally {
    hub.db.close();
  }
}

async function main() {
  const config = loadConfig();
  switch (command) {
    case 'serve': {
      await startServer(createHub(config));
      return null;
    }
    case 'keygen': {
      const { created, publicKey } = ensureKey(config.keyFile);
      process.stdout.write(`${created ? 'created' : 'exists'} ${config.keyFile}\n${publicKey}\n`);
      return 0;
    }
    case 'owner': {
      if (!rest[0]) return usage();
      return withHub(config, hub => {
        const password = hub.auth.makeOwner(rest[0]);
        process.stdout.write(`owner ${normalizeLogin(rest[0])}\npassword ${password}\n`);
        return 0;
      }, { quiet: true });
    }
    case 'reset-password': {
      if (!rest[0]) return usage();
      return withHub(config, hub => {
        const account = hub.auth.accountByLogin(normalizeLogin(rest[0]));
        if (!account) throw new Error(`there is no account ${rest[0]}`);
        const password = hub.auth.resetPassword(account.id);
        hub.db.audit('cli', 'account.password', account.login);
        process.stdout.write(`${account.login} (${account.role})\npassword ${password}\n`);
        return 0;
      }, { quiet: true });
    }
    case 'import-partner': {
      if (!rest[0]) return usage();
      if (rest.includes('--login') && !option('--login')) return usage();
      return withHub(config, hub => {
        const result = hub.importPartner(rest[0], { login: option('--login') });
        process.stdout.write(`imported ${result.id} for ${result.login}: grants ${result.grants.join(', ')}\n`);
        for (const n of result.notes) process.stdout.write(`  note: ${n}\n`);
        const published = hub.publish('cli');
        process.stdout.write(`published ${published.partners.length} partner(s) into ${config.publicDir}\n`);
        return 0;
      });
    }
    case 'publish': {
      return withHub(config, hub => {
        const published = hub.publish('cli');
        process.stdout.write(`published ${published.partners.length} partner(s) into ${config.publicDir}: ${published.partners.join(', ') || 'none'}`
          + `${published.down.length ? `; taken down: ${published.down.join(', ')}` : ''}\n`);
        return 0;
      });
    }
    default:
      return usage();
  }
}

main().then(code => {
  if (code !== null) process.exitCode = code;
}, e => {
  setQuiet(false);
  const detail = e?.detail ? ` ${JSON.stringify(e.detail)}` : '';
  log.error('failed', { command, error: `${e?.message || e}${detail}` });
  process.exitCode = 1;
});
