// Reports of failed updates from V2pn, sent only when the person turned them on in the app (off by default):
// POST /api/report/update with {from, to, os, arch, stage, code}. Checked strictly; only counts by day, the version
// updated to, os, arch, stage and code are kept (downloads.mjs). No address, no User-Agent, no id of the copy.
import { HubError } from './errors.mjs';

export const REPORT_FIELDS = ['from', 'to', 'os', 'arch', 'stage', 'code'];
export const REPORT_OS = ['windows', 'macos'];
export const REPORT_ARCH = ['x64', 'arm64'];
// Where the update stopped: fetching the package, checking its signature, unpacking it beside the app, putting it in
// place, starting the new version, going back to the old one.
export const REPORT_STAGES = ['download', 'verify', 'stage', 'swap', 'start', 'rollback'];
export const MAX_REPORT_BYTES = 1024;
// 1.4.2, 1.5.0-beta.2, 1.4.1-local.202611141320: at most 32 characters.
const VERSION = /^\d{1,4}\.\d{1,4}\.\d{1,6}(?:-[0-9A-Za-z.-]{1,25})?$/;
const CODE = /^[a-z0-9_-]{1,40}$/;

const version = v => typeof v === 'string' && v.length <= 32 && VERSION.test(v);

/** The report in <body> (parsed JSON) as {from, to, os, arch, stage, code}; throws "invalid" naming the fields otherwise. */
export function parseUpdateReport(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new HubError('bad_request');
  const fields = {};
  for (const key of Object.keys(body)) if (!REPORT_FIELDS.includes(key)) fields[key] = 'Такого поля нет.';
  if (!version(body.from)) fields.from = 'Версия вида 1.4.2, до 32 символов.';
  if (!version(body.to)) fields.to = 'Версия вида 1.4.3, до 32 символов.';
  if (!REPORT_OS.includes(body.os)) fields.os = `Одно из: ${REPORT_OS.join(', ')}.`;
  if (!REPORT_ARCH.includes(body.arch)) fields.arch = `Одно из: ${REPORT_ARCH.join(', ')}.`;
  if (!REPORT_STAGES.includes(body.stage)) fields.stage = `Одно из: ${REPORT_STAGES.join(', ')}.`;
  if (typeof body.code !== 'string' || !CODE.test(body.code)) fields.code = 'От 1 до 40 знаков: a-z, 0-9, _ и -.';
  if (Object.keys(fields).length) throw new HubError('invalid', { fields });
  return { from: body.from, to: body.to, os: body.os, arch: body.arch, stage: body.stage, code: body.code };
}
