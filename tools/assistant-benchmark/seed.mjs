#!/usr/bin/env node
// Benchmark ve simülasyonun başlangıç durumu. BOŞ bir yerel veritabanında bir kez çalıştırılır:
// örnek okul verisi (sabit tohum), ücret tarifesi, her kayda Ekim aidatı; Ada Çetin, Bora Aydın
// ve Ege Kurt ödemiş, Kerem Aksoy 2.000 ₺ kısmi ödemiş. Diğer sekiz öğrenci borçlu. Lara Arslan
// ve Emir Aydın'ın birer telafi hakkı var.
//
//   node tools/assistant-benchmark/seed.mjs [--api http://localhost:8080]

import { randomUUID } from "node:crypto";

const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => (value.startsWith("--") ? [...pairs, [value.slice(2), all[index + 1]]] : pairs), []));
const api = args.api ?? "http://localhost:8080";
const PERIOD = "2026-10";

const login = await fetch(`${api}/api/dev/auth/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ role: "Admin" }) });
if (!login.ok) throw new Error(`Yönetici girişi başarısız (${login.status}). API geliştirme modunda ve Auth__DevLogin__Enabled=true mı?`);
const cookie = login.headers.getSetCookie().map((value) => value.split(";")[0]).join("; ");

async function call(method, path, body, headers = {}) {
  const response = await fetch(`${api}${path}`, { method, headers: { "Content-Type": "application/json", cookie, ...headers }, body: body ? JSON.stringify(body) : undefined });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

const seed = await call("POST", "/api/dev/mock-data/seed", {});
console.log("örnek veri:", seed.body.status);
if (seed.body.status !== "seeded") throw new Error("Veritabanı boş değil; benchmark boş bir veritabanı ister.");

for (const [courseKind, monthlyAmount] of [["Individual", 6000], ["Group", 4500]]) {
  const rate = await call("POST", "/api/tuition-rates", { courseKind, lessonsPerMonth: 4, monthlyAmount, effectiveFrom: "2026-01-01", currency: "TRY" });
  console.log(`tarife ${courseKind}:`, rate.status);
}

const students = (await call("GET", "/api/students")).body;
for (const student of students) {
  const [enrollment] = (await call("GET", `/api/students/${student.id}/enrollments`)).body;
  const created = await call("POST", "/api/receivables", { enrollmentId: enrollment.id, period: PERIOD });
  if (![201, 409].includes(created.status)) throw new Error(`${student.firstName} aidatı açılamadı: ${created.status}`);
}

const dues = new Map((await call("GET", "/api/billing/dues")).body.map((due) => [due.studentName, due]));
for (const [name, amount] of [["Ada Çetin"], ["Bora Aydın"], ["Ege Kurt"], ["Kerem Aksoy", 2000]]) {
  const due = dues.get(name);
  const paid = await call("POST", `/api/receivables/${due.id}/payments`, { amount: amount ?? due.amount, paymentDate: "2026-10-05", method: "Cash", reference: null, note: "benchmark" }, { "Idempotency-Key": `bench-${randomUUID()}` });
  if (paid.status !== 201) throw new Error(`${name} ödemesi kaydedilemedi: ${paid.status}`);
}

// Telafi akışı için iki öğrenciye gerçek hak: son derslerinden biri yoklamada Mazeretli
// işaretlenir (yoklama ucu hakkı kendisi açar). Örnek veri bazı dersleri hak açmadan Mazeretli
// yazdığı için yoklaması Mazeretli olmayan bir ders seçilir.
const now = new Date();
const from = new Date(now.getTime() - 14 * 86_400_000).toISOString();
const lessons = (await call("GET", `/api/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(now.toISOString())}`)).body;
for (const name of ["Lara Arslan", "Emir Aydın"]) {
  const lesson = lessons
    .filter((item) => item.studentName === name && item.status !== "Cancelled" && item.attendanceStatus !== "Excused")
    .sort((a, b) => b.startAt.localeCompare(a.startAt))[0];
  if (!lesson) throw new Error(`${name} için son iki haftada ders yok.`);
  const marked = await call("POST", `/api/lessons/${lesson.id}/attendance`, { status: "Excused", note: "benchmark: telafi hakkı" });
  if (marked.status !== 200) throw new Error(`${name} yoklaması yazılamadı: ${marked.status}`);
}

console.log(`${students.length} öğrenci, ${dues.size} Ekim aidatı; 3 ödenmiş, 1 kısmi. Telafi hakkı: Lara Arslan, Emir Aydın.`);
