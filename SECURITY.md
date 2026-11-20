# Security

## Reporting a vulnerability

**Please do not open a public issue.** Report privately through [Security → Report a vulnerability](https://github.com/Piwqust/V2pn/security/advisories/new) in this repository; only the maintainers see it.

Include:

- the V2pn version (it is in the Updates window) and the operating system;
- what happens, and how to reproduce it.

> [!WARNING]
> Do not send subscription links or server addresses, not even in a private report: they carry your access key. Replace them with `***`.

Писать можно и по-русски.

## What counts

| In V2pn | Elsewhere |
| --- | --- |
| Installing an update that is not signed with the release key, or getting around the package check | Flaws in the cores themselves (Xray, sing-box, mihomo): report them to their authors |
| Running someone else's code or gaining privileges through V2pn or its updater (AmazTool) | Flaws in the protocols: report them to their authors |
| Leaking server keys, subscription links, or traffic outside the chosen mode | Servers and subscriptions: V2pn does not run any; ask your provider |
| Access to the local proxy or the core's API from the network or from another program when the user did not turn it on | |

If V2pn makes a flaw in a core or a protocol worse, report it here too.

## Supported versions

Only the latest version is supported: V2pn updates itself, and fixes ship in a new version. How releases are signed and what is checked before an update is installed: [docs/V2PN-RELEASES.md](docs/V2PN-RELEASES.md#what-exactly-is-checked).
