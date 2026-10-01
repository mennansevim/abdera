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
  const students = await (await page.request.get(`${apiUrl}/api/students`)).json();
  const student = students[0];
  const enrollments = await (await page.request.get(`${apiUrl}/api/students/${student.id}/enrollments`)).json();
  const enrollment = enrollments[0];
  // Güncel modelde fiyat PriceList/FeePlan'dan değil merkezi TuitionRate politikasından
  // hesaplanır. Seed tarifeyi kurar; test yalnızca öğrenci aidatını açar.
  const created = await page.request.post(`${apiUrl}/api/receivables`, {
    data: { enrollmentId: enrollment.id, period: "2026-09" },
  });
  expect([201, 409]).toContain(created.status());
}

test.describe.serial("Ödeme takvimi ve hafta navigasyonu", () => {
  test.beforeEach(async ({ page }) => {
    await loginAdmin(page);
    const seed = await page.request.post(`${apiUrl}/api/dev/mock-data/seed`);
    expect(seed.ok()).toBeTruthy();
  });

  test("ödeme penceresi yıl değiştirince çökmez ve tahsilat açık kalır", async ({ page }) => {
    const browserErrors: Error[] = [];
    page.on("pageerror", (error) => browserErrors.push(error));
    await createBillingFixture(page);

    await page.goto("/dashboard/billing");
    await page.getByRole("button", { name: "Çizelge", exact: true }).click();
    // İlk öğrencinin adına dokun: pencere hiçbir ay seçili olmadan açılır.
    await page.locator("table tbody th button").first().click();
    const dialog = page.getByRole("dialog").first();
    await expect(dialog.getByText("Hangi aylar?")).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Önce ay seç" })).toBeDisabled();

    // Aralık + Ocak gibi yıl aşan peşin ödeme için pencere kendi yılını değiştirebilir.
    const year = new Date().getFullYear();
    await dialog.getByRole("button", { name: "Sonraki yıl" }).click();
    await expect(dialog.getByText(String(year + 1), { exact: true })).toBeVisible();
    await dialog.getByRole("button", { name: "Önceki yıl" }).click();
    await expect(dialog.getByText(String(year), { exact: true })).toBeVisible();

    await expect(dialog).toBeVisible();
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
