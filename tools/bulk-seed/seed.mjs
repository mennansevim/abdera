#!/usr/bin/env node
// Abdera toplu kurulum agent'ı (docs/13-toplu-kurulum-ve-fix-list.md, Pillar B).
//
// Gerçek HTTP API'ye karşı çalışır (yerelde 8081, canlıda 8080). Admin olarak giriş yapar,
// referans veriyi hazırlar, öğretmen/öğrenci/veli kayıtlarını SIRAYLA (tek tek) girer,
// velilere telefon+şifre üretir ve WhatsApp'tan yollatır, sonra veli girişini doğrular.
// Her isteğin gecikmesini ölçer; yavaş uçları ve hataları bir rapora toplar (fix listesi için).
//
// Çalıştırma:
//   API_BASE=http://localhost:8081 ADMIN_PASSWORD=... node tools/bulk-seed/seed.mjs
// Ölçek (varsayılan 10x15 = en fazla 150 öğrenci):
//   TEACHERS=2 STUDENTS_PER=3 node tools/bulk-seed/seed.mjs      # duman testi
// Bayraklar:
//   WITH_LESSONS=0   -> haftalık ders serisi (gün/saat) oluşturmayı atla (varsayılan 1)
//   VERIFY_SAMPLE=25 -> kaç velinin girişinin doğrulanacağı (varsayılan 25)

import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));

// ---------------- config ----------------
const API_BASE = process.env.API_BASE ?? "http://localhost:8081";
const ADMIN_EMAIL = process.env.ADMIN_EMAIL ?? "admin@example.com";
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD ?? "";
const MAX_TEACHERS = 10;
const MAX_STUDENTS = 150;
const TEACHERS = Number(process.env.TEACHERS ?? MAX_TEACHERS);
const STUDENTS_PER = Number(process.env.STUDENTS_PER ?? 15);
const SECOND_RATIO = Number(process.env.SECOND_RATIO ?? 0.35); // >=0.30 istendi
const WITH_LESSONS = (process.env.WITH_LESSONS ?? "1") !== "0";
const VERIFY_SAMPLE = Number(process.env.VERIFY_SAMPLE ?? 25);
const SLOW_MS = Number(process.env.SLOW_MS ?? 800);
const VERY_SLOW_MS = Number(process.env.VERY_SLOW_MS ?? 2000);

if (!ADMIN_PASSWORD) {
  console.error("HATA: ADMIN_PASSWORD env değişkeni gerekli.");
  process.exit(1);
}
if (!Number.isInteger(TEACHERS) || TEACHERS < 1 || TEACHERS > MAX_TEACHERS ||
    !Number.isInteger(STUDENTS_PER) || STUDENTS_PER < 1 || TEACHERS * STUDENTS_PER > MAX_STUDENTS) {
  console.error(`HATA: demo veri en fazla ${MAX_TEACHERS} öğretmen ve ${MAX_STUDENTS} öğrenci içerebilir.`);
  process.exit(1);
}

// ---------------- Türkçe isim havuzları ----------------
const FEMALE = ["Zeynep","Elif","Defne","Ela","Azra","Nehir","Duru","Asel","Ecrin","Miray","Meryem","İpek","Ada","Eylül","Nil","Sıla","Belinay","Yağmur","Lina","Ayşe","Fatma","Hümeyra","Zehra","Rüya","Masal"];
const MALE = ["Yusuf","Eymen","Ömer","Miraç","Ali","Mustafa","Ahmet","Mehmet","Emir","Kerem","Aras","Poyraz","Kuzey","Deniz","Çınar","Alp","Ege","Doruk","Efe","Berat","Yiğit","Kaan","Barış","Toprak","Bora"];
const LAST = ["Yılmaz","Kaya","Demir","Şahin","Çelik","Yıldız","Yıldırım","Öztürk","Aydın","Arslan","Doğan","Kılıç","Aslan","Çetin","Kara","Koç","Kurt","Özdemir","Şimşek","Polat","Korkmaz","Erdoğan","Aksoy","Güneş","Bulut"];
const ADULT_F = ["Ayşe","Fatma","Emine","Hatice","Zeynep","Elif","Meryem","Sultan","Hülya","Derya","Sevgi","Gül","Nurten","Şerife","Havva"];
const ADULT_M = ["Mehmet","Mustafa","Ahmet","Ali","Hüseyin","Hasan","İbrahim","Osman","Murat","Kemal","Serkan","Fatih","Emre","Cem","Levent"];

