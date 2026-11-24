// Which addresses the hub may connect to on a provider's behalf: public unicast only. Everything a request could use
// to reach this server, its neighbours or the provider's private network is refused.
import net from 'node:net';

const V4_BLOCKED = [
  ['0.0.0.0', 8], // "this" network
  ['10.0.0.0', 8],
  ['100.64.0.0', 10], // CGNAT
  ['127.0.0.0', 8],
  ['169.254.0.0', 16], // link-local, cloud metadata
  ['172.16.0.0', 12],
  ['192.0.0.0', 24], // IETF protocol assignments
  ['192.0.2.0', 24], // TEST-NET-1
  ['192.31.196.0', 24], // AS112
  ['192.52.193.0', 24], // AMT
  ['192.88.99.0', 24], // 6to4 relay anycast
  ['192.168.0.0', 16],
  ['192.175.48.0', 24], // AS112
  ['198.18.0.0', 15], // benchmarking
  ['198.51.100.0', 24], // TEST-NET-2
  ['203.0.113.0', 24], // TEST-NET-3
  ['224.0.0.0', 4], // multicast
  ['240.0.0.0', 4], // reserved, broadcast
];

function v4ToInt(ip) {
  const p = ip.split('.').map(Number);
  return ((p[0] << 24) >>> 0) + (p[1] << 16) + (p[2] << 8) + p[3];
}

const V4_RANGES = V4_BLOCKED.map(([base, bits]) => {
  const mask = bits === 0 ? 0 : (0xffffffff << (32 - bits)) >>> 0;
  return [(v4ToInt(base) & mask) >>> 0, mask];
});

export function isPublicV4(ip) {
  if (net.isIPv4(ip) !== true) return false;
  const n = v4ToInt(ip);
  return !V4_RANGES.some(([base, mask]) => ((n & mask) >>> 0) === base);
}

/** The 8 groups of an IPv6 address as numbers; null when it does not parse. */
function v6Groups(ip) {
  if (!net.isIPv6(ip)) return null;
  let text = ip.toLowerCase();
  const zone = text.indexOf('%');
  if (zone >= 0) text = text.slice(0, zone);
  let tail = [];
  const lastColon = text.lastIndexOf(':');
  const last = text.slice(lastColon + 1);
  if (last.includes('.')) {
    const n = v4ToInt(last);
    tail = [n >>> 16, n & 0xffff];
    text = text.slice(0, lastColon) + ':0:0';
  }
  const [head, rest] = text.split('::');
  const a = head ? head.split(':') : [];
  const b = rest !== undefined && rest !== '' ? rest.split(':') : [];
  const fill = rest !== undefined ? 8 - a.length - b.length : 0;
  const groups = [...a, ...Array(fill).fill('0'), ...b].map(g => parseInt(g || '0', 16));
  if (tail.length) groups.splice(6, 2, ...tail);
  return groups.length === 8 ? groups : null;
}

export function isPublicV6(ip) {
  const g = v6Groups(ip);
  if (!g) return false;
  // IPv4-mapped (::ffff:a.b.c.d) and IPv4-compatible forms: judged as the IPv4 address they carry.
  if (g.slice(0, 5).every(x => x === 0) && (g[5] === 0xffff || g[5] === 0)) {
    if (g[5] === 0 && g[6] === 0 && g[7] <= 1) return false; // :: and ::1
    return isPublicV4(`${g[6] >> 8}.${g[6] & 255}.${g[7] >> 8}.${g[7] & 255}`);
  }
  // NAT64 64:ff9b::/96 can reach any IPv4 address, private ones included.
  if (g[0] === 0x64 && g[1] === 0xff9b) {
    if (g.slice(2, 6).every(x => x === 0)) return isPublicV4(`${g[6] >> 8}.${g[6] & 255}.${g[7] >> 8}.${g[7] & 255}`);
    return false; // 64:ff9b:1::/48, local-use NAT64
  }
  // Only the global unicast block 2000::/3 is public at all.
  if ((g[0] & 0xe000) !== 0x2000) return false;
  if (g[0] === 0x2001 && g[1] < 0x200) return false; // 2001::/23 IETF protocol assignments (Teredo, ORCHID, ...)
  if (g[0] === 0x2001 && g[1] === 0x0db8) return false; // documentation
  if (g[0] === 0x2002) return false; // 6to4: carries an IPv4 address of any kind
  if (g[0] === 0x3fff && g[1] < 0x1000) return false; // 3fff::/20 documentation
  return true;
}

/**
 * What a visitor's limits are counted by: an IPv4 address as it is (an IPv4-mapped IPv6 one too), an IPv6 address by
 * its /64. One home or one server gets a whole /64, so counting single IPv6 addresses would let it walk round any limit.
 */
export function ipKey(ip) {
  const text = String(ip ?? '');
  if (net.isIPv4(text)) return text;
  const g = v6Groups(text);
  if (!g) return text;
  if (g.slice(0, 5).every(x => x === 0) && g[5] === 0xffff) return `${g[6] >> 8}.${g[6] & 255}.${g[7] >> 8}.${g[7] & 255}`;
  return `${g.slice(0, 4).map(x => x.toString(16)).join(':')}::/64`;
}

/**
 * The wider network of an IPv6 address, its /48 (a site's usual share: 65 536 /64s), or null for IPv4 (an IPv4-mapped
 * address included). A limit by /64 alone would let one site walk round it from /64 to /64.
 */
export function wideIpKey(ip) {
  const text = String(ip ?? '');
  if (net.isIPv4(text)) return null;
  const g = v6Groups(text);
  if (!g || (g.slice(0, 5).every(x => x === 0) && g[5] === 0xffff)) return null;
  return `${g.slice(0, 3).map(x => x.toString(16)).join(':')}::/48`;
}

/** True for a public unicast IPv4 or IPv6 address. */
export function isPublicIp(ip) {
  if (net.isIPv4(ip)) return isPublicV4(ip);
  if (net.isIPv6(ip)) return isPublicV6(ip);
  return false;
}
