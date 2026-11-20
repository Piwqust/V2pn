# V2pn site: design

Written before the build (2026-11-20) and kept current. The source of the identity is the brand book in
`docs/brand/v2pn-brand-book.html` and the app itself (`v2rayN/v2rayN.Desktop/Assets/V2pnTheme.axaml`).

## Brief

- **Subject.** V2pn, a free open-source VPN client for Windows and macOS built on v2rayN. One window: pick a
  server, pick Proxy or VPN, pick what goes through the VPN, press «Подключиться». Around it, a provider
  platform: a provider fills one form and their people get a tab of their own in V2pn.
- **Audience.** (1) People in Russia who got a subscription link from a provider, usually through a Telegram bot,
  and need a desktop client. (2) Small providers who sell those subscriptions. (3) Moderators.
- **Job of the site.** Get a person from «what is this» to the right download and the first connection. Get a
  provider to «Создать партнёра». Hold the documentation for both. Run the provider tools (sign-in, «Мой
  партнёр», moderation) on the hub's API.
- **Language.** Russian. The provider guide also in English; the provider pages (`/p/<id>/`) switch RU/EN.
- **Constraints.** Static files on the owner's server behind Caddy, under
  `default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'`: no inline
  script, no CDN, fonts from the site. `/api/*`, `/r/*`, `/releases/*` belong to the hub. Old URLs keep working.
- **Assumptions.** No version is fetched at run time (GitHub is cross-origin); the version shown is read from
  `CHANGELOG.md` at build. The screenshots are real captures of V2pn; some are in English, the app's default
  language (marked for replacement with Russian captures in `src/data/site.ts`).

## Concept

Three candidates, from different territories.

| | A. Развилка (the mark) | B. Рифлёное стекло (the material) | C. Проверено (the process) |
|---|---|---|---|
| Idea | The site is V2pn's fork seen from above: every request rises up one stem and leaves by one of two roads, direct in plain sight or through a server behind ribbed glass | Everything that goes through a server is seen through a pane of ribbed glass; the pane slides like the app's one long move | Nothing on the page claims «Защищено» until a check has passed; sections tick off like the app's connection log |
| Anchor | The logo is literally «a road seen from above» with a stem (you) and two branches (direct, server), and the product's core feature is exactly that split (the three route presets) | Brand book chapter 05: private things sit behind ribbed glass | The app writes «Защищено» only after a real connection check |
| Signature | Site names climb the fork and branch by the chosen preset; the server branch runs under the glass, where names turn into light | A glass pane over the dotted map that slides on «Подключиться» | A live checklist with ping numerals ticking |
| O / F / Fe / P | 5 / 5 / 4 / 1 | 3 / 4 / 4 / 3 | 3 / 4 / 5 / 3 |

**Pick A**, with B as the material of the right branch (it already belongs to the same brand rule: the second way
is behind glass). Swap test: put another VPN in the sentence and it breaks; the fork is V2pn's mark and the split
is V2pn's preset. C lends the status section its honesty (no number on the page is invented: pings are labelled
as an example) and the 404 lends from A: both roads lead somewhere real.

```
Idea:        Всегда две дороги. Everything on the site rises up one stem and leaves by two ways:
             direct, in plain sight, or through a server, behind ribbed glass.
Metaphor:    V2pn's fork seen from above: a road junction, and privacy glass over one of its roads.
Signature:   «Развилка» (src/motion/fork.ts): the mark at full height as two roads; real site names
             climb the stem and take their branch by the selected preset (the app's three presets,
             switchable); the server branch runs under ribbed glass where names become light.
             Canvas 2D on the GSAP ticker; the glass renderer is the brand book's.
Emotion:     Calm control. «I can see where each site goes.»
Motion verb: Rise, then branch at 45°. Things come up the stem (vertical), turn at 45°; the one long
             move is the glass pane sliding across (page transitions), on the Glass curve.
Screenshot:  Tunnel black, the Signal fork, «gosuslugi.ru» sharp on the left road, light streaks
             under the ribs on the right, «Всегда две дороги» set huge.
```

