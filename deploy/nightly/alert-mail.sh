#!/usr/bin/env bash
# run abdera rapor/sorun maili: alert-mail.sh "<konu>" <gövde dosyası>
# SMTP bilgileri repoda DEĞİL, bu Mac'te ~/.config/abdera-nightly/mail.env (chmod 600):
#   SMTP_URL=smtp://smtp.gmail.com:587
#   SMTP_USER=<gmail adresi>
#   SMTP_PASS=<Gmail uygulama şifresi>
#   MAIL_TO=<bildirimin gideceği adres>
# Dosya yoksa çıkış 3 - çağıran taraf yedek kanala (push bildirimi + GitHub issue) geçer.
set -euo pipefail
SUBJECT="${1:?konu gerekli}"; BODY_FILE="${2:?gövde dosyası gerekli}"
CONF="$HOME/.config/abdera-nightly/mail.env"
[ -r "$CONF" ] || { echo "mail.env yok: $CONF" >&2; exit 3; }
# shellcheck disable=SC1090
. "$CONF"
: "${SMTP_URL:?}" "${SMTP_USER:?}" "${SMTP_PASS:?}" "${MAIL_TO:?}"

msg=$(mktemp); trap 'rm -f "$msg"' EXIT
{
  printf 'From: run abdera <%s>\r\n' "$SMTP_USER"
  printf 'To: %s\r\n' "$MAIL_TO"
  printf 'Subject: =?UTF-8?B?%s?=\r\n' "$(printf '%s' "$SUBJECT" | base64 | tr -d '\n')"
  printf 'Date: %s\r\n' "$(LC_ALL=C date -R 2>/dev/null || LC_ALL=C date '+%a, %d %b %Y %H:%M:%S %z')"
  printf 'MIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: 8bit\r\n\r\n'
  cat "$BODY_FILE"
} > "$msg"

curl -sS --ssl-reqd --url "$SMTP_URL" --user "$SMTP_USER:$SMTP_PASS" \
  --mail-from "$SMTP_USER" --mail-rcpt "$MAIL_TO" --upload-file "$msg"
echo "mail gönderildi: $MAIL_TO"
