// Special rights: the parts of a partner that change how V2pn works for its people, so a moderator approves them
// first. The provider's request keeps its content; an approval is bound to the content's hash, so a changed request
// waits again while the published pack keeps what was approved.
import crypto from 'node:crypto';
import { HubError } from './errors.mjs';
import { actionProblem, parseActions } from './look.mjs';
import { parseRouting, routingBytes, routingToText } from './routing.mjs';
import { domainAllowed, httpsUrl, oneLine } from './validate.mjs';

// In the order of the index's grants.
export const RIGHTS = ['account', 'actions', 'lock', 'routing'];

export const RIGHT_NAMES = {
  account: 'API: данные аккаунта и вход по коду',
  actions: 'свои кнопки',
  lock: 'скрытые адреса серверов',
  routing: 'свои правила маршрутизации',
};

const sha256 = buf => crypto.createHash('sha256').update(buf).digest('hex');

/**
 * The rights a request asks for: {name: content} with only those asked. <input> is the body's "rights":
 * {account?: {api, login}, actions?: [...], lock?: true, routing?: {...} | "lines"}; null, false or a missing key
 * means not asked. <domains> are the partner's: the API and links must be on them.
 */
export function parseRights(input, { domains }) {
  if (input == null) return {};
  if (typeof input !== 'object' || Array.isArray(input)) throw new HubError('invalid', { fields: { rights: 'Особые права: объект.' } });
  const out = {};
  const fields = {};
  const asked = key => input[key] != null && input[key] !== false && input[key] !== '';
  if (asked('account')) {
    const a = input.account;
    const api = typeof a === 'object' && !Array.isArray(a) ? oneLine(a.api ?? '', 512) : null;
    if (!api || !httpsUrl(api)) fields['rights.account.api'] = 'Адрес API должен начинаться с https://.';
    else if (!domainAllowed(domains, api)) fields['rights.account.api'] = `Адрес API должен быть на домене подписки (${domains.join(', ')}).`;
    else if (a.login != null && typeof a.login !== 'boolean') fields['rights.account.login'] = 'Да или нет.';
    else out.account = { api, login: a.login === true };
  }
  if (asked('actions')) {
    try {
      const actions = parseActions(input.actions, 'rights.actions');
      actions.forEach((a, i) => {
        const why = actionProblem(a, out.account?.api ?? '', domains);
        if (why) fields[`rights.actions.${i}`] = why;
      });
      out.actions = actions;
    } catch (e) {
      if (!(e instanceof HubError)) throw e;
      Object.assign(fields, e.fields);
    }
  }
  if (asked('lock')) {
    if (input.lock !== true) fields['rights.lock'] = 'Да или нет.';
    else out.lock = true;
  }
  if (asked('routing')) {
    try {
      out.routing = parseRouting(input.routing);
    } catch (e) {
      if (!(e instanceof HubError)) throw e;
      Object.assign(fields, e.fields);
    }
  }
  if (Object.keys(fields).length) throw new HubError('invalid', { fields });
  return out;
}

/** The hash an approval is bound to; for routing it is routing.json's SHA-256, the one the index names. */
export function contentHash(name, content) {
  return sha256(name === 'routing' ? routingBytes(content) : Buffer.from(JSON.stringify(content), 'utf8'));
}

/** pending | approved | denied, from a row of the rights table. */
export function rightState(row) {
  if (row.approved_hash && row.approved_hash === row.hash) return 'approved';
  if (row.denied_hash && row.denied_hash === row.hash) return 'denied';
  return 'pending';
}

/** A right as the provider and the moderators see it. */
export function rightView(row) {
  const content = JSON.parse(row.content);
  const view = {
    state: rightState(row),
    content,
    hash: row.hash,
    // Something approved earlier is still in the pack while a changed request waits.
    live: Boolean(row.approved_hash),
    reason: rightState(row) === 'denied' ? row.reason : null,
    requestedAt: row.requested_at,
    decidedAt: row.decided_at,
  };
  if (row.name === 'routing') view.text = routingToText(content);
  if (row.approved_hash && row.approved_hash !== row.hash) {
    view.approved = JSON.parse(row.approved);
    if (row.name === 'routing') view.approvedText = routingToText(view.approved);
  }
  return view;
}