## Art direction

**Palette** (brand book, chapter 03; app tokens). Signal on Tunnel is the identity of the app itself, so the
"near-black + vermilion" default is the brand here, not a reflex. Paper is where people read and write.

| Role | Token | Hex | Rule |
|---|---|---|---|
| ground, dark | `--tunnel` | `#0B0B0D` | marketing pages, the hero, the footer, provider pages |
| raised on tunnel | `--graphite` | `#19191E` / `#24242B` | panels, code, cards |
| ground, light | `--paper` | `#F2F0EC` / `#E4E1DB` | docs, sign-in, «Мой партнёр», moderation |
| ink | `--ink` | `#ECECF1` on tunnel, `#0B0B0D` on paper | |
| muted | `--muted` | `#9B9BA8` on tunnel (7.0:1), `#5F5C57` on paper (6.0:1) | |
| line | `--line` | 12 % white / 14 % tunnel | hairlines |
| accent | `--signal` | `#FF3D1F` | the one thing to act on; text on Signal is Tunnel; never body text on Paper |
| states | `--protected` `--caution` | `#3DD68C`, `#F2B84B` | only for states, as in the app |
| light in glass | `--flare` | `#FF8A2A` | only inside the ribbed glass |

One global variable: `html[data-ground]` (tunnel | paper) sets `--c-bg/--c-fg/--c-muted/--c-line`; a section that
changes ground carries `data-ground` too and redefines the same four.

**Type** (brand book, chapter 04; the app ships Inter). Inter Tight for display (200 for numerals, 600/800 for
headlines), Inter for text and UI, JetBrains Mono only for data: addresses, ports, pings, protocols, versions,
commands, file names. Self-hosted, variable, latin + cyrillic. Tracking tightens with size: -0.06em at 140 px and up,
-0.035em at 56, -0.02em at 28, 0 at body. Tabular figures wherever numbers stand in a column.

```
--t-mega  clamp(3.4rem, 0.6rem + 11.2vw, 13rem)   / 0.86  / -0.06em   Inter Tight 800
--t-xl    clamp(2.4rem, 1.2rem + 4.4vw, 5.6rem)   / 0.95  / -0.045em  Inter Tight 600
--t-l     clamp(1.6rem, 1.2rem + 1.6vw, 2.6rem)   / 1.08  / -0.025em  Inter Tight 600
--t-m     clamp(1.2rem, 1.1rem + 0.45vw, 1.5rem)  / 1.3   / -0.01em   Inter 500
--t-body  1.0625rem (17 px)                        / 1.55             Inter 400
--t-s 0.875rem, --t-xs 0.8125rem                                       UI, captions, data
--t-num   clamp(5rem, 2rem + 12vw, 14rem)          / 0.8   / -0.06em  Inter Tight 200 (numerals)
```

**Grid.** Thirteen columns, taken from the mark: the fork is 3.25u wide, so one u is four columns. Columns 1–4 are
the left road (direct), 5–8 the stem (you), 9 the notch, 10–13 the right road (through a server). Text starts on
line 5 (the stem); asides hang on line 1; pictures of what goes through a server sit on 10–13. Phones: four columns
(one u). Section rhythm varies: tight (8u), normal (16u), airy (28u), full viewport for the hero.

**Shape.** No radius in the site's own chrome. The one cut is the fork's: a 45° chamfer on the top-right corner of
primary buttons and of the glass panels (`--cut`). Screenshots of the app keep their own rounded window.

