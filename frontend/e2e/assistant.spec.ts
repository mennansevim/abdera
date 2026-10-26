import { expect, test, type Page, type Request } from "@playwright/test";
import { RELEASES } from "../src/data/releases";

// Abdera Asistanı (docs/10-decisions.md AE). API taklit edilir: cümleyi sunucunun çözdüğü
// varsayılır (POST /api/assistant/resolve - kuralların kendisi backend birim testlerinde),
// burada kartların doğru veriyi çizdiği, yazma işleminin ONAYDAN ÖNCE gitmediği ve onaydan
// sonra mevcut ekranların kullandığı uca gittiği sınanır.

type Handler = (request: Request) => unknown;

const ADMIN = { id: "admin-1", email: "admin@example.com", role: "Admin", mustChangePassword: false, teacherId: null, instrumentIds: [], displayName: null, name: null };
const TEACHER = { id: "teacher-user-1", email: "teacher@example.com", role: "Teacher", mustChangePassword: false, teacherId: "teacher-1", instrumentIds: ["instrument-1"], displayName: null, name: "Ayşe Kaya" };

const STUDENT = {
  studentId: "student-1",
  fullName: "Deniz Arslan",
  status: "Active",
  courses: [{ enrollmentId: "enrollment-1", instrumentName: "Piyano", courseKind: "Individual", teacherName: "Ayşe Kaya" }],
};

const PAGED_EMPTY = { items: [], totalCount: 0, page: 1, pageSize: 1 };

function resolution(kind: string, extra: Record<string, unknown> = {}) {
  return { kind, allowed: true, students: [STUDENT], move: null, navigation: null, ...extra };
}

function dateKey(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function daysFromNow(days: number, hour = 0, minute = 0) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  date.setHours(hour, minute, 0, 0);
  return date;
}

function lesson(id: string, start: Date, studentId = "student-1", studentName = "Deniz Arslan") {
  return {
    id, lessonSeriesId: "series-1", startAt: start.toISOString(), endAt: new Date(start.getTime() + 45 * 60_000).toISOString(),
    status: "Normal", studentId, studentName, teacherId: "teacher-1", teacherName: "Ayşe Kaya", instrumentId: "instrument-1", instrumentName: "Piyano",
  };
}

// Her istek önce testin kendi tablosuna bakar; yoksa ekranı ayakta tutan boş yanıt döner.
// GET dışındaki her istek kaydedilir: yazma işlemi onaydan önce gitmemeli.
async function mockApi(page: Page, me: object, handlers: Record<string, Handler>) {
  const writes: { method: string; path: string; body: unknown }[] = [];
  await page.addInitScript(({ userId, version }) => {
    // "Yenilikler" penceresi testin önüne geçmesin.
    window.localStorage.setItem(`abdera:seen-release:${userId}`, version);
  }, { userId: (me as { id: string }).id, version: RELEASES[0]!.version });

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") writes.push({ method: request.method(), path, body: request.postDataJSON() });

    const handler = handlers[`${request.method()} ${path}`] ?? handlers[path];
    if (handler) return route.fulfill({ json: handler(request) });
    if (path === "/api/auth/me") return route.fulfill({ json: me });
    if (path === "/api/me/notifications") return route.fulfill({ json: { items: [], unreadCount: 0 } });
    if (path === "/api/bank-transactions" || path === "/api/notifications") return route.fulfill({ json: PAGED_EMPTY });
    if (path === "/api/me/pending-lesson-notes") return route.fulfill({ json: { items: [], lookbackDays: 14 } });
    return route.fulfill({ json: [] });
  });
  return writes;
}

async function ask(page: Page, text: string) {
  await page.getByRole("button", { name: "Abdera Asistanı (⌘K)" }).click();
  const dialog = page.getByRole("dialog", { name: "Abdera Asistanı" });
  await dialog.getByLabel("Asistana yaz").fill(text);
  await dialog.getByRole("button", { name: "Gönder", exact: true }).click();
  return dialog;
}

