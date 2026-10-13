import { expect, test, type Page } from "@playwright/test";

// #22: telafi penceresi yalnızca önerilen saatleri gösteriyordu (gün başına en fazla iki,
// 16-19 arası öne çıkarılmış); öğretmen boşken istenen saat seçilemiyor, ilk öneri
// yerleşiyordu. Kullanıcı artık kendi gün/saatini girebilir, o saat çakışma ve müsaitlikle
// denetlenir.

function localDateInput(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

async function mockMakeupApi(page: Page, { lessonStart, busyStart }: { lessonStart: Date; busyStart: Date }) {
  const now = new Date();
  const expiresAt = new Date(now);
  expiresAt.setDate(expiresAt.getDate() + 60);
  const posted: { startAt?: string } = {};
  const lesson = (id: string, start: Date, status: string) => ({
    id, lessonSeriesId: "series-1", startAt: start.toISOString(), endAt: new Date(start.getTime() + 45 * 60_000).toISOString(),
    status, studentId: id === "lesson-1" ? "student-1" : "student-2", studentName: id === "lesson-1" ? "Telafi Öğrencisi" : "Başka Öğrenci",
    teacherId: "teacher-1", teacherName: "Ayşe Öğretmen", instrumentId: "instrument-1", instrumentName: "Piyano", rsvpResponse: "Unknown",
  });

  await page.route("**/api/**", async (route) => {
    const pathname = new URL(route.request().url()).pathname;
    const json = (body: unknown) => route.fulfill({ json: body });
    if (pathname === "/api/auth/me") return json({ id: "user-1", email: "teacher@example.com", role: "Teacher", mustChangePassword: false, teacherId: "teacher-1", instrumentIds: ["instrument-1"] });
    if (pathname === "/api/me/notifications") return json({ items: [], unreadCount: 0 });
    if (pathname === "/api/calendar") return json([lesson("lesson-1", lessonStart, "Cancelled"), lesson("lesson-busy", busyStart, "Normal")]);
    if (pathname === "/api/students") return json([{ id: "student-1", firstName: "Telafi", lastName: "Öğrencisi", birthDate: "2014-01-01", status: "Active" }]);
    if (pathname === "/api/teachers") return json([{ id: "teacher-1", firstName: "Ayşe", lastName: "Öğretmen", status: "Active", instrumentIds: ["instrument-1"] }]);
    if (pathname === "/api/instruments") return json([{ id: "instrument-1", code: "PIANO", name: "Piyano" }]);
    if (pathname === "/api/students/student-1/enrollments") return json([{ id: "enrollment-1", studentId: "student-1", teacherId: "teacher-1", instrumentId: "instrument-1", status: "Active", startedAt: "2026-01-01", endedAt: null }]);
    if (pathname === "/api/students/student-1/makeup-credits") return json([{ id: "credit-1", studentId: "student-1", sourceLessonId: "lesson-1", earnedReason: "SchoolCancelled", earnedAt: now.toISOString(), expiresAt: expiresAt.toISOString(), status: "Available", usedLessonId: null, sourceLessonStartAt: lessonStart.toISOString() }]);
    if (pathname === "/api/makeup-credits/credit-1/use" && route.request().method() === "POST") {
      posted.startAt = route.request().postDataJSON().startAt;
      return json({ creditId: "credit-1", newLessonId: "makeup-1" });
    }
    if (pathname === "/api/teachers/teacher-1/availability") return json([]);
    return json([]);
  });
  return posted;
}

test("a makeup lesson can be placed at a free time the user picks", async ({ page }) => {
  const lessonStart = new Date();
  lessonStart.setHours(15, 0, 0, 0);
  const target = new Date(lessonStart);
  target.setDate(target.getDate() + 2);
  const busyStart = new Date(target);
  busyStart.setHours(16, 0, 0, 0);
  const posted = await mockMakeupApi(page, { lessonStart, busyStart });

  await page.goto("/dashboard/calendar");
  await page.getByRole("button", { name: "Telafi planla", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Telafi planla" });
  await dialog.getByLabel("Öğrenci").selectOption("student-1");
  await expect(dialog.getByText("Müsait saatler", { exact: true })).toBeVisible();

  await dialog.getByLabel("Telafi günü").fill(localDateInput(target));
  // Öğretmenin o gün 16:00'da başka dersi var: çakışan saat seçilemez.
  await dialog.getByLabel("Telafi saati").fill("16:15");
  await expect(dialog.getByRole("alert").filter({ hasText: "çakışıyor" })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Telafi dersini yerleştir" })).toBeDisabled();

  await dialog.getByLabel("Telafi saati").fill("15:00");
  await expect(dialog.getByRole("alert").filter({ hasText: "çakışıyor" })).toHaveCount(0);
  await dialog.getByRole("button", { name: "Telafi dersini yerleştir" }).click();
  await expect(dialog).toBeHidden();

  const expected = new Date(target);
  expected.setHours(15, 0, 0, 0);
  expect(posted.startAt).toBe(expected.toISOString());
});
