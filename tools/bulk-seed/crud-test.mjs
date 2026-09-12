#!/usr/bin/env node
// docs/13 Pillar D — öğrenci/öğretmen/veli CRUD doğrulaması (create/read/update/delete).
// Çalıştırma: API_BASE=http://localhost:8081 ADMIN_PASSWORD=... node tools/bulk-seed/crud-test.mjs

const API_BASE = process.env.API_BASE ?? "http://localhost:8081";
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD ?? "";
if (!ADMIN_PASSWORD) { console.error("ADMIN_PASSWORD gerekli"); process.exit(1); }

let cookie = "";
async function req(method, path, body, { noThrow = false } = {}) {
  const headers = { "Content-Type": "application/json" };
  if (cookie) headers.Cookie = cookie;
  const res = await fetch(API_BASE + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
  const setCookie = res.headers.getSetCookie?.() ?? [];
  for (const c of setCookie) cookie = c.split(";")[0];
  const text = await res.text();
  let json = null; try { json = text ? JSON.parse(text) : null; } catch {}
  if (!res.ok && !noThrow) throw new Error(`HTTP ${res.status} ${method} ${path}: ${text.slice(0, 200)}`);
  return { status: res.status, json };
}

let pass = 0, fail = 0;
function chk(actual, expected, name) {
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  console.log(`  ${ok ? "✓" : "✗"} ${name}${ok ? "" : ` — beklenen ${JSON.stringify(expected)}, gelen ${JSON.stringify(actual)}`}`);
  ok ? pass++ : fail++;
}

async function main() {
  console.log("== admin login ==");
  chk((await req("POST", "/api/auth/login", { email: "admin@example.com", password: ADMIN_PASSWORD })).status, 200, "login");

  const teachers = (await req("GET", "/api/teachers")).json;
  const teacher0 = teachers[0];
  const instrumentId = teacher0.instrumentIds[0];

  // ---------- TEACHER ----------
  console.log("== ÖĞRETMEN CRUD ==");
  const tc = (await req("POST", "/api/teachers", { firstName: "CRUD", lastName: "Ogretmen", instrumentIds: [instrumentId], email: null })).json;
  const newTeacherId = tc.teacher.id;
  chk(!!newTeacherId, true, "create");
  chk((await req("GET", "/api/teachers")).json.filter(t => t.id === newTeacherId).length, 1, "read (listede var)");
  chk((await req("PATCH", `/api/teachers/${newTeacherId}`, { firstName: "CRUDX", lastName: "Ogretmen", status: "Active", instrumentIds: [instrumentId] })).status, 200, "update");
  chk((await req("GET", "/api/teachers")).json.find(t => t.id === newTeacherId).firstName, "CRUDX", "update kalıcı");
  chk((await req("PATCH", `/api/teachers/${newTeacherId}`, { firstName: "CRUDX", lastName: "Ogretmen", status: "Inactive", instrumentIds: [instrumentId] })).status, 200, "soft-delete (Inactive)");
  chk((await req("GET", "/api/teachers")).json.find(t => t.id === newTeacherId).status, "Inactive", "inactive kalıcı");

  // ---------- STUDENT (+enrollment) ----------
  console.log("== ÖĞRENCİ CRUD (+kayıt) ==");
  const sc = (await req("POST", `/api/teachers/${teacher0.id}/students`, { firstName: "CRUD", lastName: "Ogrenci", birthDate: "2015-05-05", instrumentId, startedAt: "2026-09-01" })).json;
  const studentId = sc.studentId, enrollmentId = sc.enrollmentId;
  chk(!!studentId, true, "create (öğrenci+kayıt)");
  chk((await req("GET", "/api/students")).json.filter(s => s.id === studentId).length, 1, "read (listede var)");
  chk((await req("PATCH", `/api/students/${studentId}`, { firstName: "CRUDX", lastName: "Ogrenci", birthDate: "2015-05-05", status: "Active" })).status, 200, "update");
  chk((await req("GET", "/api/students")).json.find(s => s.id === studentId).firstName, "CRUDX", "update kalıcı");
  chk((await req("GET", `/api/students/${studentId}/enrollments`)).json.length, 1, "read kayıtlar");
  chk((await req("DELETE", `/api/students/${studentId}/enrollments/${enrollmentId}`)).status, 204, "delete (kaydı sonlandır)");
  chk((await req("GET", `/api/students/${studentId}/enrollments`)).json.filter(e => e.status === "Active").length, 0, "aktif kayıt kalmadı");

  // ---------- GUARDIAN (+link +password) ----------
  console.log("== VELİ CRUD (+bağlama +şifre) ==");
  const suffix = String(Date.now()).slice(-7);        // her çalıştırmada benzersiz telefon
  const rawPhone = `0555${suffix}`;
  const normPhone = `+90555${suffix}`;
  const gc = (await req("POST", "/api/guardians", { firstName: "CRUD", lastName: "Veli", phoneNumber: rawPhone })).json;
  const guardianId = gc.id;
  chk(!!guardianId, true, "create");
  chk((await req("GET", "/api/guardians")).json.filter(g => g.id === guardianId).length, 1, "read (listede var)");
  chk((await req("PATCH", `/api/guardians/${guardianId}`, { firstName: "CRUDX", lastName: "Veli", phoneNumber: rawPhone })).status, 200, "update");
  chk([200, 201].includes((await req("POST", `/api/students/${studentId}/guardians`, { guardianId, relationship: "Anne", isPrimary: true })).status), true, "öğrenciye bağla");
  const pw = (await req("POST", `/api/guardians/${guardianId}/reset-password`, {})).json;
  chk(!!pw.password, true, "reset-password");
  chk(pw.password.slice(0, 3), "Cru", "şifre bağlı çocuğun adından türedi (CRUDX)");
  cookie = ""; // guardian login temiz oturum
  chk((await req("POST", "/api/guardian/login", { phoneNumber: rawPhone, password: pw.password })).status, 200, "veli girişi (yeni şifre)");
  chk((await req("GET", "/api/guardian/me")).json.phoneNumber, normPhone, "guardian/me doğru");

  console.log(`\n== SONUÇ: ${pass} geçti, ${fail} başarısız ==`);
  console.log(fail === 0 ? "TÜM CRUD TESTLERİ GEÇTİ ✓" : "BAZI TESTLER BAŞARISIZ ✗");
  process.exit(fail === 0 ? 0 : 1);
}
main().catch(e => { console.error("HATA:", e.message); process.exit(1); });