let seed = 424242;
const rnd = () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x7fffffff; };
const pick = (arr) => arr[Math.floor(rnd() * arr.length)];
const chance = (p) => rnd() < p;

// benzersiz TR cep telefonu: +90 5XX XXX XX XX
let phoneCounter = 3000000; // 5 + 3000000.. -> 53000000xx
const nextPhone = () => { phoneCounter += 1; return `+905${String(phoneCounter).padStart(9, "0").slice(0, 9)}`; };

// ---------------- HTTP + cookie + telemetri ----------------
let cookie = "";
const timings = new Map(); // template -> [ms...]
const findings = []; // {level, area, msg, detail}
let requestCount = 0;

function template(method, path) {
  const p = path
    .replace(/\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, "/:id")
    .split("?")[0];
  return `${method} ${p}`;
}

async function req(method, path, body) {
  const url = API_BASE + path;
  const headers = { "Content-Type": "application/json" };
  if (cookie) headers["Cookie"] = cookie;
  const t0 = performance.now();
  let res, text;
  try {
    res = await fetch(url, { method, headers, body: body ? JSON.stringify(body) : undefined });
    text = await res.text();
  } catch (err) {
    findings.push({ level: "error", area: "network", msg: `İstek başarısız: ${template(method, path)}`, detail: String(err) });
    throw err;
  }
  const ms = performance.now() - t0;
  requestCount += 1;
  const tpl = template(method, path);
  if (!timings.has(tpl)) timings.set(tpl, []);
  timings.get(tpl).push(ms);

  if (ms > VERY_SLOW_MS) findings.push({ level: "error", area: "latency", msg: `Çok yavaş (${Math.round(ms)}ms): ${tpl}`, detail: `eşik ${VERY_SLOW_MS}ms` });

  // cookie yakala
  const setCookie = res.headers.getSetCookie?.() ?? (res.headers.get("set-cookie") ? [res.headers.get("set-cookie")] : []);
  for (const c of setCookie) {
    const nv = c.split(";")[0];
    if (nv) cookie = nv;
  }

  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* boş/non-json */ }

  if (!res.ok) {
    const err = new Error(`HTTP ${res.status} ${tpl}: ${text?.slice(0, 300)}`);
    err.status = res.status;
    err.body = json;
    throw err;
  }
  return json;
}

// ---------------- yardımcılar ----------------
const pct = (arr, p) => { if (!arr.length) return 0; const s = [...arr].sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.floor(p / 100 * s.length))]; };
const dateISO = (d) => d.toISOString().slice(0, 10);
const today = new Date("2026-09-12T12:00:00Z");
function pastDate(daysAgo) { const d = new Date(today); d.setDate(d.getDate() - daysAgo); return d; }
function birthDate(minAge, maxAge) { const age = minAge + Math.floor(rnd() * (maxAge - minAge + 1)); const d = new Date(today); d.setFullYear(d.getFullYear() - age); d.setDate(1 + Math.floor(rnd() * 27)); return dateISO(d); }

const DAYS = ["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"];
const SLOTS = ["15:00:00","16:00:00","17:00:00","18:00:00","19:00:00"];

