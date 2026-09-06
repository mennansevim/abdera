// Ana yük profili - kritik akışların ağırlıklı karışımı.
// PROFILE env değişkeni ile üç modda çalışır:
//   PROFILE=ramp (varsayılan) -> 10 -> 50 -> 200 -> 500 VU, her kademe 2 dk
//   PROFILE=spike             -> ani 0 -> 500 VU sıçraması
//   PROFILE=soak              -> 15 dk sabit 50 VU (memory leak / connection pool sızıntısı için)
//
// Çalıştırma:
//   k6 run -e PROFILE=ramp  --summary-export=results/roundN-ramp.json  loadtest/k6/ramp.js
//   k6 run -e PROFILE=spike --summary-export=results/roundN-spike.json loadtest/k6/ramp.js
//   k6 run -e PROFILE=soak  --summary-export=results/roundN-soak.json  loadtest/k6/ramp.js
import http from 'k6/http';
import { check, sleep, group } from 'k6';
import exec from 'k6/execution';
import { Rate } from 'k6/metrics';
import {
  BASE_URL, ADMIN_EMAIL, ADMIN_PASSWORD, TEACHER_COUNT,
  teacherEmail, TEACHER_PASSWORD, jsonParams, randomInt, ensureAdminSession,
  loginAsAdmin, loginAsTeacher,
} from './lib.js';

// Gerçek sunucu hatası (5xx / bağlantı hatası) - başarı kriteri "hata oranı < %1" burayı
// hedefler. İş kuralı reddi (409 çakışma, 400/422 validasyon) AYRI sayılır - bunlar
// uygulamanın DOĞRU çalıştığının kanıtı, sistem hatası değil.
export const appErrorRate = new Rate('app_errors');
export const businessConflictRate = new Rate('business_conflicts');

const PROFILE = __ENV.PROFILE || 'ramp';

const PROFILES = {
  ramp: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '30s', target: 10 },
      { duration: '2m', target: 10 },
      { duration: '30s', target: 50 },
      { duration: '2m', target: 50 },
      { duration: '30s', target: 200 },
      { duration: '2m', target: 200 },
      { duration: '1m', target: 500 },
      { duration: '2m', target: 500 },
      { duration: '30s', target: 0 },
    ],
    gracefulRampDown: '10s',
  },
  spike: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '10s', target: 500 },
      { duration: '1m', target: 500 },
      { duration: '15s', target: 0 },
    ],
    gracefulRampDown: '5s',
  },
  soak: {
    executor: 'constant-vus',
    vus: 50,
    duration: '15m',
  },
  // Yalnizca script gelistirme/dogrulama icin - gercek round olcumlerinde kullanilmaz.
  smoke: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '5s', target: 5 },
      { duration: '15s', target: 5 },
      { duration: '5s', target: 0 },
    ],
  },
};

