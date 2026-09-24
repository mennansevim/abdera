import { expect, test, type Locator, type Page } from "@playwright/test";

async function expectCentered(page: Page, dialog: Locator) {
  const viewport = page.viewportSize();
  const bounds = await dialog.boundingBox();
  expect(viewport).not.toBeNull();
  expect(bounds).not.toBeNull();
  expect(Math.abs(bounds!.x + bounds!.width / 2 - viewport!.width / 2)).toBeLessThanOrEqual(2);
  expect(Math.abs(bounds!.y + bounds!.height / 2 - viewport!.height / 2)).toBeLessThanOrEqual(2);
}

test("quick-add dialog stays centered regardless of the double-click position", async ({ page }) => {
  await page.route("**/api/**", async (route) => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname === "/api/auth/me") {
      await route.fulfill({
        json: {
          id: "admin-1",
          email: "admin@example.com",
          role: "Admin",
          mustChangePassword: false,
          teacherId: null,
          instrumentIds: [],
        },
      });
      return;
    }
    if (pathname === "/api/bank-transactions" || pathname === "/api/notifications") {
      await route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 50 } });
      return;
    }
    await route.fulfill({ json: [] });
  });

  await page.goto("/dashboard/calendar");
  const dayColumn = page.locator('[data-testid^="calendar-day-"]').last();
  await expect(dayColumn).toBeVisible();
  const dayBounds = await dayColumn.boundingBox();
  expect(dayBounds).not.toBeNull();

  const dialog = page.getByRole("dialog", { name: "Yeni ders oluştur" });
  await dayColumn.dispatchEvent("dblclick", {
    clientX: dayBounds!.x + 8,
    clientY: dayBounds!.y + dayBounds!.height * 0.1,
    bubbles: true,
  });
  await expect(dialog).toBeVisible();
  await expectCentered(page, dialog);
  await dialog.getByRole("button", { name: "Kapat", exact: true }).click();
  await expect(dialog).toBeHidden();

  await dayColumn.dispatchEvent("dblclick", {
    clientX: dayBounds!.x + dayBounds!.width - 8,
    clientY: dayBounds!.y + dayBounds!.height * 0.9,
    bubbles: true,
  });
  await expect(dialog).toBeVisible();
  await expectCentered(page, dialog);
});

test("cancelled lesson opens a one-off makeup flow with available slots", async ({ page }) => {
  const now = new Date();
  const lessonStart = new Date(now);
  lessonStart.setHours(15, 0, 0, 0);
  const lessonEnd = new Date(lessonStart.getTime() + 45 * 60_000);
  const expiresAt = new Date(now);
  expiresAt.setDate(expiresAt.getDate() + 60);
  let scheduledStart: string | undefined;

  await page.route("**/api/**", async (route) => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname === "/api/auth/me") {
      await route.fulfill({ json: { id: "user-1", email: "teacher@example.com", role: "Teacher", mustChangePassword: false, teacherId: "teacher-1", instrumentIds: ["instrument-1"] } });
      return;
    }
    if (pathname === "/api/me/notifications") {
      await route.fulfill({ json: { items: [], unreadCount: 0 } });
      return;
    }
    if (pathname === "/api/calendar") {
      await route.fulfill({ json: [{ id: "lesson-1", lessonSeriesId: "series-1", startAt: lessonStart.toISOString(), endAt: lessonEnd.toISOString(), status: "Cancelled", studentId: "student-1", studentName: "Telafi Öğrencisi", teacherId: "teacher-1", teacherName: "Ayşe Öğretmen", instrumentId: "instrument-1", instrumentName: "Piyano", rsvpResponse: "Unknown" }] });
      return;
    }
    if (pathname === "/api/students") {
      await route.fulfill({ json: [{ id: "student-1", firstName: "Telafi", lastName: "Öğrencisi", birthDate: "2014-01-01", status: "Active" }] });
      return;
    }
    if (pathname === "/api/teachers") {
      await route.fulfill({ json: [{ id: "teacher-1", firstName: "Ayşe", lastName: "Öğretmen", status: "Active", instrumentIds: ["instrument-1"] }] });
      return;
    }
    if (pathname === "/api/instruments") {
      await route.fulfill({ json: [{ id: "instrument-1", code: "PIANO", name: "Piyano" }] });
      return;
    }
    if (pathname === "/api/students/student-1/enrollments") {
      await route.fulfill({ json: [{ id: "enrollment-1", studentId: "student-1", teacherId: "teacher-1", instrumentId: "instrument-1", status: "Active", startedAt: "2026-01-01", endedAt: null }] });
      return;
    }
    if (pathname === "/api/students/student-1/makeup-credits") {
      await route.fulfill({ json: [{ id: "credit-1", studentId: "student-1", sourceLessonId: "lesson-1", earnedReason: "SchoolCancelled", earnedAt: now.toISOString(), expiresAt: expiresAt.toISOString(), status: "Available", usedLessonId: null, sourceLessonStartAt: lessonStart.toISOString() }] });
      return;
    }
    if (pathname === "/api/makeup-credits/credit-1/use" && route.request().method() === "POST") {
      scheduledStart = route.request().postDataJSON().startAt;
      await route.fulfill({ json: { creditId: "credit-1", newLessonId: "makeup-1" } });
      return;
    }
    if (pathname === "/api/teachers/teacher-1/availability") {
      await route.fulfill({ json: [] });
      return;
    }
    await route.fulfill({ json: [] });
  });

  await page.goto("/dashboard/calendar");
  await page.getByRole("button", { name: /Telafi Öğrencisi/ }).first().click();
  const lessonDialog = page.getByRole("dialog", { name: "Ders detayları" });
  await expect(lessonDialog.getByRole("button", { name: "Telafi dersi ekle" })).toBeVisible();
  await lessonDialog.getByRole("button", { name: "Telafi dersi ekle" }).click();

  const makeupDialog = page.getByRole("dialog", { name: "Telafi Öğrencisi için telafi dersi" });
  await expect(makeupDialog).toContainText("Tek derslik telafi");
  await expect(makeupDialog).toContainText("Yalnızca iptal edilen ders gününden sonraki tarihler gösterilir.");
  await expect(makeupDialog.getByText("Müsait saatler", { exact: true })).toBeVisible();
  await expect(makeupDialog.getByRole("button", { name: "Telafi dersini yerleştir" })).toBeEnabled();
  await makeupDialog.getByRole("button", { name: "Telafi dersini yerleştir" }).click();
  await expect(makeupDialog).toBeHidden();

  expect(scheduledStart).toBeTruthy();
  const scheduledDay = new Date(scheduledStart!);
  scheduledDay.setHours(0, 0, 0, 0);
  const sourceDay = new Date(lessonStart);
  sourceDay.setHours(0, 0, 0, 0);
  expect(scheduledDay.getTime()).toBeGreaterThan(sourceDay.getTime());
});
