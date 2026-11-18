#!/usr/bin/env node
// V2pn Hub: the server side of V2pn's providers (hub/README.md).
//
//   node hub.mjs [serve]                 the service on HUB_HOST:HUB_PORT (127.0.0.1:18190)
//   node hub.mjs admin-link <email>      a one-time sign-in link for an owner's email (HUB_ADMINS), valid 15 minutes
//   node hub.mjs admin-password <email>  a new random password for an owner's email (sign-in at /admin/ without mail)
//   node hub.mjs import-partner <folder> [--tier 1|2] [--email <email>]
//                                        partners/<id> as an approved provider with its look published
//   node hub.mjs publish                 rebuilds and re-signs the index, the zips and the pages
//   node hub.mjs keygen                  creates the hub's partner key if missing; prints its public half
import { loadConfig } from './lib/config.mjs';
import { ensureKey } from './lib/keys.mjs';
import { log, setQuiet } from './lib/log.mjs';
import { createHub, startServer } from './lib/server.mjs';
import { normalizeEmail } from './lib/validate.mjs';

const [command = 'serve', ...rest] = process.argv.slice(2);
const option = name => {
  const i = rest.indexOf(name);
  return i >= 0 ? rest[i + 1] : undefined;
};

function usage() {
  process.stderr.write('usage: node hub.mjs [serve | admin-link <email> | admin-password <email> | import-partner <folder> [--tier 1|2] [--email <email>] | publish | keygen]\n');
  return 2;
}

async function main() {
  const config = loadConfig();
  switch (command) {
    case 'serve': {
      const hub = createHub(config);
      await startServer(hub);
      return null;
    }
    case 'keygen': {
      const { created, publicKey } = ensureKey(config.keyFile);
      process.stdout.write(`${created ? 'created' : 'exists'} ${config.keyFile}\n${publicKey}\n`);
      return 0;
    }
    case 'admin-link': {
      if (!rest[0]) return usage();
      setQuiet(true);
      const hub = createHub(config);
      try {
        process.stdout.write(hub.auth.createAdminLink(rest[0]) + '\n');
      } finally {
        hub.db.close();
      }
      return 0;
    }
    case 'admin-password': {
      if (!rest[0]) return usage();
      setQuiet(true);
      const hub = createHub(config);
      try {
        process.stdout.write(hub.auth.setAdminPassword(rest[0]) + '\n');
      } finally {
        hub.db.close();
      }
      return 0;
    }
    case 'import-partner': {
      if (!rest[0]) return usage();
      const tier = option('--tier') ? Number(option('--tier')) : undefined;
      if (tier !== undefined && tier !== 1 && tier !== 2) return usage();
      const email = option('--email') ? normalizeEmail(option('--email')) : undefined;
      if (option('--email') && !email) return usage();
      const hub = createHub(config);
      try {
        const result = hub.importPartner(rest[0], { tier, email });
        process.stdout.write(`imported ${result.id}: tier ${result.tier}, grants ${result.grants.join(', ')}\n`);
        for (const n of result.notes) process.stdout.write(`  note: ${n}\n`);
        const published = hub.publish('cli');
        process.stdout.write(`published ${published.partners.length} partner(s) into ${config.publicDir}\n`);
      } finally {
        hub.db.close();
      }
      return 0;
    }
    case 'publish': {
      const hub = createHub(config);
      try {
        const published = hub.publish('cli');
        process.stdout.write(`published ${published.partners.length} partner(s) into ${config.publicDir}: ${published.partners.join(', ') || 'none'}\n`);
      } finally {
        hub.db.close();
      }
      return 0;
    }
    default:
      return usage();
  }
}

main().then(code => {
  if (code !== null) process.exitCode = code;
}, e => {
  const detail = e?.detail ? ` ${JSON.stringify(e.detail)}` : '';
  log.error('failed', { command, error: `${e?.message || e}${detail}` });
  process.exitCode = 1;
});