// ---------------- ana akış ----------------
async function main() {
  const started = performance.now();
  console.log(`# Abdera toplu kurulum — ${TEACHERS} öğretmen × ${STUDENTS_PER} öğrenci, 2. enstrüman oranı ~${Math.round(SECOND_RATIO*100)}%`);
  console.log(`API: ${API_BASE}`);

  // 1) admin login
  await req("POST", "/api/auth/login", { email: ADMIN_EMAIL, password: ADMIN_PASSWORD });
  console.log("✓ admin girişi");

  // Yarım kalmış bir koşu yeniden başlatıldığında aynı demo telefonunu üretme.
  const existingGuardians = await req("GET", "/api/guardians");
  for (const guardian of existingGuardians) {
    if (/^\+905\d{9}$/.test(guardian.phoneNumber)) {
      phoneCounter = Math.max(phoneCounter, Number(guardian.phoneNumber.slice(4)));
    }
  }

  // 2) enstrümanlar
  const instruments = await req("GET", "/api/instruments");
  const byCode = Object.fromEntries(instruments.map((i) => [i.code, i]));
  const CODES = ["PIANO","GUITAR","VIOLIN","DRUMS"].filter((c) => byCode[c]);
  if (CODES.length < 2) { console.error("Yetersiz enstrüman seed'i:", instruments); process.exit(1); }
  console.log(`✓ enstrümanlar: ${CODES.join(", ")}`);

  // 3) öğretmenler — TEACHERS yeni kayıt adedi değil, veritabanındaki toplam hedeftir.
  // Böylece bootstrap öğretmeni olan temiz bir veritabanında 10 öğretmenin üstüne çıkılmaz.
  const existingTeachers = await req("GET", "/api/teachers");
  if (existingTeachers.length > TEACHERS) {
    throw new Error(`Veritabanında zaten ${existingTeachers.length} öğretmen var; hedef ${TEACHERS}. Önce demo veri temizleme migration'ını uygulayın.`);
  }

  const codeByInstrumentId = Object.fromEntries(instruments.map((instrument) => [instrument.id, instrument.code]));
  const teachers = existingTeachers.map((teacher) => {
    const instrCodes = teacher.instrumentIds.map((id) => codeByInstrumentId[id]).filter(Boolean);
    if (!instrCodes.length) throw new Error(`${teacher.firstName} ${teacher.lastName} öğretmeninin enstrümanı yok.`);
    return {
      id: teacher.id,
      name: `${teacher.firstName} ${teacher.lastName}`,
      primaryCode: instrCodes[0],
      instrCodes,
      instrumentIds: teacher.instrumentIds,
    };
  });

  for (let i = existingTeachers.length; i < TEACHERS; i++) {
    const primaryCode = CODES[i % CODES.length];
    const secondCode = CODES[(i + 2) % CODES.length];
    const instrCodes = [...new Set([primaryCode, secondCode])];
    const instrumentIds = instrCodes.map((c) => byCode[c].id);
    const first = pick(chance(0.5) ? ADULT_F : ADULT_M);
    const last = pick(LAST);
    const resp = await req("POST", "/api/teachers", { firstName: first, lastName: last, instrumentIds, email: null });
    teachers.push({ id: resp.teacher.id, name: `${first} ${last}`, primaryCode, instrCodes, instrumentIds });
    process.stdout.write(`\r  öğretmen ${teachers.length}/${TEACHERS}`);
  }
  console.log(`\n✓ ${teachers.length} öğretmen`);

  // enstrüman kodu -> onu öğreten öğretmenler (2. enstrüman ataması için)
  const teachersByCode = {};
  for (const c of CODES) teachersByCode[c] = teachers.filter((t) => t.instrCodes.includes(c));

  // 4) öğrenciler + kayıtlar + veliler. Demo aidat verisi özellikle üretilmez.
  const existingStudents = await req("GET", "/api/students");
  const targetStudentCount = TEACHERS * STUDENTS_PER;
  if (existingStudents.length > targetStudentCount) {
    throw new Error(`Veritabanında zaten ${existingStudents.length} öğrenci var; hedef ${targetStudentCount}. Önce demo veri temizleme migration'ını uygulayın.`);
  }
  const studentsToCreate = targetStudentCount - existingStudents.length;
  const credentials = []; // {phone, password, studentName}
  let studentTotal = 0, secondEnrollTotal = 0, guardianTotal = 0, lessonSeriesTotal = 0;

  for (let ti = 0; ti < teachers.length; ti++) {
    const teacher = teachers[ti];
    for (let si = 0; si < STUDENTS_PER; si++) {
      if (studentTotal >= studentsToCreate) break;
      const female = chance(0.5);
      const sFirst = pick(female ? FEMALE : MALE);
      const sLast = pick(LAST);
      const startedAt = dateISO(pastDate(30 + Math.floor(rnd() * 300)));

      // 5a) öğrenci + 1. kayıt (öğretmenin primary enstrümanı)
      const primaryInstrId = byCode[teacher.primaryCode].id;
      const st = await req("POST", `/api/teachers/${teacher.id}/students`, {
        firstName: sFirst, lastName: sLast, birthDate: birthDate(6, 17), instrumentId: primaryInstrId, startedAt,
      });
      studentTotal += 1;
      const enrollments = [{ id: st.enrollmentId, code: teacher.primaryCode, instrumentId: primaryInstrId }];

      // 5b) ~%35 ikinci enstrüman (farklı enstrüman + onu öğreten bir öğretmen)
      if (chance(SECOND_RATIO)) {
        const otherCodes = CODES.filter((c) => c !== teacher.primaryCode && teachersByCode[c].length);
        if (otherCodes.length) {
          const code2 = pick(otherCodes);
          const t2 = pick(teachersByCode[code2]);
          const instr2 = byCode[code2].id;
          try {
            const en2 = await req("POST", `/api/students/${st.studentId}/enrollments`, { teacherId: t2.id, instrumentId: instr2, startedAt });
            enrollments.push({ id: en2.id, code: code2, instrumentId: instr2 });
            secondEnrollTotal += 1;
          } catch (e) { findings.push({ level: "warn", area: "enrollment", msg: "2. kayıt başarısız", detail: e.message }); }
        }
      }

      // 5c) veli + bağlama + şifre
      const gFirst = pick(female ? ADULT_F.concat(ADULT_M) : ADULT_M.concat(ADULT_F));
      const phone = nextPhone();
      const g = await req("POST", "/api/guardians", { firstName: gFirst, lastName: sLast, phoneNumber: phone });
      await req("POST", `/api/students/${st.studentId}/guardians`, { guardianId: g.id, relationship: female ? "Anne/Baba" : "Anne/Baba", isPrimary: true });
      const pw = await req("POST", `/api/guardians/${g.id}/reset-password`, {});
      credentials.push({ phone: pw.phoneNumber, password: pw.password, studentName: `${sFirst} ${sLast}` });
      guardianTotal += 1;

      // 4d) haftalık ders serisi (gün/saat) — benchmark "çalışma günü" verisi için
      for (const en of enrollments) {
        if (WITH_LESSONS && en.code === teacher.primaryCode) {
          try {
            await req("POST", "/api/lesson-series", {
              enrollmentId: en.id, dayOfWeek: pick(DAYS), startTime: pick(SLOTS),
              durationMinutes: 45, effectiveFrom: dateISO(today), effectiveUntil: null,
            });
            lessonSeriesTotal += 1;
          } catch (e) { findings.push({ level: "warn", area: "scheduling", msg: "Ders serisi kurulamadı", detail: e.message }); }
        }
      }
      process.stdout.write(`\r  öğretmen ${ti + 1}/${teachers.length} · öğrenci ${si + 1}/${STUDENTS_PER} · toplam ${studentTotal}   `);
    }
  }
  const finalStudentCount = existingStudents.length + studentTotal;
  const secondEnrollmentRate = studentTotal ? Math.round(secondEnrollTotal / studentTotal * 100) : 0;
  console.log(`\n✓ ${finalStudentCount} öğrenci (${studentTotal} yeni), ${secondEnrollTotal} ikinci kayıt (%${secondEnrollmentRate}), ${guardianTotal} yeni veli, ${lessonSeriesTotal} ders serisi, 0 demo aidat`);

  // 5) veli girişi doğrulama (örneklem) — ADMIN cookie'sini kaybetmemek için ayrı cookie ile
  const adminCookie = cookie;
  let verified = 0, failed = 0;
  const sample = credentials.slice(0, Math.min(VERIFY_SAMPLE, credentials.length));
  for (const c of sample) {
    cookie = ""; // temiz oturum
    try {
      const me = await loginGuardian(c.phone, c.password);
      if (me?.id) verified += 1; else failed += 1;
    } catch { failed += 1; findings.push({ level: "error", area: "guardian-login", msg: "Veli girişi başarısız", detail: `${c.phone} / ${c.studentName}` }); }
  }
  cookie = adminCookie;
  console.log(`✓ veli girişi doğrulandı: ${verified}/${sample.length} (başarısız: ${failed})`);

  // 6) rapor
  writeReport({ started, studentTotal: finalStudentCount, secondEnrollTotal, guardianTotal, lessonSeriesTotal, verified, failed, sampleSize: sample.length, credentials });
}

