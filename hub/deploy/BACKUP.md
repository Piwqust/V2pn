# Резервные копии хаба

Раз в сутки, около 03:30 UTC, `v2pn-hub-backup.timer` запускает `v2pn-hub-backup.sh`. Скрипт кладёт в
`/var/backups/v2pn-hub/` архив `v2pn-hub-<дата>-<время>.tar.gz`, в нём:

- `hub.db` — копия базы. Её снимает онлайн-бэкап SQLite (`backup-db.mjs`), пока хаб работает. Копия проверена
  `integrity_check` и лежит одним файлом, без WAL;
- `keys/` — ключ хаба `hub-partner-key.pem`;
- `uploads/` — картинки партнёров.

Хранятся последние 14 дней (`HUB_BACKUP_KEEP_DAYS`). Если задан `HUB_BACKUP_REMOTE`, каждый новый архив ещё уходит туда
через rsync по ssh.

В архиве ключ хаба. Без ключа нужен новый ключ и релиз V2pn, который ему доверяет. С ключом можно подписать индекс
партнёров от имени хаба. Поэтому каталог копий читает только `v2pn-hub`, а удалённую копию держите там, куда больше
никто не ходит.

## Установка

Под root, после установки хаба по `hub/README.md`. Нужны `node` и `tar`, для удалённой копии ещё `rsync` и `ssh`.

```sh
install -d -o v2pn-hub -g v2pn-hub -m 0700 /var/backups/v2pn-hub
install -m 0640 -o root -g v2pn-hub /opt/v2pn-hub/deploy/v2pn-hub-backup.env.example /etc/v2pn-hub-backup.env
cp /opt/v2pn-hub/deploy/v2pn-hub-backup.service /opt/v2pn-hub/deploy/v2pn-hub-backup.timer /etc/systemd/system/
systemctl daemon-reload && systemctl enable --now v2pn-hub-backup.timer
systemctl start v2pn-hub-backup.service       # первая копия сразу
journalctl -u v2pn-hub-backup.service -n 20   # последняя строка: backup /var/backups/v2pn-hub/…, N bytes
systemctl list-timers v2pn-hub-backup.timer   # когда следующая
```

### Копия на другой машине

1. Ключ для хаба: `sudo -u v2pn-hub ssh-keygen -t ed25519 -N '' -f /var/lib/v2pn-hub/.ssh/backup_ed25519`, затем
   `ssh-keyscan backup.example >> /var/lib/v2pn-hub/.ssh/known_hosts`.
2. На машине для копий открытый ключ идёт в `authorized_keys` с запретом всего, кроме записи в один каталог:
   `command="rrsync -wo /srv/backups/v2pn-hub",restrict ssh-ed25519 AAAA…`. Тогда взломанный хаб не прочтёт и не
   сотрёт старые копии. Старые копии там удаляет сама та машина.
3. В `/etc/v2pn-hub-backup.env`:

   ```sh
   HUB_BACKUP_REMOTE=v2pn-backup@backup.example:
   HUB_BACKUP_SSH=ssh -i /var/lib/v2pn-hub/.ssh/backup_ed25519 -o BatchMode=yes
   ```

4. `systemctl start v2pn-hub-backup.service` и проверить, что архив появился на той машине.

## Восстановление

```sh
systemctl stop v2pn-hub
cd /var/lib/v2pn-hub
mkdir -p /root/v2pn-hub-before && mv hub.db hub.db-wal hub.db-shm keys uploads /root/v2pn-hub-before/   # прежнее, на всякий случай
tar -xzf /var/backups/v2pn-hub/v2pn-hub-20261124-033012.tar.gz -C /var/lib/v2pn-hub
chown -R v2pn-hub:v2pn-hub /var/lib/v2pn-hub && chmod 0600 keys/hub-partner-key.pem
systemctl start v2pn-hub
hub publish   # функция hub() из hub/README.md: заново подписать индекс, zip и страницы
```

`mv` может пожаловаться на `hub.db-wal` и `hub.db-shm`, если их нет: это нормально.

Если сервер пропал целиком, поставьте хаб на новом по `hub/README.md` (без `hub keygen`) и сделайте шаги выше. Ключ из
архива тот же, V2pn ему доверяет, новый релиз не нужен.

Посмотреть архив, ничего не трогая: `tar -tzvf /var/backups/v2pn-hub/v2pn-hub-….tar.gz`.
