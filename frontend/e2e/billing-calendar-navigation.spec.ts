import { expect, test, type Page } from "@playwright/test";

const apiUrl = process.env.E2E_API_URL ?? "http://localhost:8080";
const adminEmail = process.env.E2E_ADMIN_EMAIL ?? "admin@example.com";
const adminPassword = process.env.E2E_ADMIN_PASSWORD ?? "DevAdmin123!";

async function loginAdmin(page: Page) {
  // chooseRole=1: açık bir oturum varken /login doğrudan /dashboard'a yönlenir ve rol
  // kartları render edilmez (login/page.tsx). Bu parametre bilinçli rol seçimi yoludur.
  await page.goto("/login?chooseRole=1");
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

  // Aidat modeli yeniden tasarlandığında (docs/10-decisions.md H1) fiyat listesi -> fiyat
  // kalemi -> ücret planı zinciri ve `/api/price-lists*`, `/api/enrollments/{id}/fee-plan`
  // uçları kaldırıldı. Tek ön koşul ders türü için yürürlükte bir tarifenin olması; o da
  // migration ile seed ediliyor, yani aidat doğrudan açılabiliyor.
  //
  // 409 da kabul: bu bir FIXTURE, amacı "o dönem için bir aidat bulunsun". Aynı veritabanına
  // karşı ikinci kez koşulduğunda aidat zaten vardır ve bu bir hata değildir.
  const created = await page.request.post(`${apiUrl}/api/receivables`, {
    data: { enrollmentId: enrollment.id, period: "2026-09" },
  });
  expect([201, 409], `aidat oluşturulamadı: ${await created.text()}`).toContain(created.status());
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
    // Düğme ve pencere başlığı "Aidat al" / "Aidat ödemesi al" iken "Tahsilat kaydet"
    // olarak yeniden adlandırıldı; modalın içi (Öğrenci / Hangi kurs? / İlk dönem /
    // Elden alındı / Havale geldi) aynı kaldı, bu yüzden testin geri kalanı değişmiyor.
    await page.getByRole("button", { name: "Tahsilat kaydet", exact: true }).first().click();
    const dialog = page.getByRole("dialog", { name: "Tahsilat kaydet" });
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
