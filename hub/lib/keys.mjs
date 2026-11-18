// The hub's partner key (ECDSA P-256; it signs partner-index.json as V2pnPartnerSigning.Sign does: DER, base64) and
// the secret the hub keys its code hashes with. Both live in HUB_DATA/keys, readable by the service only.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

function writeSecretFile(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
  // "wx": never overwrite a key that is already there.
  fs.writeFileSync(file, text, { mode: 0o600, flag: 'wx' });
}

/** Creates the key when there is none; returns {created, publicKey}. The private half is never printed. */
export function ensureKey(file) {
  let created = false;
  if (!fs.existsSync(file)) {
    const { privateKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
    writeSecretFile(file, privateKey.export({ type: 'pkcs8', format: 'pem' }));
    created = true;
  }
  return { created, publicKey: publicKeyOf(loadKey(file)) };
}

export function loadKey(file) {
  const key = crypto.createPrivateKey(fs.readFileSync(file));
  if (key.asymmetricKeyType !== 'ec' || key.asymmetricKeyDetails?.namedCurve !== 'prime256v1') {
    throw new Error(`${file} is not an ECDSA P-256 private key`);
  }
  return key;
}

/** The public half as base64 SubjectPublicKeyInfo: what V2pnPartnerKeys holds. */
export function publicKeyOf(key) {
  return crypto.createPublicKey(key).export({ type: 'spki', format: 'der' }).toString('base64');
}

/** The .sig file of <data>: DER ECDSA P-256 / SHA-256 in base64 and a newline. */
export function signature(key, data) {
  return crypto.sign('sha256', data, { key, dsaEncoding: 'der' }).toString('base64') + '\n';
}

export function verify(publicKeyBase64, data, sig) {
  const key = crypto.createPublicKey({ key: Buffer.from(publicKeyBase64, 'base64'), format: 'der', type: 'spki' });
  return crypto.verify('sha256', data, { key, dsaEncoding: 'der' }, Buffer.from(sig.trim(), 'base64'));
}

/** 32 random bytes kept in <file>, made on first use. */
export function loadSecret(file) {
  if (!fs.existsSync(file)) {
    try {
      writeSecretFile(file, crypto.randomBytes(32).toString('hex') + '\n');
    } catch (e) {
      if (e.code !== 'EEXIST') throw e;
    }
  }
  const secret = Buffer.from(fs.readFileSync(file, 'utf8').trim(), 'hex');
  if (secret.length < 32) throw new Error(`${file} does not hold a 32-byte secret`);
  return secret;
}
