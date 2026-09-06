// Sürükle-bırak sonrası tarih güncelleme - EŞ ZAMANLI güncelleme / lost-update testi.
// Amaç: backend/src/Abdera.Api/Modules/Scheduling/Features/UpdateLesson.cs hiçbir
// ETag/If-Match veya xmin kontrolü yapmıyor (bkz. Explore raporu, Lesson entity'sinde
// optimistic concurrency YOK) - N eş zamanlı PATCH isteği aynı "Normal" durumundaki dersi
// aynı anda okursa hepsi TOCTOU penceresinden geçip birden fazla "replacement" ders satırı
// üretebilir. Bu script kasıtlı olarak TEK bir dersi çok sayıda VU ile aynı anda hedefler;
// asıl doğrulama k6 sonrası scripts/verify-concurrent-reschedule.sh ile DB'de yapılır
// (yalnızca HTTP durum kodlarına bakmak yetmez - ikisi de 200 dönüp veri bozulmuş olabilir).
//
// İzole çalışır: ramp.js'in mixed-flow reschedule akışı +60 günden itibaren hedef seçiyor,
// bu script kasıtlı olarak +2..+5 gün penceresini kullanıyor - iki script birbirine karışmaz.
//
// Çalıştırma:
//   LESSON_ID=<guid> STUDENT_ID=<guid> TEACHER_ID=<guid> \
//   k6 run --summary-export=results/roundN-concurrency.json loadtest/k6/concurrent_reschedule.js
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { BASE_URL, ADMIN_EMAIL, ADMIN_PASSWORD, jsonParams } from './lib.js';

const CONCURRENCY = parseInt(__ENV.CONCURRENCY || '20', 10);

export const options = {
  scenarios: {
    race: {
      executor: 'per-vu-iterations',
      exec: 'race',
      vus: CONCURRENCY,
      iterations: 1,
      maxDuration: '30s',
    },
  },
  // setup() admin olarak login olup cookie'yi jar'a koyuyor; noCookiesReset olmadan k6 bunu
  // race() calisirken sifirlardi (bkz. ramp.js'teki ayni nottaki debug bulgusu).
  noCookiesReset: true,
};

export function setup() {
  const loginRes = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({
    email: ADMIN_EMAIL, password: ADMIN_PASSWORD,
  }), jsonParams);
  if (loginRes.status !== 200) {
    throw new Error(`setup: admin login basarisiz (${loginRes.status})`);
  }

  let lessonId = __ENV.LESSON_ID;
  let studentId = __ENV.STUDENT_ID;
  let teacherId = __ENV.TEACHER_ID;

  if (!lessonId) {
    // +2..+5 gun penceresinde, "Normal" durumda ve henuz hic dokunulmamis bir ders sec.
    const from = new Date(Date.now() + 2 * 86400000).toISOString();
    const to = new Date(Date.now() + 5 * 86400000).toISOString();
    const calRes = http.get(`${BASE_URL}/api/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, jsonParams);
    const candidates = calRes.status === 200 ? calRes.json().filter((l) => l.status === 'Normal') : [];
    if (candidates.length === 0) {
      throw new Error('setup: +2..+5 gun penceresinde Normal durumda ders bulunamadi - once /api/dev/load-test/seed calistirilmali.');
    }
    const target = candidates[0];
    lessonId = target.id;
    studentId = target.studentId;
    teacherId = target.teacherId;
  }

  console.log(`[concurrent_reschedule] hedef lessonId=${lessonId} studentId=${studentId} teacherId=${teacherId} concurrency=${CONCURRENCY}`);
  return { lessonId, studentId, teacherId };
}

// Her VU AYNI dersi, FARKLI (ve birbirleriyle celismeyen) yeni saatlere tasimaya calisir -
// boylece bir reddin nedeni "gercek bir zaman cakismasi" degil, yalnizca "Status artik
// Normal degil" olmali (dogru davranista TAM OLARAK 1 istek basarili olmali, digerleri 409
// almali). Farkli hedef saatler kasitli: ikisi ayni yeni saati secseydi reddin nedeni
// belirsizlesirdi (gercek cakisma mi, yoksa status yarisi mi).
export function race(data) {
  // setup() ayri bir VU/context'te calisir (k6 modeli) - onun login'i buradaki VU'nun
  // cookie jar'ina TASINMAZ, o yuzden her VU PATCH'ten once KENDI oturumunu acar. Tek
  // iterasyonluk bir VU oldugu icin noCookiesReset burada zaten sorun degil.
  const loginRes = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({
    email: ADMIN_EMAIL, password: ADMIN_PASSWORD,
  }), jsonParams);
  if (loginRes.status !== 200) {
    console.log(`[concurrent_reschedule] vu login basarisiz status=${loginRes.status}`);
    return;
  }

  const vuOffset = exec.vu.idInInstance;
  const startAt = new Date(Date.now() + (200 + vuOffset) * 86400000).toISOString();

  const res = http.patch(`${BASE_URL}/api/lessons/${data.lessonId}`, JSON.stringify({
    studentId: data.studentId,
    teacherId: data.teacherId,
    startAt,
    durationMinutes: 50,
    status: 'Normal',
  }), jsonParams);

  check(res, {
    'reschedule 200 veya 409 (baska durum sistem hatasi)': (rr) => rr.status === 200 || rr.status === 409,
  });

  console.log(`[concurrent_reschedule] vu=${vuOffset} status=${res.status} body=${res.body?.slice(0, 200)}`);
}
