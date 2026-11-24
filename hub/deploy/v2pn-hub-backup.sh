#!/bin/sh
# The V2pn hub's daily backup (deploy/BACKUP.md), run by v2pn-hub-backup.timer: a consistent copy of hub.db made with
# SQLite's online backup while the hub runs, then keys/ and uploads/, in one archive under HUB_BACKUP_DIR. Archives
# older than HUB_BACKUP_KEEP_DAYS days are removed. With HUB_BACKUP_REMOTE set, the new archive is also copied there
# with rsync over ssh (HUB_BACKUP_SSH is the ssh command). The archive holds the hub's key: with
# HUB_BACKUP_AGE_RECIPIENT (an age public key, age1...) the copy that leaves the server is encrypted to it with age
# (v2pn-hub-<stamp>.tar.gz.age); without it, the copy goes as it is and a warning says so. The archive kept here stays
# as it is: the server holds the key anyway, and a restore here needs no private key.
set -eu
umask 077

data=${HUB_DATA:-/var/lib/v2pn-hub}
out=${HUB_BACKUP_DIR:-/var/backups/v2pn-hub}
keep=${HUB_BACKUP_KEEP_DAYS:-14}
remote=${HUB_BACKUP_REMOTE:-}
recipient=${HUB_BACKUP_AGE_RECIPIENT:-}
node=${HUB_NODE:-node}
age=${HUB_AGE:-age}
rsync=${HUB_RSYNC:-rsync}
here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

case $keep in
  ''|*[!0-9]*|0) echo "HUB_BACKUP_KEEP_DAYS must be a number of days, 1 or more" >&2; exit 2 ;;
esac
# An age X25519 recipient is "age1" and 58 more of bech32's lowercase letters and digits.
case $recipient in
  '') ;;
  age1*[!0-9a-z]*) echo "HUB_BACKUP_AGE_RECIPIENT must be an age public key: age1..." >&2; exit 2 ;;
  age1?*) ;;
  *) echo "HUB_BACKUP_AGE_RECIPIENT must be an age public key: age1..." >&2; exit 2 ;;
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
  if [ -n "$recipient" ]; then
    # The archive here is made all the same; the run fails so that the timer's unit shows it.
    if ! command -v "$age" > /dev/null 2>&1; then
      echo "HUB_BACKUP_AGE_RECIPIENT is set, but age is not installed (apt install age): $archive is not sent" >&2
      exit 1
    fi
    sealed="$work/v2pn-hub-$stamp.tar.gz.age"
    "$age" -r "$recipient" -o "$sealed" "$archive"
    "$rsync" -t -e "${HUB_BACKUP_SSH:-ssh -o BatchMode=yes}" "$sealed" "$remote"
    echo "sent $(basename "$sealed") to $remote, encrypted with age"
  else
    echo "warning: HUB_BACKUP_AGE_RECIPIENT is not set: the copy on $remote holds the hub's key unencrypted (deploy/BACKUP.md)" >&2
    "$rsync" -t -e "${HUB_BACKUP_SSH:-ssh -o BatchMode=yes}" "$archive" "$remote"
    echo "sent $(basename "$archive") to $remote, not encrypted"
  fi
fi
echo "backup $archive, $(wc -c < "$archive" | tr -d ' ') bytes"
