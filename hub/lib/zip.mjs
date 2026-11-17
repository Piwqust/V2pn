// partner-<id>.zip laid out as V2pnPartnerPublisher.Zip writes it with .NET's ZipArchive on the owner's Windows PC:
// entries by name (ordinal), deflated, one fixed date (2020-01-01 00:00) so the same files always give the same bytes,
// no extra fields, no data descriptors, version 2.0, FAT attributes. The deflate stream itself comes from Node's zlib,
// not .NET's zlib-ng: pictures (stored blocks) come out the same, text may not, so the zip's hash can differ from the
// one tools/partner.cs computes for the same files. V2pn only ever compares a zip with the index that names it.
import zlib from 'node:zlib';

const DOS_DATE = ((2020 - 1980) << 9) | (1 << 5) | 1;
const DOS_TIME = 0;

let crcTable = null;
export function crc32(buf) {
  if (zlib.crc32) return zlib.crc32(buf) >>> 0;
  if (!crcTable) {
    crcTable = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crcTable[n] = c >>> 0;
    }
  }
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = crcTable[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

const byName = (a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0);

/** A flat zip of [{name, content}] (ASCII names), sorted by name. */
export function makeZip(files) {
  const sorted = [...files].sort(byName);
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const { name, content } of sorted) {
    const nameBytes = Buffer.from(name, 'ascii');
    // memLevel 9 is what .NET's zlib-ng uses; with it incompressible pictures come out as the same stored blocks.
    const data = zlib.deflateRawSync(content, { level: 6, memLevel: 9 });
    const crc = crc32(content);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0, 6);
    local.writeUInt16LE(8, 8);
    local.writeUInt16LE(DOS_TIME, 10);
    local.writeUInt16LE(DOS_DATE, 12);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(content.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    local.writeUInt16LE(0, 28);
    locals.push(local, nameBytes, data);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0, 8);
    central.writeUInt16LE(8, 10);
    central.writeUInt16LE(DOS_TIME, 12);
    central.writeUInt16LE(DOS_DATE, 14);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(content.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    centrals.push(central, nameBytes);
    offset += 30 + nameBytes.length + data.length;
  }
  const cd = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(sorted.length, 8);
  end.writeUInt16LE(sorted.length, 10);
  end.writeUInt32LE(cd.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, cd, end]);
}

/**
 * The entries of a zip by name, through its central directory, each read to at most <limit> bytes. For the hub's
 * own zips and the tests; it refuses anything a pack zip never has (zip64, encryption, odd methods).
 */
export function readZip(zip, limit = 2 * 1024 * 1024) {
  const eocd = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  if (eocd < 0) throw new Error('not a zip');
  const count = zip.readUInt16LE(eocd + 10);
  let at = zip.readUInt32LE(eocd + 16);
  const entries = new Map();
  for (let i = 0; i < count; i++) {
    if (zip.readUInt32LE(at) !== 0x02014b50) throw new Error('bad central directory');
    const flags = zip.readUInt16LE(at + 8);
    const method = zip.readUInt16LE(at + 10);
    const csize = zip.readUInt32LE(at + 20);
    const usize = zip.readUInt32LE(at + 24);
    const nlen = zip.readUInt16LE(at + 28);
    const xlen = zip.readUInt16LE(at + 30);
    const clen = zip.readUInt16LE(at + 32);
    const local = zip.readUInt32LE(at + 42);
    const name = zip.subarray(at + 46, at + 46 + nlen).toString('utf8');
    if (flags & 1) throw new Error('encrypted entry');
    if (usize > limit) throw new Error(`${name} is too large`);
    const lnlen = zip.readUInt16LE(local + 26);
    const lxlen = zip.readUInt16LE(local + 28);
    const start = local + 30 + lnlen + lxlen;
    const raw = zip.subarray(start, start + csize);
    let content;
    if (method === 0) content = Buffer.from(raw);
    else if (method === 8) content = zlib.inflateRawSync(raw, { maxOutputLength: limit });
    else throw new Error(`unsupported method ${method}`);
    if (content.length !== usize || crc32(content) !== zip.readUInt32LE(at + 16)) throw new Error(`${name} is damaged`);
    entries.set(name, content);
    at += 46 + nlen + xlen + clen;
  }
  return entries;
}
