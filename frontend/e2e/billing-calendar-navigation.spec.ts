import { expect, test, type Page } from "@playwright/test";

const apiUrl = process.env.E2E_API_URL ?? "http://localhost:8080";
const adminEmail = process.env.E2E_ADMIN_EMAIL ?? "admin@example.com";
const adminPassword = process.env.E2E_ADMIN_PASSWORD ?? "DevAdmin123!";

async function loginAdmin(page: Page) {
  await page.goto("/login");
  await page.getByRole("radio", { name: /Yöneticiyim/ }).click();
  await page.locator("#email").fill(adminEmail);
  await page.locator("#password").fill(adminPassword);
  await page.getByRole("button", { name: "Giriş yap", exact: true }).click();
  await page.waitForURL(/\/dashboard/);
}

async function createBillingFixture(page: Page) {
  const instruments = await (await page.request.get(`${apiUrl}/api/instruments`)).json();
  const students = await (await page.request.get(`${apiUrl}/api/students`)).json();
  const student = students[0];
  const enrollments = await (await page.request.get(`${apiUrl}/api/students/${student.id}/enrollments`)).json();
  const enrollment = enrollments[0];
  const instrument = instruments.find((item: { id: string }) => item.id === enrollment.instrumentId);

  const existingLists = await (await page.request.get(`${apiUrl}/api/price-lists`)).json();
  let priceItem = existingLists.flatMap((list: { items: Array<{ instrumentId: string }> }) => list.items)
    .find((item: { instrumentId: string }) => item.instrumentId === instrument.id);
  if (!priceItem) {
    const created = await page.request.post(`${apiUrl}/api/price-lists`, {
      data: {
        name: `E2E Fiyat ${Date.now()}`,
        effectiveFrom: "2026-01-01",
        effectiveUntil: null,
        items: [{ instrumentId: instrument.id, durationMinutes: 50, billingType: "Monthly", amount: 100, currency: "TRY", packageLessonCount: null }],
      },
    });
    expect(created.status()).toBe(201);
    priceItem = (await created.json()).items[0];
  }

  expect((await page.request.post(`${apiUrl}/api/enrollments/${enrollment.id}/fee-plan`, {
    data: { priceListItemId: priceItem.id, dueDay: 5, activeFrom: "2026-01-01" },
  })).status()).toBe(201);
  expect((await page.request.post(`${apiUrl}/api/receivables`, {
    data: { enrollmentId: enrollment.id, period: "2026-09" },
  })).status()).toBe(201);
}

test.describe.serial("Ödeme takvimi ve hafta navigasyonu", () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    const seed = await page.request.post(`${apiUrl}/api/dev/mock-data/seed`);
    expect(seed.ok()).toBeTruthy();
  });

  test("ay seçimini temizlemek modalı çökertmez ve tahsilatı engeller", async ({ page }) => {
    const browserErrors: Error[] = [];
    page.on("pageerror", (error) => browserErrors.push(error));
    await createBillingFixture(page);

    const duesResponse = await page.request.get(`${apiUrl}/api/billing/dues`);
    expect(duesResponse.ok()).toBeTruthy();
    const dues = await duesResponse.json() as Array<{ studentId: string; enrollmentId: string; status: string }>;
    const due = dues.find((item) => item.status !== "Cancelled");
    expect(due).toBeTruthy();

    await page.goto("/dashboard/billing");
    await page.getByRole("button", { name: "Aidat al", exact: true }).first().click();
    const dialog = page.getByRole("dialog", { name: "Aidat ödemesi al" });
    await dialog.getByLabel("Öğrenci").selectOption(due!.studentId);

    const coursePicker = dialog.getByLabel("Hangi kurs?");
    if (await coursePicker.isVisible()) await coursePicker.selectOption(due!.enrollmentId);

    const monthPicker = dialog.getByLabel("İlk dönem");
    await expect(monthPicker).toBeVisible();
    await monthPicker.fill("");

    await expect(dialog.getByText("İlk dönemi seçin", { exact: false })).toBeVisible();
    await expect(dialog.getByRole("button", { name: /Elden alındı|ayı nakit ödendi/ })).toBeDisabled();
    await expect(dialog.getByRole("button", { name: /Havale geldi|aylık havale geldi/ })).toBeDisabled();
    expect(browserErrors).toEqual([]);
  });

  test("geçmiş ve gelecek haftaları tarih aralığıyla yükler, Bugün geri döner", async ({ page }) => {
    await page.goto("/dashboard/calendar");
    const range = page.locator('[aria-live="polite"]');
    const currentRange = await range.textContent();
    await expect(page.getByRole("button", { name: "Bugün", exact: true })).toBeDisabled();

    const previousRequest = page.waitForRequest((request) => request.url().includes("/api/calendar?") && request.url().includes("from=") && request.url().includes("to="));
    await page.getByRole("button", { name: "Önceki hafta" }).click();
    await previousRequest;
    await expect(range).not.toHaveText(currentRange ?? "");
    await expect(page.getByRole("button", { name: "Bugün", exact: true })).toBeEnabled();

    const futureRequest = page.waitForRequest((request) => request.url().includes("/api/calendar?") && request.url().includes("from=") && request.url().includes("to="));
    await page.getByRole("button", { name: "Sonraki hafta" }).click();
    await page.getByRole("button", { name: "Sonraki hafta" }).click();
    await futureRequest;
    await expect(range).not.toHaveText(currentRange ?? "");

    await page.getByRole("button", { name: "Bugün", exact: true }).click();
    await expect(range).toHaveText(currentRange ?? "");
    await expect(page.getByRole("button", { name: "Bugün", exact: true })).toBeDisabled();
  });
});
