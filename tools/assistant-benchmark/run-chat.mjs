#!/usr/bin/env node
// Asistan sohbet benchmark'ı: cases.json'daki her cümleyi gerçek sohbet ucuna
// (POST /api/assistant/chat, gerçek dil modeli) tek mesajlık bir sohbet olarak gönderir ve modelin
// açtığı kartı beklenenle karşılaştırır. Yerel geliştirme ortamı + Ai_Chatbot_ApiKey gerekir
// (README.md). Her vaka bir model çağrısıdır; 117 vaka birkaç dakika sürer.
//
//   node tools/assistant-benchmark/run-chat.mjs [--cases cases.json] [--only R] [--api http://localhost:8080]
//     [--log <API günlüğü>] [--budget <toplam maliyet dosyası>] [--repeat 2]
//
// Beklenen niyet karta çevrilir: Progress → progress kartı, Reschedule → reschedule kartı ...;
// Unknown kart açılmamasıdır. choice:true vakada model seçim sormalıdır: kart açmamalı ve
// cevabında öğrencilerin hepsini anmalıdır; adlar açıkça verildiyse her öğrenciye kart açılır.
// "allowed: false" o kartın açılmamasıdır; reply cevapta aranan düzenli ifadedir; forbid hiç
// açılmaması gereken kart türleridir. turns, asıl cümleden önce aynı sohbette gönderilen
// mesajlardır (geçmiş taşınır; düzeltme ve seçim cevabı gibi çok adımlı vakalar).
//
// Yazma kartlarında (ders taşıma, telafi) kullanıcının SÖYLEMEDİĞİ hedef alanlar boş olmalı:
// uydurulmuş bir hedef gün, saat, kaydırma ya da "her hafta" kapsamı yanlış işlem demektir.
// Kaynak alanlarını (taşınacak dersin günü/saati) modelin araçla bulup doldurması serbesttir.
//
// --log verilirse koşu boyunca API günlüğüne düşen token sayılarından maliyet hesaplanır
// (DeepSeek Flash yoğun saat fiyatı, kötü durum); --budget dosyasına koşu maliyeti eklenir.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => (value.startsWith("--") ? [...pairs, [value.slice(2), all[index + 1]]] : pairs), []));
const api = args.api ?? "http://localhost:8080";
const outDir = args.out ?? join(here, "results");
const casesFile = args.cases ?? "cases.json";
const reportName = `chat-${casesFile.replace(/\.json$/, "")}`;
const { cases: allCases } = JSON.parse(readFileSync(join(here, casesFile), "utf8"));
const cases = args.only ? allCases.filter((testCase) => testCase.id.startsWith(args.only)) : allCases;

const repeat = Number(args.repeat ?? 1);
// DeepSeek Flash, yoğun saat ($ / 1M token): önbellekten giriş, önbelleksiz giriş, çıkış.
const PRICE = { hit: 0.006, miss: 0.3, out: 1.2 };
const logStart = args.log ? statSize(args.log) : 0;

function statSize(path) {
  try {
    return readFileSync(path).length;
  } catch {
    return 0;
  }
}

const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const CARD_OF_KIND = { Progress: "progress", Dues: "dues", Reschedule: "reschedule", Makeup: "makeup", Lessons: "lessons", Navigate: "navigate" };

