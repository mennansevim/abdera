import { expect, test, type Page } from "@playwright/test";

const apiUrl = process.env.E2E_API_URL ?? "http://localhost:8080";
const adminEmail = process.env.E2E_ADMIN_EMAIL ?? "admin@example.com";
const adminPassword = process.env.E2E_ADMIN_PASSWORD ?? "DevAdmin123!";
const teacherEmail = "mock.ayse.kaya@abdera.local";
const teacherPassword = "DemoTeacher123!";
const demoGuardianPhone = "+905550000001";

// Genel bir "sayfa hiç açılmadı / boş kaldı / konsola hata bastı" duman testi. Ders/aidat/
// akış detaylarını değil, YALNIZCA her ekranın gerçekten render olduğunu doğrular - derin
// senaryolar critical-roles.spec.ts / billing-calendar-navigation.spec.ts / data-isolation.spec.ts'te.
// Bu dosya kasıtlı sığ: amacı "biri bir sayfayı kırdı mı" sorusuna saniyeler içinde cevap vermek.
function trackPageErrors(page: Page) {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(`[${page.url()}] console.error: ${message.text()}`);
  });
  return errors;
}

async function loginStaff(page: Page, role: "Admin" | "Teacher", email: string, password: string) {
  await page.goto("/login");
  await page.getByRole("radio", { name: role === "Admin" ? /Yöneticiyim/ : /Öğretmenim/ }).click();
  await page.locator("#email").fill(email);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: "Giriş yap", exact: true }).click();
  await page.waitForURL(/\/dashboard/);
}

// Bu dosya diğer spec dosyalarının bıraktığı veriye GÜVENMEZ (hangi sırayla koşacakları
// garanti değil) - kendi mock öğretmen/öğrenci/veli sabitlerini burada, tek seferde kurar.
// describe.serial sayesinde bu, dosyadaki sonraki testlerden önce kesin olarak tamamlanır.
async function seedDemoData(page: Page) {
  const response = await page.request.post(`${apiUrl}/api/dev/mock-data/seed`);
  expect(response.ok()).toBeTruthy();
}

const ADMIN_PAGES: Array<{ path: string; heading: RegExp }> = [
  { path: "/dashboard", heading: /Merhaba/ },
  { path: "/dashboard/students", heading: /^Öğrenciler$/ },
  { path: "/dashboard/progress", heading: /Gelişim günlüğü/ },
  { path: "/dashboard/teachers", heading: /^Öğretmenler$/ },
  { path: "/dashboard/benchmark", heading: /Performans/ },
  { path: "/dashboard/calendar", heading: /Ders Programı/ },
  { path: "/dashboard/shows", heading: /Yıl sonu gösterisi/i },
  { path: "/dashboard/change-requests", heading: /^Talepler$/ },
  { path: "/dashboard/billing", heading: /Aidat yönetimi/ },
  { path: "/dashboard/costs", heading: /Maliyet takibi/ },
  { path: "/dashboard/banking", heading: /Banka entegrasyonu/ },
  { path: "/dashboard/notifications", heading: /Mesaj Merkezi/ },
  { path: "/dashboard/backups", heading: /^Yedekleme$/ },
  { path: "/dashboard/settings", heading: /^Ayarlar$/ },
];

