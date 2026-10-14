import { expect, type Page } from "@playwright/test";

const apiUrl = process.env.E2E_API_URL ?? "http://localhost:8080";
const adminEmail = process.env.E2E_ADMIN_EMAIL ?? "admin@example.com";
const adminPassword = process.env.E2E_ADMIN_PASSWORD ?? "DevAdmin123!";

// Veli girişi (docs/10-decisions.md AA): şifreyi okul verir, veli telefon + şifreyle girer.
// Giriş ekranındaki "WhatsApp ile kod al" kaldırıldığı için testler de gerçek kullanıcı yolunu
// izler - yönetici "Veli girişleri"ndeki gibi şifre verir (POST reset-password), veli o şifreyle
// formdan girer. Yönetici oturumu ayrı bir bağlamda açılır; veli oturumuna karışmaz.
export async function loginAsGuardian(page: Page, phone: string) {
  const adminContext = await page.context().browser()!.newContext({ baseURL: apiUrl });
  let password: string;
  try {
    const login = await adminContext.request.post(`${apiUrl}/api/auth/login`, {
      data: { email: adminEmail, password: adminPassword },
    });
    expect(login.status(), "yönetici API girişi başarısız").toBe(200);

    const lookup = await adminContext.request.get(`${apiUrl}/api/guardians/by-phone?phone=${encodeURIComponent(phone)}`);
    expect(lookup.status(), `${phone} numaralı veli bulunamadı`).toBe(200);
    const guardian = (await lookup.json()) as { id: string };

    const reset = await adminContext.request.post(`${apiUrl}/api/guardians/${guardian.id}/reset-password`);
    expect(reset.status(), "veliye şifre verilemedi").toBe(200);
    password = ((await reset.json()) as { password: string }).password;
  } finally {
    await adminContext.close();
  }

  await page.goto("/parent/login");
  await page.getByLabel("Telefon numarası").fill(phone);
  await page.getByLabel("Şifre").fill(password);
  await page.getByRole("button", { name: "Giriş yap" }).click();
  await page.waitForURL(/\/parent$/);
}