test("progress card shows attendance, piece, last note and the AI summary", async ({ page }) => {
  const lessons = Array.from({ length: 12 }, (_, index) => ({
    startAt: daysFromNow(-7 * (12 - index), 16).toISOString(),
    state: index === 4 ? "Excused" : "Present",
    teacherName: "Ayşe Kaya",
  }));
  await mockApi(page, ADMIN, {
    "POST /api/assistant/resolve": () => resolution("Progress"),
    "/api/students/student-1/analysis": () => ({
      studentId: "student-1", fullName: "Deniz Arslan", birthDate: "2013-04-27", status: "Active", siblingDiscount: false, enrolledSince: "2026-04-01",
      courses: [], guardians: [], availableMakeupCredits: 1, nextMakeupCreditExpiry: null, billingVisible: true, openBalance: 0,
      months: [{ period: "2026-10", lessons, attended: 11, rated: 12, attendanceRate: 92, cancelled: 0, unmarked: 0, averageDifficulty: 1, assignmentCount: 4, assignmentCompleted: 3, assignmentRate: 75, noteCount: 4, billing: null, lastNote: null }],
      pieces: [{ title: "Bach - Minuet in G", composer: null, difficulty: 1, firstPeriod: "2026-09", lastPeriod: "2026-10", finished: false }],
      weekdays: [],
    }),
    "/api/students/student-1/progress": () => ({
      studentId: "student-1", studentName: "Deniz Arslan", entryCount: 1, lastEntryAt: daysFromNow(-5).toISOString(),
      entries: [{ id: "note-1", lessonId: "lesson-0", teacherId: "teacher-1", instrumentId: "instrument-1", lessonStartAt: daysFromNow(-5, 16).toISOString(), createdAt: daysFromNow(-5).toISOString(), teacherName: "Ayşe Kaya", instrumentName: "Piyano", practiced: null, note: "Sol el pasajı oturdu.", homework: null, nextGoal: "70 bpm", pieceTitle: null, pieceDifficulty: null, pieceComposer: null, pieceStatus: null, pieceTargetDate: null, pieceResourceUrl: null, pieceResourceVisibleToGuardian: false, parentComment: null, parentCommentApprovedAt: null }],
      skillAssessments: [
        { id: "s1", skillDefinitionId: "d1", skillCode: "RHYTHM", skillLabel: "Ritim", teacherId: "teacher-1", teacherName: "Ayşe Kaya", lessonId: null, score: 4, note: null, assessedAt: daysFromNow(-3).toISOString() },
        { id: "s2", skillDefinitionId: "d1", skillCode: "RHYTHM", skillLabel: "Ritim", teacherId: "teacher-1", teacherName: "Ayşe Kaya", lessonId: null, score: 3, note: null, assessedAt: daysFromNow(-60).toISOString() },
      ],
    }),
    "/api/students/student-1/progress-summary": () => ({ status: "Ready", summary: "Devamı düzenli, ritimde belirgin ilerleme var.", generatedAt: daysFromNow(-10).toISOString(), sourceNoteCount: 11, noteCount: 12, minimumNotes: 4, nextRefreshOn: null, isStale: false }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz Arslan'ın gelişimini getir");

  await expect(dialog.getByText("%92")).toBeVisible();
  await expect(dialog.getByText("3/4")).toBeVisible();
  await expect(dialog.getByText("Bach - Minuet in G")).toBeVisible();
  await expect(dialog.getByText(/Sol el pasajı oturdu\. Sonraki hedef: 70 bpm/)).toBeVisible();
  await expect(dialog.getByText("4 ↑")).toBeVisible();
  await expect(dialog.getByText("Devamı düzenli, ritimde belirgin ilerleme var.")).toBeVisible();
  await expect(dialog.getByRole("link", { name: "Öğrenci analizi" })).toHaveAttribute("href", "/dashboard/students/analysis?studentId=student-1");
});

test("two matching students are offered as choices before any card", async ({ page }) => {
  const other = { ...STUDENT, studentId: "student-2", fullName: "Deniz Kaya", courses: [{ ...STUDENT.courses[0]!, enrollmentId: "enrollment-2", instrumentName: "Gitar" }] };
  await mockApi(page, ADMIN, {
    "POST /api/assistant/resolve": () => ({ ...resolution("Lessons"), students: [STUDENT, other] }),
    "/api/students/student-2/lesson-series": () => [],
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz'in dersleri");
  await expect(dialog.getByText("Birden fazla öğrenci eşleşti. Hangisi?")).toBeVisible();
  await dialog.getByRole("button", { name: "Deniz Kaya · Gitar" }).click();
  await expect(dialog.getByText("Haftalık ders programı yok.")).toBeVisible();
});

test("dues list groups open dues and sends a reminder only after approval", async ({ page }) => {
  const due = (id: string, studentName: string, dueDate: Date, status: string, period = "D004") => ({
    id, enrollmentId: `enrollment-${id}`, period, amount: 2400, currency: "TRY", dueDate: dateKey(dueDate), status, totalPaid: 0, payments: [],
    baseAmount: 2400, discountPercent: 0, discountReason: null, prepayPlanId: null,
    studentId: `student-${id}`, studentName, teacherId: "teacher-1", teacherName: "Ayşe Kaya", instrumentId: "instrument-1", instrumentName: "Piyano", courseKind: "Individual",
  });
  const writes = await mockApi(page, ADMIN, {
    "POST /api/assistant/resolve": () => ({ ...resolution("Dues"), students: [] }),
    "/api/billing/dues": () => [
      due("r1", "Gecikmiş Öğrenci", daysFromNow(-5), "Overdue"),
      due("r2", "Yaklaşan Öğrenci", daysFromNow(3), "Unpaid", "2026-11"),
      due("r3", "Ödemiş Öğrenci", daysFromNow(-2), "Paid"),
      due("r4", "Uzak Öğrenci", daysFromNow(20), "Unpaid"),
    ],
    "POST /api/receivables/r1/send-reminder": () => ({ scheduled: true }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Aidat günü gelenleri listele");

  await expect(dialog.getByText("Gecikmiş Öğrenci")).toBeVisible();
  await expect(dialog.getByText("Yaklaşan Öğrenci")).toBeVisible();
  await expect(dialog.getByText("Ödemiş Öğrenci")).toHaveCount(0);
  await expect(dialog.getByText("Uzak Öğrenci")).toHaveCount(0);
  await expect(dialog.getByText("Ödenmedi", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Yaklaşıyor", { exact: true })).toBeVisible();

  // Asistan para almaz: "Ödeme al" Aidatlar'daki tek ödeme penceresini o kayıtla açar.
  await expect(dialog.getByRole("link", { name: "Ödeme al" }).first()).toHaveAttribute("href", /\/dashboard\/billing\?collect=enrollment-r1&period=D004&name=/);

  await dialog.getByRole("button", { name: "Hatırlatma gönder" }).first().click();
  await expect(dialog.getByText("Gecikmiş Öğrenci velisine hatırlatma gönderilsin mi?")).toBeVisible();
  expect(writes.filter((write) => write.path.includes("send-reminder"))).toEqual([]);

  await dialog.getByRole("button", { name: "Gönder", exact: true }).first().click();
  await expect(dialog.getByText("1 hatırlatma sıraya alındı.")).toBeVisible();
  expect(writes.filter((write) => write.path.includes("send-reminder")).map((write) => write.path)).toEqual(["/api/receivables/r1/send-reminder"]);
});

function scheduleHandlers(sourceStart: Date, extraLessons: ReturnType<typeof lesson>[] = []): Record<string, Handler> {
  const dayKeys = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
  return {
    "/api/students/student-1/lesson-series": () => [{
      id: "series-1", enrollmentId: "enrollment-1", teacherId: "teacher-1", teacherName: "Ayşe Kaya", instrumentId: "instrument-1", instrumentName: "Piyano",
      dayOfWeek: dayKeys[sourceStart.getDay()], startTime: "16:00:00", durationMinutes: 45, effectiveFrom: "2026-01-01", effectiveUntil: null, status: "Active",
    }],
    "/api/calendar": () => [lesson("lesson-1", sourceStart), ...extraLessons],
    "/api/teachers/teacher-1/availability": () => [],
    "/api/school-calendar-days": () => [],
  };
}

test("a one-off move is checked, confirmed and sent through the calendar's change request", async ({ page }) => {
  const sourceStart = daysFromNow(2, 16);
  const target = daysFromNow(2, 17);
  const writes = await mockApi(page, ADMIN, {
    ...scheduleHandlers(sourceStart),
    "POST /api/assistant/resolve": () => resolution("Reschedule", { move: { sourceDate: null, sourceTime: null, targetDate: null, targetTime: "17:00:00", scope: "Once" } }),
    "POST /api/lessons/lesson-1/change-requests": () => ({ id: "request-1" }),
    "POST /api/change-requests/request-1/approve": () => ({}),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz Arslan'ın dersini bu seferlik 17:00'ye al");

  await expect(dialog.getByText("Bu ders taşınacak")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Yalnız bu ders" })).toHaveAttribute("aria-pressed", "true");
  expect(writes).toEqual([expect.objectContaining({ path: "/api/assistant/resolve" })]);

  await dialog.getByRole("button", { name: "Onayla ve taşı" }).click();
  await expect(dialog.getByText(/Deniz Arslan dersi .* 17:00 saatine taşındı\./)).toBeVisible();
  const request = writes.find((write) => write.path === "/api/lessons/lesson-1/change-requests");
  expect(request?.body).toEqual({ proposedStartAt: target.toISOString(), proposedEndAt: new Date(target.getTime() + 45 * 60_000).toISOString() });
  expect(writes.map((write) => write.path)).toContain("/api/change-requests/request-1/approve");
});

test("a conflicting time is explained and free times are suggested", async ({ page }) => {
  const sourceStart = daysFromNow(2, 16);
  const busy = lesson("lesson-busy", daysFromNow(2, 17), "student-9", "Başka Öğrenci");
  const writes = await mockApi(page, ADMIN, {
    ...scheduleHandlers(sourceStart, [busy]),
    "POST /api/assistant/resolve": () => resolution("Reschedule", { move: { sourceDate: null, sourceTime: null, targetDate: null, targetTime: "17:00:00", scope: "Once" } }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz Arslan'ın dersini 17:00'ye al");

  await expect(dialog.getByText(/öğretmenin veya öğrencinin başka bir dersiyle çakışıyor/)).toBeVisible();
  await expect(dialog.getByText("Uygun saatler:")).toBeVisible();
  await expect(dialog.getByText("Bu ders taşınacak")).toHaveCount(0);

  await dialog.getByRole("button", { name: /\d{2}:\d{2}$/ }).first().click();
  await expect(dialog.getByText("Bu ders taşınacak")).toBeVisible();
  expect(writes.map((write) => write.path)).toEqual(["/api/assistant/resolve"]);
});

test("a makeup lesson uses the earliest expiring credit after approval", async ({ page }) => {
  const sourceStart = daysFromNow(-2, 15);
  const writes = await mockApi(page, ADMIN, {
    "POST /api/assistant/resolve": () => resolution("Makeup"),
    "/api/students/student-1/makeup-credits": () => [
      { id: "credit-late", studentId: "student-1", sourceLessonId: "source-2", earnedReason: "Excused", earnedAt: sourceStart.toISOString(), expiresAt: daysFromNow(30).toISOString(), status: "Available", usedLessonId: null, sourceLessonStartAt: sourceStart.toISOString() },
      { id: "credit-early", studentId: "student-1", sourceLessonId: "source-1", earnedReason: "Excused", earnedAt: sourceStart.toISOString(), expiresAt: daysFromNow(15).toISOString(), status: "Available", usedLessonId: null, sourceLessonStartAt: sourceStart.toISOString() },
      { id: "credit-used", studentId: "student-1", sourceLessonId: "source-3", earnedReason: "Excused", earnedAt: sourceStart.toISOString(), expiresAt: daysFromNow(10).toISOString(), status: "Used", usedLessonId: "x", sourceLessonStartAt: sourceStart.toISOString() },
    ],
    "/api/lessons/source-1": () => lesson("source-1", sourceStart),
    "/api/calendar": () => [],
    "/api/teachers/teacher-1/availability": () => [],
    "/api/school-calendar-days": () => [],
    "POST /api/makeup-credits/credit-early/use": () => ({ creditId: "credit-early", newLessonId: "makeup-1" }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz'e telafi oluştur");

  await expect(dialog.getByText("süresi en erken dolan", { exact: false })).toBeVisible();
  const slot = dialog.getByRole("button", { name: /\d{2}:\d{2}$/ }).first();
  await slot.click();
  await expect(dialog.getByText("Telafi dersi açılacak")).toBeVisible();
  expect(writes.map((write) => write.path)).toEqual(["/api/assistant/resolve"]);

  await dialog.getByRole("button", { name: "Telafiyi aç" }).click();
  await expect(dialog.getByText(/Deniz Arslan için telafi dersi açıldı/)).toBeVisible();
  const use = writes.find((write) => write.path === "/api/makeup-credits/credit-early/use");
  expect(use?.body).toMatchObject({ teacherId: "teacher-1", instrumentId: "instrument-1", durationMinutes: 45 });
});

test("navigation questions point to the screen and unknown questions get help", async ({ page }) => {
  let call = 0;
  await mockApi(page, ADMIN, {
    "POST /api/assistant/resolve": () => (++call === 1
      ? { kind: "Navigate", allowed: true, students: [], move: null, navigation: { key: "attendance", label: "Yoklama", path: "/dashboard/attendance" } }
      : { kind: "Unknown", allowed: true, students: [], move: null, navigation: null }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Yoklamayı nereden alırım?");
  await expect(dialog.getByText("Mazeretli seçilince öğrenciye telafi hakkı açılır.")).toBeVisible();
  await expect(dialog.getByRole("link", { name: "Yoklama ekranına git" })).toHaveAttribute("href", "/dashboard/attendance");

  await dialog.getByLabel("Asistana yaz").fill("hava nasıl");
  await dialog.getByRole("button", { name: "Gönder", exact: true }).click();
  await expect(dialog.getByText(/Bunu anlayamadım\./)).toBeVisible();
});

test("teachers do not see the assistant yet", async ({ page }) => {
  const writes = await mockApi(page, TEACHER, {});

  await page.goto("/dashboard/releases");
  await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Abdera Asistanı (⌘K)" })).toHaveCount(0);
  await page.keyboard.press("ControlOrMeta+k");
  await expect(page.getByRole("dialog", { name: "Abdera Asistanı" })).toHaveCount(0);
  expect(writes.filter((write) => write.path === "/api/assistant/resolve")).toEqual([]);
});

test("the billing link from the assistant opens the payment window for that due", async ({ page }) => {
  await mockApi(page, ADMIN, {
    "/api/enrollments/enrollment-r1/collection-preview": () => ({
      enrollmentId: "enrollment-r1", studentName: "Gecikmiş Öğrenci", instrumentName: "Piyano", courseKind: "Individual", currency: "TRY",
      defaults: { sibling: false, multiCourse: false, manualPercent: null, manualReason: null, siblingPercent: 10, multiCoursePercent: 10 },
      prepayPercent: 0, rows: [], baseTotal: 2400, total: 2400, blockers: [],
    }),
  });

  await page.goto("/dashboard/billing?collect=enrollment-r1&period=D004&name=Gecikmi%C5%9F%20%C3%96%C4%9Frenci");
  const payment = page.getByRole("dialog", { name: "Ödeme al" });
  await expect(payment).toBeVisible();
  await expect(payment.getByText("Gecikmiş Öğrenci")).toBeVisible();
});
