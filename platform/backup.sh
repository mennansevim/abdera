#!/bin/sh
# Platform veritabanının günlük dökümü (PLATFORM.md §2). compose.yml'deki `backup` servisi çalıştırır.
# Döküm platform sunucusunun kendi diskindedir: disk kaybına karşı koruma DEĞİL, yanlış silme/bozulmaya karşıdır.
# Sunucu dışı kopya için PLATFORM.md'deki "Anahtar yedeği" komutuyla aynı zamanda `backups` volume'ünü de al.
set -eu

KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"
mkdir -p /backups

while true; do
  stamp="$(date -u +%F)"
  target="/backups/platform-$stamp.sql.gz"
  if [ ! -s "$target" ]; then
    # Yarım dosya kalmasın: önce geçici dosyaya, bitince yerine.
    if pg_dump -h db -U platform -d abdera_platform | gzip > "$target.tmp" && [ -s "$target.tmp" ]; then
      mv "$target.tmp" "$target"
      echo "platform dökümü alındı: $target"
    else
      rm -f "$target.tmp"
      echo "platform dökümü ALINAMADI" >&2
    fi
  fi
  find /backups -name 'platform-*.sql.gz' -mtime +"$KEEP_DAYS" -delete
  sleep 3600
done
