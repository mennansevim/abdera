import { expect, test, type Page, type Request } from "@playwright/test";
import { RELEASES } from "../src/data/releases";

// Abdera Asistanı (docs/10-decisions.md AE, AE9). API taklit edilir: sohbet ucunun (POST
// /api/assistant/chat, dil modeli) cevabı ve açtığı kart verilir - modelin kendisi
// tools/assistant-benchmark ile ölçülür. Burada cevabın çizildiği, geçmişin geri gittiği,
// kartların doğru veriyi gösterdiği, yazma işleminin ONAYDAN ÖNCE gitmediği ve onaydan sonra
// mevcut ekranların kullandığı uca gittiği sınanır.

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

// Sohbet ucunun cevabı: kısa metin + modelin açtığı tek kart.
function chat(kind: string, card: Record<string, unknown> = {}, reply = "Kartı açtım, onaylarsan uygulanır.") {
  return { reply, cards: [{ kind, student: STUDENT, move: null, navigation: null, ...card }], history: [] };
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
async function mockApi(page: Page, me: object, handlers: Record<string, Handler>, { intro = false } = {}) {
  const writes: { method: string; path: string; body: unknown }[] = [];
  await page.addInitScript(({ userId, version, intro }) => {
    // "Yenilikler" penceresi ve asistanın ilk açılış tanıtımı testin önüne geçmesin.
    window.localStorage.setItem(`abdera:seen-release:${userId}`, version);
    if (!intro) window.localStorage.setItem("abdera.assistant.intro.v1", "1");
  }, { userId: (me as { id: string }).id, version: RELEASES[0]!.version, intro });

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") writes.push({ method: request.method(), path, body: request.postDataJSON() });

    const handler = handlers[`${request.method()} ${path}`] ?? handlers[path];
    if (handler) {
      const result = handler(request) as { status?: number; body?: unknown } & Record<string, unknown>;
      // { status, body } biçimi hata yanıtı içindir (ör. dil modeli kapalı → 503).
      return typeof result?.status === "number" && "body" in result ? route.fulfill({ status: result.status, json: result.body }) : route.fulfill({ json: result });
    }
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
    "POST /api/assistant/chat": () => chat("progress"),
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

test("the reply is rendered, the masked history goes back with the next message and errors are shown", async ({ page }) => {
  const bodies: unknown[] = [];
  const history = [{ role: "user", content: "Arslan'ın dersleri" }, { role: "assistant", content: "İki öğrenci var: Ö-AB12 ve Ö-CD34. Hangisi?" }];
  let call = 0;
  await mockApi(page, ADMIN, {
    "POST /api/assistant/chat": (request) => {
      bodies.push(request.postDataJSON());
      call += 1;
      if (call === 1) return { reply: "İki öğrenci var:\n- **Deniz Arslan** (piyano, perşembe)\n- Lara Arslan (piyano, pazartesi)\nHangisini soruyorsun?", cards: [], history };
      if (call === 2) return { reply: "Lara Arslan'ın dersi her pazartesi 16:00.", cards: [], history: [] };
      return { status: 503, body: { title: "Asistan yanıt veremedi", detail: "Dil modeli zamanında yanıt vermedi." } };
    },
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Arslan'ın dersleri");
  await expect(dialog.getByRole("listitem").filter({ hasText: "Lara Arslan (piyano, pazartesi)" })).toBeVisible();
  await expect(dialog.locator("strong", { hasText: "Deniz Arslan" })).toBeVisible();

  await dialog.getByLabel("Asistana yaz").fill("Lara olanın");
  await dialog.getByRole("button", { name: "Gönder", exact: true }).click();
  await expect(dialog.getByText("Lara Arslan'ın dersi her pazartesi 16:00.")).toBeVisible();
  // Sunucunun döndürdüğü maskeli geçmiş bir sonraki mesajla aynen geri gider.
  expect(bodies[0]).toMatchObject({ message: "Arslan'ın dersleri", history: [] });
  expect(bodies[1]).toMatchObject({ message: "Lara olanın", history });

  await dialog.getByLabel("Asistana yaz").fill("Bir şey daha");
  await dialog.getByRole("button", { name: "Gönder", exact: true }).click();
  await expect(dialog.getByText("Dil modeli zamanında yanıt vermedi.")).toBeVisible();
});

test("dues list groups open dues and sends a reminder only after approval", async ({ page }) => {
  const due = (id: string, studentName: string, dueDate: Date, status: string, period = "D004") => ({
    id, enrollmentId: `enrollment-${id}`, period, amount: 2400, currency: "TRY", dueDate: dateKey(dueDate), status, totalPaid: 0, payments: [],
    baseAmount: 2400, discountPercent: 0, discountReason: null, prepayPlanId: null,
    studentId: `student-${id}`, studentName, teacherId: "teacher-1", teacherName: "Ayşe Kaya", instrumentId: "instrument-1", instrumentName: "Piyano", courseKind: "Individual",
  });
  const writes = await mockApi(page, ADMIN, {
    "POST /api/assistant/chat": () => chat("dues", { student: null }),
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
    "POST /api/assistant/chat": () => chat("reschedule", { move: { sourceDate: null, sourceTime: null, targetDate: null, targetTime: "17:00:00", scope: "Once" } }),
    "POST /api/lessons/lesson-1/change-requests": () => ({ id: "request-1" }),
    "POST /api/change-requests/request-1/approve": () => ({}),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz Arslan'ın dersini bu seferlik 17:00'ye al");

  await expect(dialog.getByText("Bu ders taşınacak")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Yalnız bu ders" })).toHaveAttribute("aria-pressed", "true");
  expect(writes).toEqual([expect.objectContaining({ path: "/api/assistant/chat" })]);

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
    "POST /api/assistant/chat": () => chat("reschedule", { move: { sourceDate: null, sourceTime: null, targetDate: null, targetTime: "17:00:00", scope: "Once" } }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz Arslan'ın dersini 17:00'ye al");

  await expect(dialog.getByText(/öğretmenin veya öğrencinin başka bir dersiyle çakışıyor/)).toBeVisible();
  await expect(dialog.getByText("Uygun saatler:")).toBeVisible();
  await expect(dialog.getByText("Bu ders taşınacak")).toHaveCount(0);

  await dialog.getByRole("button", { name: /\d{2}:\d{2}$/ }).first().click();
  await expect(dialog.getByText("Bu ders taşınacak")).toBeVisible();
  expect(writes.map((write) => write.path)).toEqual(["/api/assistant/chat"]);
});

test("a makeup lesson uses the earliest expiring credit after approval", async ({ page }) => {
  const sourceStart = daysFromNow(-2, 15);
  const writes = await mockApi(page, ADMIN, {
    "POST /api/assistant/chat": () => chat("makeup"),
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
  expect(writes.map((write) => write.path)).toEqual(["/api/assistant/chat"]);

  await dialog.getByRole("button", { name: "Telafiyi aç" }).click();
  await expect(dialog.getByText(/Deniz Arslan için telafi dersi açıldı/)).toBeVisible();
  const use = writes.find((write) => write.path === "/api/makeup-credits/credit-early/use");
  expect(use?.body).toMatchObject({ teacherId: "teacher-1", instrumentId: "instrument-1", durationMinutes: 45 });
});

test("navigation questions point to the screen and unrelated questions get a short answer", async ({ page }) => {
  let call = 0;
  await mockApi(page, ADMIN, {
    "POST /api/assistant/chat": () => (++call === 1
      ? { reply: "Yoklamayı Yoklama ekranından alırsın.", cards: [{ kind: "navigate", student: null, move: null, navigation: { key: "attendance", label: "Yoklama", path: "/dashboard/attendance" } }], history: [] }
      : { reply: "Buna bakamam; ders, öğrenci ve aidat işlerinde yardımcı olurum.", cards: [], history: [] }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Yoklamayı nereden alırım?");
  await expect(dialog.getByText("Yoklamayı Yoklama ekranından alırsın.")).toBeVisible();
  await expect(dialog.getByText("Mazeretli seçilince öğrenciye telafi hakkı açılır.")).toBeVisible();
  await expect(dialog.getByRole("link", { name: "Yoklama ekranına git" })).toHaveAttribute("href", "/dashboard/attendance");

  await dialog.getByLabel("Asistana yaz").fill("hava nasıl");
  await dialog.getByRole("button", { name: "Gönder", exact: true }).click();
  await expect(dialog.getByText(/Buna bakamam/)).toBeVisible();
});

test("teachers do not see the assistant yet", async ({ page }) => {
  const writes = await mockApi(page, TEACHER, {});

  await page.goto("/dashboard/releases");
  await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Abdera Asistanı (⌘K)" })).toHaveCount(0);
  await page.keyboard.press("ControlOrMeta+k");
  await expect(page.getByRole("dialog", { name: "Abdera Asistanı" })).toHaveCount(0);
  expect(writes.filter((write) => write.path === "/api/assistant/chat")).toEqual([]);
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

test("typing a name offers students and the picked student goes with the message", async ({ page }) => {
  let body: { message?: string; studentId?: string | null } = {};
  const overview = (id: string, firstName: string, lastName: string, instrumentName: string) => ({
    student: { id, firstName, lastName, birthDate: "2015-01-01", status: "Active", siblingDiscount: false },
    instruments: [{ instrumentId: `i-${id}`, instrumentName }],
    enrolledSince: "2026-01-01",
    recentAttendance: [],
  });
  await mockApi(page, ADMIN, {
    "/api/students/overview": () => [overview("student-1", "Deniz", "Arslan", "Piyano"), overview("student-2", "Lara", "Arslan", "Keman")],
    "POST /api/assistant/chat": (request) => {
      body = request.postDataJSON();
      return { reply: "Tamam.", cards: [], history: [] };
    },
  });

  await page.goto("/dashboard/releases");
  await page.getByRole("button", { name: "Abdera Asistanı (⌘K)" }).click();
  const dialog = page.getByRole("dialog", { name: "Abdera Asistanı" });
  const input = dialog.getByLabel("Asistana yaz");
  await input.pressSequentially("arsl");
  const options = dialog.getByRole("option");
  await expect(options).toHaveCount(2);
  await expect(options.first()).toContainText("Deniz Arslan");

  // Kendiliğinden açılan listede ok tuşu + Enter seçer; mesaj gitmez.
  await input.press("ArrowDown");
  await input.press("Enter");
  await expect(input).toHaveValue("Lara Arslan ");
  await expect(options).toHaveCount(0);

  await input.pressSequentially("dersini göster");
  await input.press("Enter");
  await expect(dialog.getByText("Tamam.")).toBeVisible();
  expect(body).toMatchObject({ message: "Lara Arslan dersini göster", studentId: "student-2" });
});

test("a makeup time asked in the sentence is checked and offered for approval", async ({ page }) => {
  const sourceStart = daysFromNow(-2, 15);
  const target = daysFromNow(3, 17);
  const writes = await mockApi(page, ADMIN, {
    "POST /api/assistant/chat": () => chat("makeup", { move: { sourceDate: null, sourceTime: null, targetDate: dateKey(target), targetTime: "17:00:00", scope: "Unspecified", shiftMinutes: null, targetWeekday: null, targetNextWeek: false } }),
    "/api/students/student-1/makeup-credits": () => [
      { id: "credit-1", studentId: "student-1", sourceLessonId: "source-1", earnedReason: "Excused", earnedAt: sourceStart.toISOString(), expiresAt: daysFromNow(20).toISOString(), status: "Available", usedLessonId: null, sourceLessonStartAt: sourceStart.toISOString() },
    ],
    "/api/lessons/source-1": () => lesson("source-1", sourceStart),
    "/api/calendar": () => [],
    "/api/teachers/teacher-1/availability": () => [],
    "/api/school-calendar-days": () => [],
    "POST /api/makeup-credits/credit-1/use": () => ({ creditId: "credit-1", newLessonId: "makeup-1" }),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz'e 17:00'de telafi aç");

  await expect(dialog.getByText("Telafi dersi açılacak")).toBeVisible();
  await expect(dialog.getByText("Öğretmenin müsait olduğu saat")).toBeVisible();
  expect(writes.map((write) => write.path)).toEqual(["/api/assistant/chat"]);
  await dialog.getByRole("button", { name: "Onayla, telafiyi aç" }).click();
  await expect(dialog.getByText(/Deniz Arslan için telafi dersi açıldı/)).toBeVisible();
  expect(writes.find((write) => write.path === "/api/makeup-credits/credit-1/use")?.body).toMatchObject({ startAt: target.toISOString() });
});

test("a weekly move that would skip a week says so and can start in the same week", async ({ page }) => {
  const today = new Date();
  const nextMonday = (8 - today.getDay()) % 7 || 7;
  const sourceStart = daysFromNow(nextMonday + 3, 16);
  const sameWeekTuesday = daysFromNow(nextMonday + 1, 16);
  const writes = await mockApi(page, ADMIN, {
    ...scheduleHandlers(sourceStart),
    "POST /api/assistant/chat": () => chat("reschedule", { move: { sourceDate: dateKey(sourceStart), sourceTime: null, targetDate: dateKey(sameWeekTuesday), targetTime: "16:00:00", scope: "Recurring", shiftMinutes: null, targetWeekday: "Tuesday", targetNextWeek: false } }),
    "POST /api/lesson-series/series-1/reschedule": () => ({}),
  });

  await page.goto("/dashboard/releases");
  const dialog = await ask(page, "Deniz'in perşembe dersini bundan sonra salıya al");

  await expect(dialog.getByText(/o hafta ders olmayacak/)).toBeVisible();
  await dialog.getByRole("button", { name: /Bu hafta .* başlasın/ }).click();
  await expect(dialog.getByText(/o hafta ders olmayacak/)).toHaveCount(0);
  await dialog.getByRole("button", { name: "Onayla, programı değiştir" }).click();
  await expect(dialog.getByText(/programı her salı 16:00 oldu/)).toBeVisible();
  expect(writes.find((write) => write.path === "/api/lesson-series/series-1/reschedule")?.body).toMatchObject({ dayOfWeek: "Tuesday", startTime: "16:00:00", effectiveFrom: dateKey(sameWeekTuesday) });
});

test("the first open shows a short intro whose examples use a real student and it is not shown again", async ({ page }) => {
  let body: { message?: string; studentId?: string | null } = {};
  await mockApi(page, ADMIN, {
    "/api/students/overview": () => [{
      student: { id: "student-1", firstName: "Deniz", lastName: "Arslan", birthDate: "2015-01-01", status: "Active", siblingDiscount: false },
      instruments: [{ instrumentId: "i-1", instrumentName: "Piyano" }],
      enrolledSince: "2026-01-01",
      recentAttendance: [],
    }],
    "POST /api/assistant/chat": (request) => {
      body = request.postDataJSON();
      return { reply: "Deniz düzenli geliyor.", cards: [], history: [] };
    },
  }, { intro: true });

  await page.goto("/dashboard/releases");
  await page.getByRole("button", { name: "Abdera Asistanı (⌘K)" }).click();
  const dialog = page.getByRole("dialog", { name: "Abdera Asistanı" });
  await expect(dialog.getByText("Kısa tanıtım · 1/3")).toBeVisible();
  await expect(dialog.getByRole("heading", { name: "Günlük dilde sor" })).toBeVisible();
  await dialog.getByRole("button", { name: "İleri" }).click();
  await expect(dialog.getByRole("heading", { name: "Adı yaz, ben tamamlayayım" })).toBeVisible();
  await dialog.getByRole("button", { name: "İleri" }).click();
  await expect(dialog.getByRole("heading", { name: "Önce kontrol, sonra senin onayın" })).toBeVisible();

  // Örnek okulun gerçek öğrencisiyle kurulur ve kimliği bağlam olarak gider.
  await dialog.getByRole("button", { name: "Deniz Arslan nasıl gidiyor?" }).click();
  await expect(dialog.getByText("Deniz düzenli geliyor.")).toBeVisible();
  expect(body).toMatchObject({ message: "Deniz Arslan nasıl gidiyor?", studentId: "student-1" });
  await expect(dialog.getByText("Kısa tanıtım · 1/3")).toHaveCount(0);

  // Görüldü: yeniden açılışta doğrudan sohbet; "Nasıl kullanılır" tanıtımı geri getirir.
  await page.reload();
  await page.getByRole("button", { name: "Abdera Asistanı (⌘K)" }).click();
  await expect(dialog.getByText("ne yapalım?")).toBeVisible();
  await dialog.getByRole("button", { name: "Nasıl kullanılır" }).click();
  await expect(dialog.getByText("Kısa tanıtım · 1/3")).toBeVisible();
  await dialog.getByRole("button", { name: "Geç" }).click();
  await expect(dialog.getByText("ne yapalım?")).toBeVisible();
});
