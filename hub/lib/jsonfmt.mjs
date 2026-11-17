// JSON as V2pnPartnerSigning.Json writes it: System.Text.Json's Utf8JsonWriter, indented by two spaces, with
// JavaScriptEncoder.UnsafeRelaxedJsonEscaping and a newline at the end. The index is signed over these exact bytes and
// partner.json is pinned by hash, so the layout has to be the one V2pn's own tools produce.
//
// .NET writes Environment.NewLine between lines: "\r\n" on Windows (where the owner runs tools/partner.cs), "\n" on
// Linux. The hub runs on Linux and writes "\n"; tests pass "\r\n" to compare with files the owner's PC wrote.

// Characters the relaxed encoder still escapes: controls, the line and paragraph separators, the BOM, private use,
// noncharacters, everything outside the BMP (as a surrogate pair) and code points Unicode does not assign.
const ESCAPED = /[\u0000-\u001f\u007f-\u009f\u2028\u2029\ufeff\ue000-\uf8ff\ufdd0-\ufdef\ufff9-\ufffb\ufffe\uffff"\\]|[\u{10000}-\u{10ffff}]|[\ud800-\udfff]|\p{Cn}/gu;
const SHORT = { '"': '\\"', '\\': '\\\\', '\b': '\\b', '\f': '\\f', '\n': '\\n', '\r': '\\r', '\t': '\\t' };

function quote(text) {
  return '"' + text.replace(ESCAPED, ch => {
    if (SHORT[ch]) return SHORT[ch];
    let out = '';
    for (let i = 0; i < ch.length; i++) out += '\\u' + ch.charCodeAt(i).toString(16).toUpperCase().padStart(4, '0');
    return out;
  }) + '"';
}

function write(value, indent, nl, out) {
  if (value === null) { out.push('null'); return; }
  switch (typeof value) {
    case 'string': out.push(quote(value)); return;
    case 'boolean': out.push(value ? 'true' : 'false'); return;
    case 'number':
      if (!Number.isSafeInteger(value)) throw new Error('only integers are written');
      out.push(String(value));
      return;
    case 'object': break;
    default: throw new Error(`cannot write ${typeof value}`);
  }
  const inner = indent + '  ';
  if (Array.isArray(value)) {
    if (value.length === 0) { out.push('[]'); return; }
    out.push('[');
    value.forEach((item, i) => {
      out.push(nl, inner);
      write(item, inner, nl, out);
      if (i + 1 < value.length) out.push(',');
    });
    out.push(nl, indent, ']');
    return;
  }
  const keys = Object.keys(value).filter(k => value[k] !== undefined);
  if (keys.length === 0) { out.push('{}'); return; }
  out.push('{');
  keys.forEach((key, i) => {
    out.push(nl, inner, quote(key), ': ');
    write(value[key], inner, nl, out);
    if (i + 1 < keys.length) out.push(',');
  });
  out.push(nl, indent, '}');
}

/** The bytes of <value> as the partner tools write a JSON document; keys in insertion order, undefined left out. */
export function dotnetJson(value, newline = '\n') {
  const out = [];
  write(value, '', newline, out);
  out.push('\n');
  return Buffer.from(out.join(''), 'utf8');
}
