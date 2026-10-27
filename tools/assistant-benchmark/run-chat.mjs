#!/usr/bin/env node
// Asistan sohbet benchmark'ı: cases.json'daki her cümleyi gerçek sohbet ucuna
// (POST /api/assistant/chat, gerçek dil modeli) tek mesajlık bir sohbet olarak gönderir ve modelin
// açtığı kartı beklenenle karşılaştırır. Yerel geliştirme ortamı + Ai_Chatbot_ApiKey gerekir
// (README.md). Her vaka bir model çağrısıdır; 117 vaka birkaç dakika sürer.
//
//   node tools/assistant-benchmark/run-chat.mjs [--cases cases.json] [--only R] [--api http://localhost:8080]
//
// Beklenen niyet karta çevrilir: Progress → progress kartı, Reschedule → reschedule kartı ...;
// Unknown kart açılmamasıdır. choice:true vakada model seçim sormalıdır: kart açmamalı ve
// cevabında öğrencilerin hepsini anmalıdır; adlar açıkça verildiyse her öğrenciye kart açılır.
// "allowed: false" o kartın açılmamasıdır; reply cevapta aranan düzenli ifadedir.

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
  if (expected.move) {
    const move = describeMove(card.move);
    if (!move) problems.push("gün/saat bilgisi yok");
    else {
      for (const [key, want] of Object.entries(expected.move)) {
        // Modelin dersi bulup kaynak saati doldurması hata değildir; yalnızca istenen değerler denetlenir.
        if (want === null || want === undefined) continue;
        if (move[key] !== want) problems.push(`${key} ${move[key]} (beklenen ${want})`);
      }
    }
  }
  return problems;
}

const sessions = new Map();
async function sessionFor(role) {
  if (!sessions.has(role)) sessions.set(role, await login(role));
  return sessions.get(role);
}

const admin = await sessionFor("Admin");
const studentIds = new Map((await (await fetch(`${api}/api/students`, { headers: { cookie: admin } })).json()).map((student) => [`${student.firstName} ${student.lastName}`, student.id]));

const results = [];
for (const testCase of cases) {
  const cookie = await sessionFor(testCase.role);
  const started = performance.now();
  const response = await fetch(`${api}/api/assistant/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json", cookie },
    body: JSON.stringify({ message: testCase.text, history: [], studentId: testCase.context ? studentIds.get(testCase.context) : null }),
  });
  const elapsedMs = Math.round(performance.now() - started);
  const body = await response.json();
  const problems = response.ok ? compare(testCase.expected, body) : [`HTTP ${response.status}: ${body.detail ?? ""}`];
  results.push({ id: testCase.id, category: testCase.category, text: testCase.text, context: testCase.context, passed: problems.length === 0, problems, elapsedMs, reply: body.reply, cards: (body.cards ?? []).map((card) => ({ kind: card.kind, student: card.student?.fullName ?? null, navigation: card.navigation?.key ?? null, move: describeMove(card.move) })) });
  process.stdout.write(problems.length ? "✗" : "✓");
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

const lines = [
  `# Asistan sohbet benchmark'ı (${casesFile}, gerçek dil modeli)`,
  "",
  `Tarih: ${new Date().toLocaleString("tr-TR")} · API: ${api}`,
  "",
  `**Toplam: ${passed}/${results.length} (${percent(passed, results.length)})** · yanıt süresi ortanca ${times[Math.floor(times.length / 2)]} ms, en uzun ${times.at(-1)} ms`,
  "",
  "| Kategori | Geçen | Oran |",
  "|---|---|---|",
  ...Array.from(byCategory, ([category, row]) => `| ${category} | ${row.passed}/${row.total} | ${percent(row.passed, row.total)} |`),
  "",
  "## Başarısız vakalar",
  "",
  ...(passed === results.length ? ["Yok."] : results.filter((result) => !result.passed).map((result) => `- **${result.id}** \`${result.text}\`${result.context ? ` (bağlam: ${result.context})` : ""} — ${result.problems.join("; ")}\n  - Cevap: ${result.reply?.replaceAll("\n", " ")}`)),
];

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, `${reportName}-latest.json`), JSON.stringify(results, null, 2));
writeFileSync(join(outDir, `${reportName}-latest.md`), `${lines.join("\n")}\n`);
console.log(lines.join("\n"));
process.exitCode = passed === results.length ? 0 : 1;