async function loginGuardian(phone, password) {
  // guardian login kendi cookie'sini set eder; sonra /me ile doğrula
  const r = await req("POST", "/api/guardian/login", { phoneNumber: phone, password });
  const me = await req("GET", "/api/guardian/me");
  return me ?? r;
}

function writeReport(s) {
  const perEndpoint = [...timings.entries()].map(([tpl, arr]) => ({
    endpoint: tpl, count: arr.length, p50: Math.round(pct(arr, 50)), p95: Math.round(pct(arr, 95)), max: Math.round(Math.max(...arr)),
    slow: arr.filter((x) => x > SLOW_MS).length,
  })).sort((a, b) => b.p95 - a.p95);

  const totalMs = performance.now() - s.started;
  const report = {
    when: new Date().toISOString(), api: API_BASE,
    totals: { teachers: TEACHERS, students: s.studentTotal, secondEnrollments: s.secondEnrollTotal, guardians: s.guardianTotal, lessonSeries: s.lessonSeriesTotal, demoReceivables: 0, requests: requestCount, wallSeconds: Math.round(totalMs / 1000) },
    guardianLoginCheck: { verified: s.verified, failed: s.failed, sample: s.sampleSize },
    latencyByEndpoint: perEndpoint,
    findings,
  };
  writeFileSync(join(__dirname, "last-run-report.json"), JSON.stringify(report, null, 2));

  const lines = [];
  lines.push(`# Toplu kurulum raporu — ${report.when}`);
  lines.push(`API: ${API_BASE} · ${report.totals.requests} istek · ${report.totals.wallSeconds}s`);
  lines.push("");
  lines.push(`## Sonuç: ${s.studentTotal} öğrenci, ${s.secondEnrollTotal} ikinci kayıt, ${s.guardianTotal} veli, ${s.lessonSeriesTotal} ders serisi`);
  lines.push(`Demo aidat: 0 · Veli girişi doğrulama: ${s.verified}/${s.sampleSize} başarılı`);
  lines.push("");
  lines.push("## Gecikme (endpoint bazında, p95'e göre)");
  lines.push("| endpoint | adet | p50 | p95 | max | >eşik |");
  lines.push("|---|--:|--:|--:|--:|--:|");
  for (const e of perEndpoint) lines.push(`| ${e.endpoint} | ${e.count} | ${e.p50} | ${e.p95} | ${e.max} | ${e.slow} |`);
  lines.push("");
  lines.push(`## Bulgular (${findings.length})`);
  if (!findings.length) lines.push("- (yok)");
  for (const f of findings.slice(0, 200)) lines.push(`- **[${f.level}/${f.area}]** ${f.msg}${f.detail ? ` — ${f.detail}` : ""}`);
  writeFileSync(join(__dirname, "last-run-report.md"), lines.join("\n"));

  // ilk 20 kimlik bilgisi örneği (WhatsApp'a giden şifre deseni doğrulaması için)
  writeFileSync(join(__dirname, "sample-credentials.json"), JSON.stringify(s.credentials.slice(0, 20), null, 2));

  console.log(`\n📄 rapor: tools/bulk-seed/last-run-report.md (+ .json), örnek kimlikler: sample-credentials.json`);
}

main().catch((e) => { console.error("\nAGENT HATASI:", e.message); writeReport?.({ started: performance.now(), studentTotal: 0, secondEnrollTotal: 0, guardianTotal: 0, lessonSeries: 0, verified: 0, failed: 0, sampleSize: 0, credentials: [] }); process.exit(1); });