**Motion.** The brand's curves, registered as CustomEase: `standard` cubic-bezier(.2,0,0,1) for UI (200–500 ms),
`glass` cubic-bezier(.7,0,.2,1) for the pane (640–900 ms), `exit` cubic-bezier(.4,0,1,1) for things that leave.
No bounce, no overshoot, no spin of the mark. Reveals rise (y 40 → 0, the stem's direction); things in the server
column arrive on the 45° diagonal. Reduced motion: native scroll, no canvas animation (one still frame), cross-fades
of 120 ms.

**Interaction treatments (one per role).**
- Primary buttons (Signal, chamfered): the chamfer opens (8 → 18 px) and the arrow branches off at 45°. Press: 1 px down.
- Secondary buttons (outlined): the outline floods with ink from below (the stem), label inverts.
- Nav links: an underline draws from the left; the current page keeps it and a Signal square (the fork's cap).
- Cards and rows that are links: a Signal rail rises up the left edge (the stem) and the ↗ travels 4 px at 45°.
- Inline links: underline at rest, thickens into a band on hover.
- Utility links (legal, footer small print): colour only.

**Idle.** The fork keeps flowing while the visitor reads (names climb at a calm rate); it pauses off screen and
holds a still frame under reduced motion. Elsewhere the page is still at rest by decision: the hovers and the
transitions carry the life. Phones draw the fork at 30 fps from sprites (QA: 59 fps scroll, LCP 1.1 s on the
throttled phone).

**Removed in the last pass** (the skill's "remove one accessory"): the count-up of the 38 ms numeral. It is an
example ping from the screenshot; animating it suggested a live measurement.

**Continuity.** No loader (a product site; the brand forbids animating the mark while loading). The hero intro:
the headline rises line by line while the first names climb. Page transition: a ribbed glass pane slides across the
viewport left to right on the way in, and on to the right on the way out; back navigation slides right to left.

## Composition per page

```
/ (home)
  HERO  ┌ col 1–6 ─────────────┬ col 7–13 ──────────────────┐   full viewport, sticky stage
        │ V2pn                  │        ┃ glass ┃            │
        │ Всегда две            │   ███  ┃▓▓▓▓▓▓▓┃            │   the fork, names climbing
        │ дороги                │   ███▄▄┃▓▓▓▓▓▓▓┃            │
        │ lead · [Скачать ↗]    │     ███┃       ┃            │
        │ [preset][preset][preset]  caption of the preset     │
        └───────────────────────┴────────────────────────────┘
  WINDOW   sticky screenshot (cols 5–13) + three numbered decisions scrolling in col 1–4  (sticky narrative)
  CHECK    numeral «41 мс» (Inter Tight 200, mega) + the check log in mono                 (data/facts)
  PROMISE  one paragraph set large across cols 5–13                                         (statement)
  TAB      partner screenshot cols 1–8, text cols 10–13                                     (asymmetric pair)
  INSTALL  index rows: Windows x64 / ARM, macOS Apple silicon / Intel, the one command        (index list)
  FAQ      two columns of short answers                                                       (definitions)
  FOOTER   the fork + wordmark at mega size, links on lines 1 / 5 / 10                        (finale)

/download/        poster headline + the detected file as one big row, the others as rows; three steps with real
                  screenshots on a horizontal strip (a real sequence)
/providers/       hero with the partner tab screenshot; three levels as a road table; the URL with #sub= behind
                  glass (the part the server never sees); Remnawave rule (code); five steps; FAQ; CTA
/docs/…           paper; sidebar nav on line 1, article on 5–10, contents on 11–13
/providers/guide/ the provider guide from docs/partners, same docs layout (RU, EN)
/providers/login/ paper form on 1–6, the glass fork (still) on 8–13
/providers/cabinet/, /admin/   paper work surfaces; the V2pn window preview on a tunnel stage
/404              the fork where both roads are links: left «На главную», right «Документация»
/p/<id>/          (the hub's template) tunnel ground in the provider's colours, the same type and buttons
```

## Genericness test

- *A generic model would put a dotted world map with glowing pins in the hero.* The map is the app's home, so it
  stays in the screenshots, but the hero is the fork and the route presets, which only V2pn has.
- *It would use three equal feature cards.* Features are told through the real window in a sticky narrative and
  the three numbered decisions the app is built around (docs/V2PN-UX.md).
- *It would round everything.* The chrome is square with one 45° cut, from the mark.
- *Mono for every label* is a tell: mono is kept for data only.