export const options = {
  scenarios: { main: { exec: 'mixedFlow', ...PROFILES[PROFILE] } },
  thresholds: {
    http_req_duration: ['p(95)<500', 'p(99)<1000'],
    app_errors: ['rate<0.01'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  // k6 varsayilani her ITERASYONDA cookie jar'i sifirlar (izolasyon icin) - bu, gercek bir
  // kullanicinin tarayici oturumunu YANLIS temsil eder (bir VU burada "oturum acmis bir
  // kullanici" demek, "her istekte yeniden giris yapan biri" degil) ve ensureAdminSession()'in
  // "VU basina bir kere login ol" onbellegini gecersiz kilardi (ilk iterasyondan sonra tum
  // istekler cookie'siz kalip 401 donerdi - bu script gelistirilirken debug2/3.js ile
  // dogrulandi). noCookiesReset: true ile jar VU'nun TUM iterasyonlari boyunca kalici olur.
  noCookiesReset: true,
};

function fetchLessons(fromDays, toDays) {
  const from = new Date(Date.now() + fromDays * 86400000).toISOString();
  const to = new Date(Date.now() + toDays * 86400000).toISOString();
  const res = http.get(`${BASE_URL}/api/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, jsonParams);
  return res.status === 200 ? res.json() : [];
}

export function setup() {
  const loginRes = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({
    email: ADMIN_EMAIL, password: ADMIN_PASSWORD,
  }), jsonParams);
  if (loginRes.status !== 200) {
    throw new Error(`setup: admin login basarisiz (${loginRes.status}): ${loginRes.body}`);
  }

  const teachersRes = http.get(`${BASE_URL}/api/teachers`, jsonParams);
  const teachers = teachersRes.status === 200 ? teachersRes.json() : [];

  // Reschedule havuzu iki ayri pencereden toplanir (93 gunluk API sinirinin altinda kalacak
  // sekilde). Round 4'te tek pencere (-20g,+60g) kullanilinca onceki turlarin reschedule
  // trafigi bu pencerede "Normal" durumda TEK BIR ders BIRAKMADAN once tuketti (dogruladim:
  // -20g..+60g penceresinde 0 Normal, 2786 Rescheduled kaldi) - kalan az sayida dersi cok
  // sayida VU hedefleyip UpdateLesson.cs'teki YENI "FOR UPDATE" kilidinde kuyruklanmaya
  // basladi (Round 3 -> Round 4: p95 513ms -> 1.58s, p99 785ms -> 8.25s - bu bir performans
  // REGRESYONU degil, bu k6 script'inin kendi test-veri tukenmesi). Ikinci pencere
  // (+65g,+155g) doReschedule()'nin URETTIGI replacement derslerin dustugu araligi (+60g ve
  // sonrasi, bkz. asagidaki doReschedule) kapsar - bu havuz turler arasinda KENDINI YENILER
  // (her reschedule yeni bir Normal replacement uretir), NUKS eden bir tukenme olmaz.
  const windowA = fetchLessons(-20, 60);
  const windowB = fetchLessons(65, 155);
  const lessons = [...windowA, ...windowB].filter((l) => l.status === 'Normal');
  if (lessons.length < 50) {
    console.log(`[setup] UYARI: reschedule havuzu kucuk (${lessons.length} ders) - loadtest fixture'ini genisletmek gerekebilir (POST /api/dev/load-test/seed).`);
  }

  return { teachers, lessons };
}

export function mixedFlow(data) {
  const r = Math.random();
  if (r < 0.15) {
    doLogin();
  } else {
    // calendar/crud/reschedule kimlik dogrulama gerektirir (TeacherOrAdmin/AdminOnly) -
    // VU'nun bu VU-omurlu ilk cagrisinda oturum yoksa once acilir (lib.js: ensureAdminSession,
    // VU basina bir kere calisir, sonraki iterasyonlarda k6'nin otomatik VU-cookie-jar'i
    // sayesinde tekrar login gerekmez).
    ensureAdminSession();
    if (r < 0.65) {
      doCalendar(data);
    } else if (r < 0.85) {
      doCrud();
    } else {
      doReschedule(data);
    }
  }
  sleep(randomInt(5, 15) / 10);
}

function recordOutcome(res, flow) {
  const isServerError = res.status === 0 || res.status >= 500;
  const isBusinessConflict = res.status === 409 || res.status === 400 || res.status === 422;
  appErrorRate.add(isServerError, { flow });
  businessConflictRate.add(isBusinessConflict, { flow });
  return !isServerError;
}

function doLogin() {
  group('login', () => {
    // ONEMLI: lib.js'teki loginAsAdmin/loginAsTeacher kullaniliyor - bunlar VU'nun
    // paylasilan "hangi roldeyim" onbellegini (cachedRole) de gunceller. Burada ham
    // http.post kullanilsaydi cookie jar'daki oturum degisir ama ensureAdminSession()
    // hala "zaten adminim" saniyor, sonraki AdminOnly cagrilari (crud/reschedule) 403
    // alirdi - bu script gelistirilirken tam olarak bu bug DEBUG=1 ile yakalandi
    // (CREATE/RESCHEDULE FAIL status=403 - VU sessizce ogretmene donmustu).
    const useAdmin = Math.random() < 0.2;
    const res = useAdmin ? loginAsAdmin() : loginAsTeacher(exec.vu.idInTest);

    if (res.status === 200) {
      const me = http.get(`${BASE_URL}/api/auth/me`, jsonParams);
      check(me, { 'me 200': (rr) => rr.status === 200 }, { flow: 'login' });
      recordOutcome(me, 'login');
    }
  });
}

function doCalendar(data) {
  group('calendar', () => {
    // Sentetik veri araligi: seed -26 haftadan +6 haftaya kadar (LoadTestFixtures.cs).
    const windowStartOffsetDays = randomInt(-180, 35);
    const from = new Date(Date.now() + windowStartOffsetDays * 86400000).toISOString();
    const to = new Date(Date.now() + (windowStartOffsetDays + 7) * 86400000).toISOString();
    let url = `${BASE_URL}/api/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;
    if (data.teachers.length > 0 && Math.random() < 0.3) {
      const teacher = data.teachers[randomInt(0, data.teachers.length - 1)];
      url += `&teacherId=${teacher.id}`;
    }
    const res = http.get(url, jsonParams);
    check(res, { 'calendar 200': (rr) => rr.status === 200 }, { flow: 'calendar' });
    recordOutcome(res, 'calendar');
  });
}

function doCrud() {
  group('students_crud', () => {
    const suffix = `${exec.vu.idInTest}-${exec.vu.iterationInInstance}-${Date.now()}`;
    const createRes = http.post(`${BASE_URL}/api/students`, JSON.stringify({
      firstName: `K6Load${suffix}`,
      lastName: 'Fixture',
      birthDate: '2014-05-10',
    }), jsonParams);
    const created = check(createRes, { 'create 201': (rr) => rr.status === 201 }, { flow: 'crud' });
    recordOutcome(createRes, 'crud');
    if (!created) {
      if (__ENV.DEBUG) console.log(`CREATE FAIL status=${createRes.status} body=${createRes.body}`);
      return;
    }

    const studentId = createRes.json('id');

    const updateRes = http.patch(`${BASE_URL}/api/students/${studentId}`, JSON.stringify({
      firstName: `K6Load${suffix}`,
      lastName: 'FixtureUpdated',
      birthDate: '2014-05-10',
      status: 'Active',
    }), jsonParams);
    check(updateRes, { 'update 200': (rr) => rr.status === 200 }, { flow: 'crud' });
    recordOutcome(updateRes, 'crud');

    // "Silme" = soft delete (CLAUDE.md: hard delete yok, durum kolonu kullanilir).
    const deleteRes = http.patch(`${BASE_URL}/api/students/${studentId}`, JSON.stringify({
      firstName: `K6Load${suffix}`,
      lastName: 'FixtureUpdated',
      birthDate: '2014-05-10',
      status: 'Inactive',
    }), jsonParams);
    check(deleteRes, { 'soft-delete 200': (rr) => rr.status === 200 }, { flow: 'crud' });
    recordOutcome(deleteRes, 'crud');
  });
}

function doReschedule(data) {
  group('reschedule', () => {
    if (!data.lessons || data.lessons.length === 0) return;
    const lesson = data.lessons[randomInt(0, data.lessons.length - 1)];

    // Cakismayi minimize etmek icin VU+iterasyona gore genis ve dagitik bir gelecek zaman
    // dilimi seciliyor (bu genel karisimin amaci coktan zamanlama, kilit rekabetini,
    // N+1'i olcmek - kasitli cakisma testi ayri bir script'te: concurrent_reschedule.js).
    const uniqueOffsetMinutes = ((exec.vu.idInTest * 97) + (exec.vu.iterationInInstance * 13)) % (60 * 24 * 300);
    const startAt = new Date(Date.now() + 60 * 86400000 + uniqueOffsetMinutes * 60000).toISOString();

    const res = http.patch(`${BASE_URL}/api/lessons/${lesson.id}`, JSON.stringify({
      studentId: lesson.studentId,
      teacherId: lesson.teacherId,
      startAt,
      durationMinutes: 50,
      status: 'Normal',
    }), jsonParams);
    const ok = check(res, { 'reschedule 2xx/409': (rr) => rr.status === 200 || rr.status === 409 }, { flow: 'reschedule' });
    recordOutcome(res, 'reschedule');
    if (!ok && __ENV.DEBUG) console.log(`RESCHEDULE FAIL status=${res.status} body=${res.body}`);
  });
}
