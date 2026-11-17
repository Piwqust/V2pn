// One JSON object per line on stdout (journald keeps them). Callers pass only what may be kept: never codes, tokens,
// cookies, subscription links or bodies.

let quiet = false;

export function setQuiet(value) {
  quiet = value;
}

function write(level, msg, fields) {
  if (quiet) return;
  const line = JSON.stringify({ t: new Date().toISOString(), level, msg, ...fields });
  if (level === 'error') process.stderr.write(line + '\n');
  else process.stdout.write(line + '\n');
}

export const log = {
  info: (msg, fields = {}) => write('info', msg, fields),
  warn: (msg, fields = {}) => write('warn', msg, fields),
  error: (msg, fields = {}) => write('error', msg, fields),
};
