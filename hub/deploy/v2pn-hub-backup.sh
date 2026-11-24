#!/bin/sh
# The V2pn hub's daily backup (deploy/BACKUP.md), run by v2pn-hub-backup.timer: a consistent copy of hub.db made with
# SQLite's online backup while the hub runs, then keys/ and uploads/, in one archive under HUB_BACKUP_DIR. Archives
# older than HUB_BACKUP_KEEP_DAYS days are removed. With HUB_BACKUP_REMOTE set, the new archive is also copied there
# with rsync over ssh (HUB_BACKUP_SSH is the ssh command).
set -eu
umask 077

data=${HUB_DATA:-/var/lib/v2pn-hub}
out=${HUB_BACKUP_DIR:-/var/backups/v2pn-hub}
keep=${HUB_BACKUP_KEEP_DAYS:-14}
remote=${HUB_BACKUP_REMOTE:-}
node=${HUB_NODE:-node}
here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

case $keep in
  ''|*[!0-9]*|0) echo "HUB_BACKUP_KEEP_DAYS must be a number of days, 1 or more" >&2; exit 2 ;;
esac

mkdir -p "$out"
stamp=$(date -u +%Y%m%d-%H%M%S)
work=$(mktemp -d "$out/.v2pn-hub-$stamp.XXXXXX")
trap 'rm -rf "$work"' EXIT

"$node" "$here/backup-db.mjs" "$data/hub.db" "$work/hub.db"
tar -czf "$work/backup.tar.gz" -C "$work" hub.db -C "$data" keys uploads
tar -tzf "$work/backup.tar.gz" > /dev/null
archive="$out/v2pn-hub-$stamp.tar.gz"
mv "$work/backup.tar.gz" "$archive"

# One a day: what is older than the last $keep days goes.
find "$out" -maxdepth 1 -type f -name 'v2pn-hub-*.tar.gz' -mtime +"$((keep - 1))" -exec rm -f {} +

if [ -n "$remote" ]; then
  rsync -t -e "${HUB_BACKUP_SSH:-ssh -o BatchMode=yes}" "$archive" "$remote"
fi
echo "backup $archive, $(wc -c < "$archive" | tr -d ' ') bytes"