async function login(role) {
  const [roleName, email] = role.split(":");
  const response = await fetch(`${api}/api/dev/auth/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ role: roleName, email: email ?? null }) });
  if (!response.ok) throw new Error(`${role} girişi başarısız: ${response.status}`);
  return response.headers.getSetCookie().map((cookie) => cookie.split(";")[0]).join("; ");
}

function dateParts(key) {
  const [year, month, day] = key.split("-").map(Number);
  return new Date(year, month - 1, day);
}

function today() {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function describeMove(move) {
  if (!move) return null;
  const weekday = (key) => (key ? DAY_NAMES[dateParts(key).getDay()] : null);
  const offset = (key) => (key ? Math.round((dateParts(key) - today()) / 86_400_000) : null);
  return {
    sourceDate: move.sourceDate,
    sourceWeekday: weekday(move.sourceDate),
    sourceOffsetDays: offset(move.sourceDate),
    sourceTime: move.sourceTime?.slice(0, 5) ?? null,
    targetDate: move.targetDate,
    // Model gün adını ya alan olarak ya da tarih olarak verebilir.
    targetWeekday: move.targetWeekday ?? weekday(move.targetDate),
    targetOffsetDays: offset(move.targetDate),
    targetNextWeek: move.targetNextWeek || (move.targetDate ? offset(move.targetDate) >= 7 : false),
    targetTime: move.targetTime?.slice(0, 5) ?? null,
    scope: move.scope,
    shiftMinutes: move.shiftMinutes ?? null,
  };
}

function compare(expected, response) {
  const problems = [];
  const cards = response.cards;
  const wantCard = CARD_OF_KIND[expected.kind];
  const wantStudents = expected.students;
  const multiple = (wantStudents?.length ?? 0) > 1;

  for (const kind of expected.forbid ?? []) if (cards.some((card) => card.kind === kind)) problems.push(`açılmaması gereken ${kind} kartı açıldı`);

  if (expected.allowed === false) {
    if (cards.some((card) => card.kind === wantCard)) problems.push(`yetkisiz ${wantCard} kartı açıldı`);
    return problems;
  }

  if (expected.reply && !new RegExp(expected.reply, "i").test(response.reply)) problems.push(`cevapta /${expected.reply}/ yok`);

  // Belirsiz ad ("Arslan"): model seçim sormalı. Adlar açıkça verildiyse her biri için kart.
  if (multiple && expected.choice) {
    if (cards.some((card) => card.student)) problems.push(`seçim sorulmadan kart açıldı (${cards.map((card) => card.student?.fullName).join(", ")})`);
    for (const name of wantStudents) if (!response.reply.includes(name.split(" ")[0])) problems.push(`cevapta ${name} geçmiyor`);
    return problems;
  }
  if (multiple) {
    const opened = cards.filter((card) => card.kind === wantCard).map((card) => card.student?.fullName);
    for (const name of wantStudents) if (!opened.includes(name)) problems.push(`${name} için ${wantCard} kartı yok`);
    return problems;
  }

  if (expected.kind === "Unknown" || (wantCard && wantStudents?.length === 0 && ["progress", "reschedule", "makeup", "lessons"].includes(wantCard))) {
    const studentCards = cards.filter((card) => card.student);
    if (expected.kind === "Unknown" && cards.length) problems.push(`beklenmeyen kart: ${cards.map((card) => card.kind).join(", ")}`);
    else if (studentCards.length) problems.push(`öğrenci belirsizken kart açıldı (${studentCards.map((card) => card.student.fullName).join(", ")})`);
    return problems;
  }

  if (!wantCard) {
    if ("students" in expected) {
      const wrong = cards.filter((card) => card.student && !wantStudents.includes(card.student.fullName));
      if (wrong.length) problems.push(`yanlış öğrenci: ${wrong.map((card) => card.student.fullName).join(", ")}`);
    }
    return problems;
  }

  const card = cards.find((item) => item.kind === wantCard);
  if (!card) {
    problems.push(`${wantCard} kartı yok (açılan: ${cards.map((item) => item.kind).join(", ") || "hiç"})`);
    return problems;
  }
  if (wantStudents?.length === 1 && card.student?.fullName !== wantStudents[0]) problems.push(`öğrenci ${card.student?.fullName ?? "yok"} (beklenen ${wantStudents[0]})`);
  if (expected.navigation && card.navigation?.key !== expected.navigation) problems.push(`ekran ${card.navigation?.key} (beklenen ${expected.navigation})`);
  if (["reschedule", "makeup"].includes(wantCard)) problems.push(...invented(expected.move ?? {}, card.move));
  if (expected.move) {
    const move = describeMove(card.move);
    if (!move) problems.push("gün/saat bilgisi yok");
    else {
      for (const [key, want] of Object.entries(expected.move)) {
        // Modelin dersi bulup kaynak saati doldurması hata değildir; yalnızca istenen değerler denetlenir.
        if (want === null || want === undefined) continue;
        // Dizi: kabul edilen değerlerden biri (ör. kapsam "Once" ya da "Unspecified" - ikisi de güvenli).
        if (Array.isArray(want) ? !want.includes(move[key]) : move[key] !== want) problems.push(`${key} ${move[key]} (beklenen ${[want].flat().join(" ya da ")})`);
      }
    }
  }
  return problems;
}

// Söylenmemiş hedef: beklenende yazılmayan hedef alanı kartta dolu olmamalı.
function invented(want, move) {
  if (!move) return [];
  const problems = [];
  const saysDay = ["targetWeekday", "targetDate", "targetOffsetDays", "targetNextWeek"].some((key) => want[key] !== undefined && want[key] !== null);
  if (!saysDay && (move.targetDate || move.targetWeekday)) problems.push(`söylenmeyen hedef gün (${move.targetWeekday ?? move.targetDate})`);
  if (!want.targetNextWeek && move.targetNextWeek) problems.push("söylenmeyen 'haftaya'");
  if ((want.targetTime ?? null) === null && move.targetTime) problems.push(`söylenmeyen hedef saat (${move.targetTime.slice(0, 5)})`);
  if ((want.shiftMinutes ?? null) === null && move.shiftMinutes) problems.push(`söylenmeyen kaydırma (${move.shiftMinutes})`);
  if ((want.scope ?? null) === null && move.scope && move.scope !== "Unspecified") problems.push(`söylenmeyen kapsam (${move.scope})`);
  // "Kapsam söylenmedi" sayılan dizi beklentisi (ör. ["Once", "Unspecified"]) Recurring'e izin vermez.
  return problems;
}

async function send(cookie, message, history, studentId) {
  const response = await fetch(`${api}/api/assistant/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json", cookie },
    body: JSON.stringify({ message, history, studentId }),
  });
  return { response, body: await response.json() };
}

const sessions = new Map();
async function sessionFor(role) {
  if (!sessions.has(role)) sessions.set(role, await login(role));
  return sessions.get(role);
}

