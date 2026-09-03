import { expect, test, type Page } from "@playwright/test";

const apiUrl = process.env.E2E_API_URL ?? "http://localhost:8080";
const adminEmail = process.env.E2E_ADMIN_EMAIL ?? "admin@example.com";
const adminPassword = process.env.E2E_ADMIN_PASSWORD ?? "DevAdmin123!";
const teacherEmail = "mock.ayse.kaya@abdera.local";
const teacherPassword = "DemoTeacher123!";
const approvedComment = "E2E: Düzenli çalışması ritim ve ifade gelişimini belirgin biçimde destekliyor.";

async function loginStaff(page: Page, role: "Admin" | "Teacher", email: string, password: string) {
  await page.goto("/login");
  await page.getByRole("radio", { name: role === "Admin" ? /Yöneticiyim/ : /Öğretmenim/ }).click();
  await expect(page.locator("#email")).toBeFocused();
  await page.locator("#email").fill(email);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: "Giriş yap", exact: true }).click();
  await page.waitForURL(/\/dashboard/);
}

async function seedDemoData(page: Page) {
  const response = await page.request.post(`${apiUrl}/api/dev/mock-data/seed`);
  expect(response.ok()).toBeTruthy();
}

// Gerçek bir zaman dilimi hatasının düzeltmesi: `date.toISOString().slice(0, 10)` tarihi
// UTC'ye çevirir. Test makinesi Europe/Istanbul'da (UTC+3) ve yerel saat 00:00-03:00
// arasındaysa, UTC'ye çevrilince tarih BİR GÜN GERİYE düşer - "Pazartesi"yi hesaplayıp
// takvimde arıyorken aslında Pazar'ı arayan bir test üretir (gece yarısından hemen sonra
// koşulunca CI'da rastgele kırılırdı). Yerel tarih bileşenlerinden elle kurmak bu kaymayı
// önler - app'in kendi dateInputValue (calendar/page.tsx) yardımcısıyla aynı yaklaşım.
function localDateString(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

test.describe.serial("Abdera critical role flows", () => {
  test("admin creates and edits a lesson, then records a partial payment", async ({ page }) => {
    await loginStaff(page, "Admin", adminEmail, adminPassword);
    await seedDemoData(page);

    const instrumentsResponse = await page.request.get(`${apiUrl}/api/instruments`);
    const instruments = await instrumentsResponse.json();
    const piano = instruments.find((item: { code: string }) => item.code === "PIANO");
    const suffix = Date.now().toString();
    const teacherResponse = await page.request.post(`${apiUrl}/api/teachers`, {
      data: { firstName: "E2E", lastName: `Öğretmen ${suffix}`, instrumentIds: [piano.id], email: null },
    });
    expect(teacherResponse.status()).toBe(201);
    const teacher = (await teacherResponse.json()).teacher;
    const alternateTeacherResponse = await page.request.post(`${apiUrl}/api/teachers`, {
      data: { firstName: "E2E", lastName: `Alternatif ${suffix}`, instrumentIds: [piano.id], email: null },
    });
    expect(alternateTeacherResponse.status()).toBe(201);
    const alternateTeacher = (await alternateTeacherResponse.json()).teacher;
    const studentResponse = await page.request.post(`${apiUrl}/api/students`, {
      data: { firstName: "E2E", lastName: `Öğrenci ${suffix}`, birthDate: "2014-01-01" },
    });
    expect(studentResponse.status()).toBe(201);
    const student = await studentResponse.json();
    const enrollmentResponse = await page.request.post(`${apiUrl}/api/students/${student.id}/enrollments`, {
      data: { teacherId: teacher.id, instrumentId: piano.id, startedAt: localDateString(new Date()) },
    });
    expect(enrollmentResponse.status()).toBe(201);
    const enrollment = await enrollmentResponse.json();
    const alternateEnrollmentResponse = await page.request.post(`${apiUrl}/api/students/${student.id}/enrollments`, {
      data: { teacherId: alternateTeacher.id, instrumentId: piano.id, startedAt: localDateString(new Date()) },
    });
    expect(alternateEnrollmentResponse.status()).toBe(201);

    const nextMonday = new Date();
    nextMonday.setDate(nextMonday.getDate() + ((8 - nextMonday.getDay()) % 7 || 7));
    const date = localDateString(nextMonday);

    await page.goto("/dashboard/calendar");
    await page.getByRole("button", { name: "Sonraki hafta" }).click();
    const createDayColumn = page.getByTestId(`calendar-day-${date}`);
    const createStartHour = Number(await createDayColumn.getAttribute("data-start-hour"));
    const createEndHour = Number(await createDayColumn.getAttribute("data-end-hour"));
    const createBounds = await createDayColumn.boundingBox();
    expect(createBounds).not.toBeNull();
    // Hedef saat SABIT SECILMEZ: takvimin gorunur saat penceresi o haftaki derslere gore
    // dinamik olarak daralip genisliyor (orn. seed verisiyle 16:00-21:00). 14:30 gibi sabit
    // bir deger pencerenin disinda kalinca test veriye bagli olarak kiriliyordu.
    // Bunun yerine pencerenin icinden, dakika hassasiyetini koruyan bir saat turetilir -
    // testin asil amaci (bos hucreye cift tiklayinca form o gun/saatle aciliyor mu) aynen korunur.
    const createTargetMinutes = (createStartHour + 1) * 60 + 30;
    expect(
      createTargetMinutes,
      `hedef saat takvimin gorunur penceresinde (${createStartHour}:00-${createEndHour}:00) olmali`,
    ).toBeLessThan(createEndHour * 60);
    const createTargetLabel = `${String(Math.floor(createTargetMinutes / 60)).padStart(2, "0")}:${String(createTargetMinutes % 60).padStart(2, "0")}`;
    const createRatio = (createTargetMinutes - createStartHour * 60) / ((createEndHour - createStartHour) * 60);
    expect(createRatio).toBeGreaterThan(0);
    expect(createRatio).toBeLessThan(1);
    await createDayColumn.dispatchEvent("dblclick", {
      clientX: createBounds!.x + Math.max(8, createBounds!.width / 2),
      clientY: createBounds!.y + createBounds!.height * createRatio,
      bubbles: true,
    });
    const quickAdd = page.getByRole("dialog", { name: "Yeni ders oluştur" });
    await expect(quickAdd).toContainText(createTargetLabel);
    await expect(quickAdd.getByLabel("Başlangıç tarihi")).toHaveValue(date);
    await expect(quickAdd.getByLabel("Saat", { exact: true })).toHaveValue(createTargetLabel);
    await quickAdd.getByLabel("1 · Öğrenci").selectOption(student.id);
    await quickAdd.getByLabel("2 · Ders ve öğretmen").selectOption(enrollment.id);
    await quickAdd.getByRole("button", { name: "Seriyi takvime yerleştir" }).click();
    await expect(page.getByRole("button", { name: "+ Yeni ders" })).toBeVisible();

    const lessonCard = page.getByRole("button", { name: new RegExp(`E2E Öğrenci ${suffix}`) }).first();
    await expect(lessonCard).toBeVisible();
    await lessonCard.click();
    await page.getByRole("button", { name: "Düzenle" }).click();
    const editDialog = page.getByRole("dialog", { name: "Ders detayları" });
    await editDialog.locator("label").filter({ hasText: "Öğretmen" }).locator("select").selectOption(alternateTeacher.id);
    await editDialog.getByLabel("Yeni saat").fill("11:00");
    await editDialog.getByLabel("Süre (dk)").fill("60");
    await editDialog.getByRole("button", { name: "Değişiklikleri kaydet" }).click();
    await expect(page.getByRole("dialog", { name: "Ders detayları" })).toBeHidden();
    await page.reload();
    await page.getByRole("button", { name: "Sonraki hafta" }).click();
    await page.getByRole("button", { name: new RegExp(`E2E Öğrenci ${suffix}`) }).first().click();
    await expect(page.getByText("60 dakika", { exact: true })).toBeVisible();

    // Gerçek HTML5 sürükle-bırak akışıyla dersi aynı gün içinde başka bir saate taşı;
    // ardından sayfayı yenileyerek değişikliğin yalnızca ekranda değil veritabanında da
    // kalıcı olduğunu doğrula.
    await page.getByRole("dialog", { name: "Ders detayları" }).getByRole("button", { name: "Kapat", exact: true }).first().click();
    const movableLesson = page.getByRole("button", { name: new RegExp(`E2E Öğrenci ${suffix}`) }).first();
    const dayColumn = page.getByTestId(`calendar-day-${date}`);
    const startHour = Number(await dayColumn.getAttribute("data-start-hour"));
    const endHour = Number(await dayColumn.getAttribute("data-end-hour"));
    const targetMinutes = (startHour + 2) * 60 + 45;
    const targetLabel = `${String(Math.floor(targetMinutes / 60)).padStart(2, "0")}:${String(targetMinutes % 60).padStart(2, "0")}`;
    const targetRatio = (targetMinutes - startHour * 60) / ((endHour - startHour) * 60);
    const bounds = await dayColumn.boundingBox();
    expect(bounds).not.toBeNull();
    const approved = page.waitForResponse((response) => response.url().includes("/api/change-requests/") && response.url().endsWith("/approve") && response.request().method() === "POST");
    await movableLesson.dragTo(dayColumn, { targetPosition: { x: Math.max(8, bounds!.width / 2), y: Math.max(8, bounds!.height * targetRatio) } });
    expect((await approved).ok()).toBeTruthy();
    await expect(page.getByText(new RegExp(`dersi .* ${targetLabel} olarak güncellendi`))).toBeVisible();
    await page.reload();
    await page.getByRole("button", { name: "Sonraki hafta" }).click();
    await expect(page.getByRole("button", { name: new RegExp(`E2E Öğrenci ${suffix}.*${targetLabel}`) }).first()).toBeVisible();

    const movedLesson = page.getByRole("button", { name: new RegExp(`E2E Öğrenci ${suffix}.*${targetLabel}`) }).first();
    // Lokal geliştirme veritabanında önceki E2E koşularından aynı slota denk gelen kartlar
    // kalmış olabilir; benzersiz öğrenci adına bağlı hedefe doğrudan olay göndererek testin
    // temiz CI veritabanı dışında da deterministik kalmasını sağla.
    await movedLesson.click({ force: true });
    await page.getByRole("button", { name: "Düzenle" }).click();
    const statusDialog = page.getByRole("dialog", { name: "Ders detayları" });
    await statusDialog.locator("label").filter({ hasText: "Durum" }).locator("select").selectOption("Cancelled");
    await statusDialog.getByRole("button", { name: "Değişiklikleri kaydet" }).click();
    await page.reload();
    await page.getByRole("button", { name: "Sonraki hafta" }).click();
    await page.getByRole("button", { name: new RegExp(`E2E Öğrenci ${suffix}.*${targetLabel}`) }).first().click({ force: true });
    await expect(page.getByText("İptal edildi", { exact: true })).toBeVisible();
    await page.getByRole("dialog", { name: "Ders detayları" }).getByRole("button", { name: "Kapat", exact: true }).first().click();

    // Aynı öğrencinin beşinci düzenli haftalık serisi, UI'dan bağımsız olarak API/domain
    // katmanında da reddedilmelidir.
    for (const [dayOfWeek, startTime] of [["Tuesday", "08:00:00"], ["Wednesday", "08:15:00"], ["Thursday", "08:30:00"]]) {
      const extraSeries = await page.request.post(`${apiUrl}/api/lesson-series`, {
        data: { enrollmentId: enrollment.id, dayOfWeek, startTime, durationMinutes: 30, effectiveFrom: date },
      });
      expect(extraSeries.status()).toBe(201);
    }
    const fifthSeries = await page.request.post(`${apiUrl}/api/lesson-series`, {
      data: { enrollmentId: enrollment.id, dayOfWeek: "Friday", startTime: "08:45:00", durationMinutes: 30, effectiveFrom: date },
    });
    expect(fifthSeries.status()).toBe(400);

    await page.goto("/dashboard/billing");
    await expect(page.getByRole("heading", { name: "Aidat yönetimi" })).toBeVisible();
    await page.getByRole("button", { name: "Tahsilat", exact: true }).first().click();
    const paymentForm = page.locator("form").filter({ has: page.getByRole("button", { name: "Ödemeyi kaydet" }) });
    await paymentForm.getByLabel("Tutar").fill("1");
    // Kismi odemenin GERCEKTEN kaydedildigini sunucu yanitindan dogrula. Ekranda bir
    // "Kismi odendi" rozetinin gorunmesi tek basina kanit degil: seed verisinde zaten
    // kismi odenmis aidatlar var, yani odeme hic yazilmasa da o rozet gorunurdu.
    const paymentSaved = page.waitForResponse((response) =>
      response.url().includes("/api/receivables/") &&
      response.url().endsWith("/payments") &&
      response.request().method() === "POST");
    await paymentForm.getByRole("button", { name: "Ödemeyi kaydet" }).click();
    expect((await paymentSaved).ok()).toBeTruthy();
    // Odeme sonrasi liste tazelenir ve ilgili satir kismi duruma duser.
    await expect(page.getByText("Kısmi ödendi").first()).toBeVisible();
  });

  test("teacher writes repertoire note and explicitly approves the parent comment", async ({ page }) => {
    await loginStaff(page, "Teacher", teacherEmail, teacherPassword);
    await page.goto("/dashboard/progress");
    // Öğrenci listesi artık uzun bir buton listesi değil, tek bir combobox (kullanıcı isteği).
    await page.getByRole("combobox", { name: "Öğrenci seç" }).selectOption({ label: "Lara Arslan" });
    await page.getByRole("button", { name: "Yeni gelişim notu" }).click();
    const note = `E2E öğretmen ham notu ${Date.now()}`;
    await page.getByLabel("Çalınan eser").fill("E2E Minuet");
    await page.getByRole("textbox", { name: "Ders notu", exact: true }).fill(note);
    await page.getByRole("button", { name: "Gelişim notunu kaydet" }).click();
    const entry = page.locator("article").filter({ hasText: note }).first();
    await expect(entry).toBeVisible();
    await entry.getByRole("button", { name: "Yorum hazırla" }).click();
    await entry.getByPlaceholder("Ham notu veliye uygun, yapıcı bir yorum olarak düzenleyin.").fill(approvedComment);
    await entry.getByRole("button", { name: "Onayla ve veliye aç" }).click();
    await expect(entry).toContainText("Onaylandı ve veliye görünür");
  });

  test("parent sees own calendar, payment view, approved comment and records practice", async ({ page }) => {
    await page.goto("/parent/login");
    await page.getByLabel("Telefon numarası").fill("+905550000001");
    await page.getByRole("button", { name: "Kod gönder" }).click();
    const debugText = await page.getByText(/Geliştirme kodu:/).innerText();
    const code = debugText.match(/\d{6}/)?.[0];
    expect(code).toBeTruthy();
    await page.getByLabel("Doğrulama kodu").fill(code!);
    await page.getByRole("button", { name: "Giriş yap" }).click();
    await page.waitForURL(/\/parent$/);
    await expect(page.getByText("Lara Arslan")).toBeVisible();
    await page.getByRole("button", { name: "Takvim" }).click();
    await expect(page.getByRole("heading", { name: "Takvim" })).toBeVisible();
    await page.getByRole("button", { name: "Aidat" }).click();
    await expect(page.getByRole("heading", { name: "Aidat", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Gelişim" }).click();
    // Bu testin bir onceki ("teacher ...") testin basarisina BAGLI OLMAMASI icin: beklenen
    // onayli yorum yoksa ogretmen olarak API uzerinden kurulur. Boylece serial zincirdeki
    // bir kirilma bu testi de dusurmez ve tek basina calistirilabilir.
    await ensureApprovedCommentExists(page);
    await expect(page.getByText(approvedComment).first()).toBeVisible();
    await page.getByPlaceholder("Bugünkü hedef").fill("E2E: 20 dakika gam");
    await page.getByRole("button", { name: "Çalışmayı kaydet ve onayla" }).click();
    await expect(page.getByText("E2E: 20 dakika gam").first()).toBeVisible();
    await expect(page.getByText("Veli onaylı").first()).toBeVisible();
  });
});

// Veli testinin ihtiyac duydugu "onaylanmis ogretmen yorumu" durumunu garanti eder.
// Zaten varsa hicbir sey yapmaz; yoksa ogretmen oturumuyla olusturup onaylar.
async function ensureApprovedCommentExists(page: Page) {
  const students = await (await page.request.get(`${apiUrl}/api/guardian/me/students`)).json();
  // GuardianStudentResponse alani "studentId" - "id" DEGIL.
  const studentId = students[0]?.studentId;
  if (!studentId) return;
  const progress = await (await page.request.get(`${apiUrl}/api/guardian/me/students/${studentId}/progress`)).json();
  if (progress.entries?.some((entry: { parentComment: string | null }) => entry.parentComment === approvedComment)) {
    return;
  }

  // Veli oturumunu bozmadan ayri bir istek baglami ac.
  const teacherContext = await page.context().browser()!.newContext({ baseURL: apiUrl });
  try {
    const login = await teacherContext.request.post(`${apiUrl}/api/auth/login`, {
      data: { email: teacherEmail, password: teacherPassword },
    });
    if (!login.ok()) return; // Ogretmen testi zaten kurmus olmali; kuramazsak assert konusur.

    const lessons = await (await teacherContext.request.get(
      `${apiUrl}/api/students/${studentId}/progress`)).json();
    const noteId = lessons.entries?.[0]?.id;
    if (!noteId) return;

    await teacherContext.request.put(`${apiUrl}/api/lesson-notes/${noteId}/parent-comment`, {
      data: { parentComment: approvedComment, approve: true },
    });
  } finally {
    await teacherContext.close();
  }
  await page.reload();
  await page.getByRole("button", { name: "Gelişim" }).click();
}
