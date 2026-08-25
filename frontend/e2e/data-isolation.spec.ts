import { expect, test, type Page } from "@playwright/test";

const apiUrl = process.env.E2E_API_URL ?? "http://localhost:8080";
const adminEmail = process.env.E2E_ADMIN_EMAIL ?? "admin@example.com";
const adminPassword = process.env.E2E_ADMIN_PASSWORD ?? "DevAdmin123!";
const demoGuardianPhone = "+905550000001";

// Veli veri izolasyonu, backend integration testlerinde de doğrulanıyor
// (ProgressFlowTests / GuardianPortalFlowTests). Bu dosya aynı sınırı GERÇEK tarayıcı
// oturumuyla — gerçek çerez, CORS ve middleware zinciriyle — tekrar doğrular: bir veli
// tarayıcıda giriş yaptıktan sonra URL'deki öğrenci kimliğini değiştirerek başka bir
// öğrencinin verisine ulaşamamalı.
//
// critical-roles.spec.ts'ten BAĞIMSIZ: kendi seed'ini yapar, kendi girişini açar.
//
// Tüm kontroller TEK testte ve TEK veli girişinde toplanır. Bunun nedeni kozmetik değil:
// veli OTP ucu kaba kuvvete karşı IP başına 15 dakikada 5 istekle sınırlı
// (RateLimiting__GuardianOtpPermitLimit). Her kontrol için ayrı giriş yapmak, testin
// kendi güvenlik kuralımıza takılıp kırılgan hale gelmesine yol açıyordu. Doğru çözüm
// kuralı gevşetmek değil, testin gereksiz yere OTP tüketmemesi.
test("veli yalnızca kendi çocuğunun verisini görebilir ve ham öğretmen notunu asla almaz", async ({ page }) => {
  // --- Hazırlık: demo verisi ikinci (ilişkisiz) bir öğrenci de üretir ---
  await page.goto("/login");
  const adminLogin = await page.request.post(`${apiUrl}/api/auth/login`, {
    data: { email: adminEmail, password: adminPassword },
  });
  expect(adminLogin.ok(), "admin girişi başarısız").toBeTruthy();
  expect((await page.request.post(`${apiUrl}/api/dev/mock-data/seed`)).ok()).toBeTruthy();

  const allStudents = await (await page.request.get(`${apiUrl}/api/students`)).json();
  expect(allStudents.length).toBeGreaterThan(1);

  // Admin oturumunu tamamen bırak; bundan sonrası yalnızca velinin yetkisiyle olmalı.
  await page.request.post(`${apiUrl}/api/auth/logout`);
  await page.context().clearCookies();

  await loginAsGuardian(page);

  // --- 1) Yabancı bir öğrencinin verisi reddedilmeli ---
  const ownStudents = await (await page.request.get(`${apiUrl}/api/guardian/me/students`)).json();
  expect(ownStudents.length).toBeGreaterThan(0);
  // /api/guardian/me/students yaniti "id" degil "studentId" tasir (GuardianStudentResponse).
  const ownIds = new Set<string>(ownStudents.map((student: { studentId: string }) => student.studentId));

  const foreignStudent = allStudents.find((student: { id: string }) => !ownIds.has(student.id));
  expect(foreignStudent, "demo verisinde ilişkisiz bir öğrenci bulunmalı").toBeTruthy();

  // /calendar zorunlu from/to parametreleri ister; bunlar verilmezse istek yetki
  // kontrolune hic ulasmadan model binding'de reddedilir ve test asil sinirI olcmez.
  const from = new Date().toISOString();
  const to = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString();

  for (const path of [
    `/api/guardian/me/students/${foreignStudent.id}/progress`,
    `/api/guardian/me/students/${foreignStudent.id}/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
    `/api/guardian/me/students/${foreignStudent.id}/practice-journal`,
  ]) {
    const response = await page.request.get(`${apiUrl}${path}`);
    expect(
      [401, 403, 404],
      `${path} yabancı bir öğrenci için reddedilmeliydi ama ${response.status()} döndü`,
    ).toContain(response.status());
  }

  // --- 2) Yönetici uçları veliye tamamen kapalı olmalı ---
  // Rol sınırı, kimlik sınırı kadar önemli: veli hiçbir admin verisine erişememeli.
  for (const path of ["/api/students", "/api/receivables", "/api/teachers"]) {
    const response = await page.request.get(`${apiUrl}${path}`);
    expect(
      [401, 403],
      `${path} veliye kapalı olmalıydı ama ${response.status()} döndü`,
    ).toContain(response.status());
  }

  // --- 3) Kendi çocuğunun yanıtı bile ham öğretmen notunu taşımamalı ---
  const ownStudent = ownStudents[0];
  const progressResponse = await page.request.get(`${apiUrl}/api/guardian/me/students/${ownStudent.studentId}/progress`);
  expect(progressResponse.ok()).toBeTruthy();
  const raw = await progressResponse.text();

  // Ham öğretmen notu (LessonNote.Note) ve onay meta verisi veli DTO'suna hiç girmemeli.
  // Not: "homework"/"nextGoal" bilinçli olarak PAYLAŞILIR - bunlar zaten veliye yönelik
  // alanlardır; gizli olan yalnızca öğretmenin kendi ham notudur.
  // Aynı sınırın veri düzeyindeki kanıtı backend tarafında da var: GuardianPortalFlowTests
  // onaylanmamış bir ham notun metninin yanıtta hiç geçmediğini doğruluyor.
  for (const forbiddenField of ['"note"', '"parentCommentApprovedBy"', '"parentCommentApprovedAt"']) {
    expect(raw, `veli yanıtında ${forbiddenField} alanı bulunmamalı`).not.toContain(forbiddenField);
  }
});

async function loginAsGuardian(page: Page) {
  await page.goto("/parent/login");
  await page.getByLabel("Telefon numarası").fill(demoGuardianPhone);

  // OTP isteğinin gerçekten başarılı olduğunu doğrula. Aksi halde (örn. hız sınırına
  // takılınca) test ilerideki bir adımda anlamsız bir zaman aşımıyla düşüyor ve gerçek
  // sebep görünmüyordu.
  const otpResponse = page.waitForResponse((response) =>
    response.url().includes("/api/guardian/otp/request") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Kod gönder" }).click();
  const otpStatus = (await otpResponse).status();
  expect(
    otpStatus,
    otpStatus === 429
      ? "veli OTP ucu hız sınırına takıldı (IP başına 15 dk'da 5 istek) - testleri arka arkaya çalıştırdıysan sınırın dolmasını bekle"
      : `OTP isteği başarısız (HTTP ${otpStatus})`,
  ).toBe(200);

  const debugText = await page.getByText(/Geliştirme kodu:/).innerText();
  const code = debugText.match(/\d{6}/)?.[0];
  expect(code, "Development ortamında doğrulama kodu ekranda gösterilmeli").toBeTruthy();
  await page.getByLabel("Doğrulama kodu").fill(code!);
  await page.getByRole("button", { name: "Giriş yap" }).click();
  await page.waitForURL(/\/parent$/);
}