const admin = await sessionFor("Admin");
const studentIds = new Map((await (await fetch(`${api}/api/students`, { headers: { cookie: admin } })).json()).map((student) => [`${student.firstName} ${student.lastName}`, student.id]));

const results = [];
for (let round = 1; round <= repeat; round += 1) {
  for (const testCase of cases) {
    const cookie = await sessionFor(testCase.role);
    const studentId = testCase.context ? studentIds.get(testCase.context) : null;
    const started = performance.now();
    let history = [];
    let failedTurn = null;
    for (const turn of testCase.turns ?? []) {
      const previous = await send(cookie, turn, history, studentId);
      if (!previous.response.ok) {
        failedTurn = `ön mesaj HTTP ${previous.response.status}`;
        break;
      }
      history = previous.body.history;
    }
    const { response, body } = failedTurn ? { response: { ok: false, status: 0 }, body: {} } : await send(cookie, testCase.text, history, studentId);
    const elapsedMs = Math.round(performance.now() - started);
    const problems = failedTurn ? [failedTurn] : response.ok ? compare(testCase.expected, body) : [`HTTP ${response.status}: ${body.detail ?? ""}`];
    results.push({ id: testCase.id, round, category: testCase.category, text: testCase.text, turns: testCase.turns, context: testCase.context, passed: problems.length === 0, problems, elapsedMs, reply: body.reply, cards: (body.cards ?? []).map((card) => ({ kind: card.kind, student: card.student?.fullName ?? null, navigation: card.navigation?.key ?? null, move: describeMove(card.move) })) });
    process.stdout.write(problems.length ? "✗" : "✓");
  }
}
process.stdout.write("\n");

const byCategory = new Map();
for (const result of results) {
  const row = byCategory.get(result.category) ?? { passed: 0, total: 0 };
  row.total += 1;
  if (result.passed) row.passed += 1;
  byCategory.set(result.category, row);
}
const passed = results.filter((result) => result.passed).length;
const percent = (part, whole) => `%${Math.round((part / whole) * 100)}`;
const times = results.map((result) => result.elapsedMs).sort((a, b) => a - b);

// Maliyet: koşu boyunca API günlüğüne yazılan "token giriş X (önbellek Y) çıkış Z" satırları.
let cost = null;
if (args.log) {
  const added = readFileSync(args.log).subarray(logStart).toString("utf8");
  const usage = { prompt: 0, hit: 0, out: 0, calls: 0 };
  for (const match of added.matchAll(/token giriş (\d+) \(önbellek (\d+)\) çıkış (\d+)/g)) {
    usage.prompt += Number(match[1]);
    usage.hit += Number(match[2]);
    usage.out += Number(match[3]);
    usage.calls += 1;
  }
  const dollars = ((usage.prompt - usage.hit) * PRICE.miss + usage.hit * PRICE.hit + usage.out * PRICE.out) / 1_000_000;
  cost = { ...usage, dollars };
  if (args.budget) {
    const total = (Number(statSize(args.budget) ? readFileSync(args.budget, "utf8") : "0") || 0) + dollars;
    writeFileSync(args.budget, total.toFixed(6));
    cost.total = total;
  }
}

const lines = [
  `# Asistan sohbet benchmark'ı (${casesFile}, gerçek dil modeli)`,
  "",
  `Tarih: ${new Date().toLocaleString("tr-TR")} · API: ${api}`,
  "",
  `**Toplam: ${passed}/${results.length} (${percent(passed, results.length)})**${repeat > 1 ? ` (${repeat} tekrar)` : ""} · yanıt süresi ortanca ${times[Math.floor(times.length / 2)]} ms, en uzun ${times.at(-1)} ms`,
  ...(cost ? ["", `Maliyet (yoğun saat fiyatı, üst sınır): $${cost.dollars.toFixed(4)} · ${cost.calls} istek, giriş ${cost.prompt} token (önbellek ${cost.hit}), çıkış ${cost.out}${cost.total !== undefined ? ` · toplam $${cost.total.toFixed(4)}` : ""}`] : []),
  "",
  "| Kategori | Geçen | Oran |",
  "|---|---|---|",
  ...Array.from(byCategory, ([category, row]) => `| ${category} | ${row.passed}/${row.total} | ${percent(row.passed, row.total)} |`),
  "",
  "## Başarısız vakalar",
  "",
  ...(passed === results.length ? ["Yok."] : results.filter((result) => !result.passed).map((result) => `- **${result.id}**${repeat > 1 ? ` (tekrar ${result.round})` : ""} ${result.turns ? `${result.turns.map((turn) => `\`${turn}\``).join(" → ")} → ` : ""}\`${result.text}\`${result.context ? ` (bağlam: ${result.context})` : ""} — ${result.problems.join("; ")}\n  - Cevap: ${result.reply?.replaceAll("\n", " ")}`)),
];

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, `${reportName}-latest.json`), JSON.stringify(results, null, 2));
writeFileSync(join(outDir, `${reportName}-latest.md`), `${lines.join("\n")}\n`);
console.log(lines.join("\n"));
process.exitCode = passed === results.length ? 0 : 1;
