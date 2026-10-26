#!/usr/bin/env node
// Asistan çözümleyici benchmark'ı: cases.json'daki her cümleyi gerçek API'ye
// (POST /api/assistant/resolve) gönderir, beklenenle karşılaştırır, kategori bazında doğruluk
// ve hata listesi yazar. Yalnızca yerel geliştirme ortamında çalışır: şifresiz geliştirici
// girişi (Auth__DevLogin__Enabled=true) ve örnek okul verisi gerekir (README.md).
//
//   node tools/assistant-benchmark/run-resolve.mjs [--cases cases.json] [--api http://localhost:8080] [--out tools/assistant-benchmark/results]

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => (value.startsWith("--") ? [...pairs, [value.slice(2), all[index + 1]]] : pairs), []));
const api = args.api ?? "http://localhost:8080";
const outDir = args.out ?? join(here, "results");
const casesFile = args.cases ?? "cases.json";
const { cases } = JSON.parse(readFileSync(join(here, casesFile), "utf8"));
const reportName = casesFile.replace(/\.json$/, "");

const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

async function login(role) {
  const [roleName, email] = role.split(":");
  const response = await fetch(`${api}/api/dev/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ role: roleName, email: email ?? null }),
  });
  if (!response.ok) throw new Error(`${role} girişi başarısız: ${response.status} ${await response.text()}`);
  return response.headers.getSetCookie().map((cookie) => cookie.split(";")[0]).join("; ");
}

function localToday() {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function offsetDays(dateKey) {
  const [year, month, day] = dateKey.split("-").map(Number);
  return Math.round((new Date(year, month - 1, day) - localToday()) / 86_400_000);
}

function weekday(dateKey) {
  const [year, month, day] = dateKey.split("-").map(Number);
  return DAY_NAMES[new Date(year, month - 1, day).getDay()];
}

// Yanıtın beklenenle karşılaştırılabilir hâli. Gün ifadesi tarih olarak döner; benchmark gün
// adını, bugüne göre farkı ya da tarihin kendisini denetleyebilir.
function describeMove(move) {
  if (!move) return null;
  const side = (prefix, date, time) => ({
    [`${prefix}Date`]: date,
    [`${prefix}Weekday`]: date ? weekday(date) : null,
    [`${prefix}OffsetDays`]: date ? offsetDays(date) : null,
    [`${prefix}Time`]: time ? time.slice(0, 5) : null,
  });
  return {
    ...side("source", move.sourceDate, move.sourceTime),
    ...side("target", move.targetDate, move.targetTime),
    // "haftaya salı": hedef, en yakın salıdan bir hafta sonra (7 günden ileri).
    targetNextWeek: move.targetDate ? offsetDays(move.targetDate) >= 7 : null,
    scope: move.scope,
    shiftMinutes: move.shiftMinutes ?? null,
  };
}

function compare(expected, actual) {
  const problems = [];
  if ("kind" in expected && expected.kind !== actual.kind) problems.push(`niyet ${actual.kind} (beklenen ${expected.kind})`);
  if ("allowed" in expected && expected.allowed !== actual.allowed) problems.push(`izin ${actual.allowed} (beklenen ${expected.allowed})`);
  if ("students" in expected) {
    const got = actual.students.map((student) => student.fullName).sort();
    const want = [...expected.students].sort();
    if (JSON.stringify(got) !== JSON.stringify(want)) problems.push(`öğrenciler [${got.join(", ")}] (beklenen [${want.join(", ")}])`);
  }
  if ("navigation" in expected && expected.navigation !== (actual.navigation?.key ?? null)) {
    problems.push(`ekran ${actual.navigation?.key ?? "yok"} (beklenen ${expected.navigation})`);
  }
  if (expected.move) {
    const move = describeMove(actual.move);
    if (!move) problems.push("gün/saat bilgisi yok");
    else {
      for (const [key, want] of Object.entries(expected.move)) {
        if (move[key] !== want) problems.push(`${key} ${move[key]} (beklenen ${want})`);
      }
    }
  }
  return problems;
}

const sessions = new Map();
const studentIds = new Map();
async function sessionFor(role) {
  if (!sessions.has(role)) sessions.set(role, await login(role));
  return sessions.get(role);
}

const admin = await sessionFor("Admin");
for (const student of await (await fetch(`${api}/api/students`, { headers: { cookie: admin } })).json()) {
  studentIds.set(`${student.firstName} ${student.lastName}`, student.id);
}

const results = [];
for (const testCase of cases) {
  const cookie = await sessionFor(testCase.role);
  const started = performance.now();
  const response = await fetch(`${api}/api/assistant/resolve`, {
    method: "POST",
    headers: { "Content-Type": "application/json", cookie },
    body: JSON.stringify({ text: testCase.text, studentId: testCase.context ? studentIds.get(testCase.context) : null }),
  });
  const elapsedMs = Math.round(performance.now() - started);
  const actual = response.ok ? await response.json() : { kind: `HTTP ${response.status}`, allowed: null, students: [], move: null, navigation: null };
  const problems = compare(testCase.expected, actual);
  results.push({ ...testCase, passed: problems.length === 0, problems, elapsedMs, actual: {
    kind: actual.kind, allowed: actual.allowed, students: actual.students.map((student) => student.fullName), navigation: actual.navigation?.key ?? null, move: describeMove(actual.move),
  } });
}

const byCategory = new Map();
for (const result of results) {
  const row = byCategory.get(result.category) ?? { passed: 0, total: 0 };
  row.total += 1;
  if (result.passed) row.passed += 1;
  byCategory.set(result.category, row);
}
const passed = results.filter((result) => result.passed).length;
const percent = (part, whole) => `%${Math.round((part / whole) * 100)}`;

const lines = [
  `# Asistan çözümleyici benchmark'ı (${casesFile})`,
  "",
  `Tarih: ${new Date().toLocaleString("tr-TR")} · API: ${api}`,
  "",
  `**Toplam: ${passed}/${results.length} (${percent(passed, results.length)})** · ortalama yanıt ${Math.round(results.reduce((sum, result) => sum + result.elapsedMs, 0) / results.length)} ms`,
  "",
  "| Kategori | Geçen | Oran |",
  "|---|---|---|",
  ...Array.from(byCategory, ([category, row]) => `| ${category} | ${row.passed}/${row.total} | ${percent(row.passed, row.total)} |`),
  "",
  "## Başarısız vakalar",
  "",
  ...(passed === results.length ? ["Yok."] : results.filter((result) => !result.passed).map((result) => `- **${result.id}** \`${result.text}\`${result.context ? ` (bağlam: ${result.context})` : ""} — ${result.problems.join("; ")}`)),
];

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, `${reportName}-latest.json`), JSON.stringify(results, null, 2));
writeFileSync(join(outDir, `${reportName}-latest.md`), `${lines.join("\n")}\n`);
console.log(lines.join("\n"));
process.exitCode = passed === results.length ? 0 : 1;
