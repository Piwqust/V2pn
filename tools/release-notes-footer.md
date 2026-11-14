---

### Скачать

| Система | Файл |
| --- | --- |
| Windows 10 и 11, x64 | [V2pn-windows-x64.zip](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-windows-x64.zip) |
| Windows 11, ARM64 | [V2pn-windows-arm64.zip](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-windows-arm64.zip) |
| macOS 13.6 и новее, Apple silicon (M1 и новее) | [V2pn-macos-arm64.dmg](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-macos-arm64.dmg) |
| macOS 13.6 и новее, Intel | [V2pn-macos-x64.dmg](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-macos-x64.dmg) |

Если V2pn уже установлен, скачивать ничего не нужно: он обновится сам.

Остальные файлы в списке ниже предназначены не для людей. `-update.zip`, `.zip` для macOS, `release.json` и `release.json.sig` читает само приложение при обновлении; пакет `-update.zip` не содержит ядер и для первой установки не подходит. Файлы `.sha256` — контрольные суммы: `sha256sum -c V2pn-windows-x64.zip.sha256`.

При первом запуске Windows и macOS предупредят, что не знают разработчика: у приложения нет платной подписи. Как установить и пройти предупреждение, написано в [README](https://github.com/Piwqust/V2pn#скачать).

**English.** Take one of the four files in the table; an installed V2pn updates itself. The other files are read by the app's updater, and `-update.zip` has no cores, so it is not for a first install. Install steps and the first-launch warnings are explained in [README.en.md](https://github.com/Piwqust/V2pn/blob/v2pn/README.en.md#download).
