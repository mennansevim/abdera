#!/usr/bin/env bash
# concurrent_reschedule.js k6 kosusundan SONRA cagrilir. Yalnizca HTTP durum kodlarina
# guvenmek yeterli degil (200/200 donup veri bozulmus olabilir) - gercek dogrulama DB'de:
# dogru davranista original_lesson_id = <lessonId> olan TAM 1 satir olmali (tek kazanan),
# digerleri 409 ile reddedilmis olmali. >1 ise lost-update / duplicate-write bugu VAR demektir.
set -euo pipefail

LESSON_ID="${1:?kullanim: verify-concurrent-reschedule.sh <lessonId>}"

echo "--- ${LESSON_ID} icin original_lesson_id referansi olan satirlar ---"
docker exec abdera-web-db-1 psql -U abdera -d abdera -t -c \
  "SELECT id, student_id, teacher_id, start_at, status, created_at FROM lessons WHERE original_lesson_id = '${LESSON_ID}' ORDER BY created_at;"

COUNT=$(docker exec abdera-web-db-1 psql -U abdera -d abdera -t -A -c \
  "SELECT count(*) FROM lessons WHERE original_lesson_id = '${LESSON_ID}';")

echo "--- orijinal dersin son durumu ---"
docker exec abdera-web-db-1 psql -U abdera -d abdera -t -c \
  "SELECT id, status, updated_at FROM lessons WHERE id = '${LESSON_ID}';"

echo
echo "replacement_count=${COUNT}"
if [ "${COUNT}" -eq 1 ]; then
  echo "SONUC: OK - tam 1 replacement satiri var, veri bozulmasi yok."
  exit 0
elif [ "${COUNT}" -eq 0 ]; then
  echo "SONUC: UYARI - hic replacement yok (butun istekler 409 almis olabilir, bu da 'gecerli' bir sonuc - k6 loglarina bak)."
  exit 0
else
  echo "SONUC: HATA - ${COUNT} adet replacement satiri var. Lost-update / duplicate-write bugu DOGRULANDI (Lesson entity'sinde optimistic concurrency yok)."
  exit 2
fi
