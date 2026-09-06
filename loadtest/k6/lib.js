// Paylaşılan yardımcılar - CLAUDE.md'deki kritik akışlar için k6 helper'ları.
// Gerçek kullanıcı verisi kullanılmaz: tüm kimlikler .env'deki bootstrap admin ve
// backend/src/Abdera.Api/Shared/LoadTestFixtures.cs ile üretilen sentetik fixture'lardır
// (POST /api/dev/load-test/seed - yalnızca Development ortamında, AdminOnly).
import http from 'k6/http';
import { check } from 'k6';

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
export const ADMIN_EMAIL = __ENV.ADMIN_EMAIL || 'admin@example.com';
export const ADMIN_PASSWORD = __ENV.ADMIN_PASSWORD || 'DevAdmin123!';
export const TEACHER_COUNT = parseInt(__ENV.TEACHER_COUNT || '8', 10);
export const TEACHER_PASSWORD = 'LoadTest123!';

export function teacherEmail(i) {
  return `loadtest.teacher.${i % TEACHER_COUNT}@abdera.local`;
}

const jsonParams = { headers: { 'Content-Type': 'application/json' } };

// Her VU kendi JS runtime'ında bir kez çalışır ve modül-seviyesi state o VU'nun TÜM
// iterasyonları arasında korunur (k6'nın belgelenen VU izolasyon modeli) - bu yüzden
// "bu VU zaten login oldu mu" durumunu burada modül seviyesinde tutmak güvenli:
// her VU kendi kopyasını görür, aralarında sızıntı olmaz.
let cachedRole = null;

export function loginAsAdmin() {
  const res = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({
    email: ADMIN_EMAIL,
    password: ADMIN_PASSWORD,
  }), jsonParams);
  check(res, { 'admin login 200': (r) => r.status === 200 }, { flow: 'login' });
  // Round 3'te 109 istek 401 aldi (calendar/students/lessons) - kok neden burasiydi: login
  // POST'u BASARISIZ olsa bile cachedRole kosulsuz 'admin'e set ediliyordu, sonraki
  // ensureAdminSession() cagrisi "zaten girdim" saniyor, oturumsuz istek atiliyordu. Artik
  // yalnizca gercekten 200 donduyse onbelleklenir - basarisizsa bir sonraki cagri tekrar dener.
  if (res.status === 200) cachedRole = 'admin';
  return res;
}

export function loginAsTeacher(vuIndex) {
  const res = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({
    email: teacherEmail(vuIndex),
    password: TEACHER_PASSWORD,
  }), jsonParams);
  check(res, { 'teacher login 200': (r) => r.status === 200 }, { flow: 'login' });
  if (res.status === 200) cachedRole = 'teacher';
  return res;
}

// Bir VU'nun ilk iterasyonunda tek sefer login olup sonraki iterasyonlarda oturumu
// (k6'nın VU başına otomatik cookie jar'ı ile) yeniden kullanması için.
export function ensureAdminSession() {
  if (cachedRole !== 'admin') {
    loginAsAdmin();
  }
}

export function randomInt(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

export function isoDaysFromNow(days) {
  const d = new Date(Date.now() + days * 86400000);
  return d.toISOString();
}

export { jsonParams };