test.describe.serial("Abdera duman testleri (smoke)", () => {
  test("admin: her menü sayfası konsol hatası olmadan ve doğru başlıkla açılıyor", async ({ page }) => {
    // Hata izleme login'DEN SONRA başlar: /login'in kendisi "zaten oturum var mı" diye
    // /api/me + /api/guardian/me'yi anonim ziyaretçide bilerek dener, ikisi de 401 döner -
    // bu beklenen bir durum, tarayıcı yine de bunu console.error olarak loglar.
    await loginStaff(page, "Admin", adminEmail, adminPassword);
    const errors = trackPageErrors(page);
    await seedDemoData(page);

    for (const { path, heading } of ADMIN_PAGES) {
      await test.step(path, async () => {
        await page.goto(path);
        await expect(page.getByRole("heading", { name: heading }).first()).toBeVisible();
      });
    }

    // AdminGate arkasına alındı - Admin oturumuyla normal şekilde açılmalı (bkz. ayrı
    // "/tasarimlar oturumsuz ziyarette içerik sızdırmıyor" testi, orada anonim tarafı kontrol edilir).
    // Aynı oturumla kontrol ediliyor ki dosya gereksiz yere ikinci bir admin girişi yapmasın
    // (staff login rate limit'i - Program.cs "auth-login" politikası - 15 dakikada 5 istek).
    await test.step("/tasarimlar", async () => {
      await page.goto("/tasarimlar");
      await expect(page.getByText("3 alternatif")).toBeVisible();
    });

    expect(errors, `konsola/sayfaya yansıyan hatalar:\n${errors.join("\n")}`).toEqual([]);
  });

  // Öğretmen oturumu farklı (daraltılmış) bir menüyle çalışıyor - Talepler/Aidatlar/Banka gibi
  // Admin-only sayfalar hiç görünmemeli, kendi sayfaları ise sorunsuz açılmalı.
  test("teacher: kendi menüsündeki sayfalar konsol hatası olmadan açılıyor", async ({ page }) => {
    await loginStaff(page, "Teacher", teacherEmail, teacherPassword);
    const errors = trackPageErrors(page);

    await expect(page.getByRole("link", { name: "Talepler" })).toHaveCount(0);

    for (const { path, heading } of [
      { path: "/dashboard", heading: /Bugün|[A-ZÇĞİÖŞÜ][a-zçğıöşü]+ /i },
      { path: "/dashboard/calendar", heading: /Ders Programı/ },
      { path: "/dashboard/progress", heading: /Gelişim günlüğü/ },
      { path: "/dashboard/settings", heading: /^Ayarlar$/ },
    ]) {
      await test.step(path, async () => {
        await page.goto(path);
        await expect(page.getByRole("heading", { name: heading }).first()).toBeVisible();
      });
    }

    expect(errors, `konsola/sayfaya yansıyan hatalar:\n${errors.join("\n")}`).toEqual([]);
  });

  // Veli portalı ayrı bir kimlik doğrulama akışı ve alt navigasyon kullanıyor (bkz.
  // guardian-auth.ts) - dashboard'daki AppShell'den tamamen bağımsız, ayrıca duman testi gerekir.
  test("veli: portal sekmeleri konsol hatası olmadan açılıyor", async ({ page }) => {
    await page.goto("/parent/login");
    // Varsayılan giriş modu telefon+kalıcı şifre (docs/10-decisions.md Karar F reversal) - OTP
    // ikincil bir seçenek, önce oraya geçmek gerekiyor.
    await page.getByRole("button", { name: "Şifreni bilmiyor musun? WhatsApp ile kod al" }).click();
    await page.getByLabel("Telefon numarası").fill(demoGuardianPhone);
    await page.getByRole("button", { name: "Kod gönder" }).click();
    const debugText = await page.getByText(/Geliştirme kodu:/).innerText();
    const code = debugText.match(/\d{6}/)?.[0];
    expect(code, "dev OTP kodu görünmeli").toBeTruthy();
    await page.getByLabel("Doğrulama kodu").fill(code!);
    await page.getByRole("button", { name: "Giriş yap" }).click();
    await page.waitForURL(/\/parent$/);
    const errors = trackPageErrors(page);

    for (const tabName of ["Takvim", "Aidat", "Gelişim", "Mesajlar", "Ana Sayfa"]) {
      await test.step(tabName, async () => {
        await page.getByRole("button", { name: tabName }).click();
        await expect(page.getByRole("heading", { name: tabName === "Ana Sayfa" ? /./ : tabName, exact: tabName !== "Ana Sayfa" }).first()).toBeVisible();
      });
    }

    expect(errors, `konsola/sayfaya yansıyan hatalar:\n${errors.join("\n")}`).toEqual([]);
  });

  // /tasarimlar prod route'unda duruyor ama Admin dışına kapalı olmalı (bkz. AdminGate) -
  // yetkisiz erişim boş sayfa göstermeli, tasarım içeriğini asla sızdırmamalı.
  test("/tasarimlar oturumsuz ziyarette içerik sızdırmıyor", async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto("/tasarimlar");
    await expect(page.getByText("3 alternatif")).toHaveCount(0);
    await context.close();
  });
});
