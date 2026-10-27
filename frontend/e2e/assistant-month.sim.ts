import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { expect, test, type Locator, type Page } from "@playwright/test";
import { RELEASES } from "../src/data/releases";

// Abdera Asistanı - bir aylık işlem simülasyonu (docs/10-decisions.md AE). Yönetici, önümüzdeki
// dört haftanın dersleri üzerinde asistanla gerçek işlemler yapar: gelişim ve aidat sorgusu,
// hatırlatma, tahsilat, tek ders ve haftalık program taşıma, göreli kaydırma, telafi, tatil ve
// müsaitlik engelleri. Her işlemin beklenen sonucu aşağıda yazılıdır; gerçekleşen sonuç
// sohbet ekranından değil API'den (takvim, aidat, telafi hakları) doğrulanır.
//
// Gerçek API + örnek okul verisi ister ve veriyi DEĞİŞTİRİR: yalnızca geçici bir veritabanında
// çalıştır (tools/assistant-benchmark/README.md). Sonuç tablosu
// tools/assistant-benchmark/results/month-latest.md dosyasına yazılır.

const API = process.env.E2E_API_URL ?? "http://localhost:8080";
const RESULTS_DIR = join(__dirname, "..", "..", "tools", "assistant-benchmark", "results");

type Lesson = { id: string; startAt: string; endAt: string; status: string; studentName: string; teacherName: string; attendanceStatus?: string | null };
type Due = { id: string; studentId: string; studentName: string; amount: number; totalPaid: number; status: string; dueDate: string; currency: string };
type Row = { id: string; week: string; request: string; expected: string; actual: string; passed: boolean };

const rows: Row[] = [];
const money = (value: number) => new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", maximumFractionDigits: 0 }).format(value);

function startOfToday() {
  const date = new Date();
  date.setHours(0, 0, 0, 0);
  return date;
}

function addDays(date: Date, days: number) {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function dateKey(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

// Bugünden sonraki ilk gün (dow: 0 Pazar ... 6 Cumartesi). Bugün o günse bugün.
function next(dow: number) {
  const today = startOfToday();
  return addDays(today, (dow - today.getDay() + 7) % 7);
}

function at(date: Date, hour: number, minute = 0) {
  const value = new Date(date);
  value.setHours(hour, minute, 0, 0);
  return value;
}

function weekOf(date: Date) {
  return `Hafta ${Math.floor((date.getTime() - startOfToday().getTime()) / (7 * 86_400_000)) + 1}`;
}

function dayText(date: Date) {
  return date.toLocaleDateString("tr-TR", { day: "numeric", month: "long", weekday: "long" });
}

async function getJson<T>(page: Page, path: string): Promise<T> {
  const response = await page.request.get(`${API}${path}`);
  expect(response.ok(), `${path} → ${response.status()}`).toBeTruthy();
  return response.json() as Promise<T>;
}

async function lessonsOf(page: Page, studentName: string, from: Date, to: Date) {
  const lessons = await getJson<Lesson[]>(page, `/api/calendar?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`);
  return lessons.filter((lesson) => lesson.studentName === studentName).sort((a, b) => a.startAt.localeCompare(b.startAt));
}

// API tarihleri "+00:00" biçiminde döner; karşılaştırma zaman değeri üzerinden.
const sameInstant = (value: string | undefined, expected: Date) => value !== undefined && new Date(value).getTime() === expected.getTime();

function describe(lessons: Lesson[]) {
  return lessons.map((lesson) => `${new Date(lesson.startAt).toLocaleString("tr-TR", { day: "numeric", month: "short", weekday: "short", hour: "2-digit", minute: "2-digit" })} ${lesson.status}`).join(", ") || "ders yok";
}

async function openDues(page: Page) {
  const dues = await getJson<Due[]>(page, "/api/billing/dues");
  const lastKey = dateKey(addDays(startOfToday(), 7));
  return dues.filter((due) => ["Unpaid", "Partial", "Overdue"].includes(due.status) && due.amount - due.totalPaid > 0 && due.dueDate <= lastKey);
}

// Her işlem temiz bir sohbetle başlar: sayfa yeniden açılır, asistan açılır, cümle yazılır.
// Cevap gerçek dil modelinden gelir (birkaç saniye); sohbet ucunun yanıtı beklenir.
async function chat(page: Page, text: string) {
  await page.goto("/dashboard/releases");
  await page.getByRole("button", { name: "Abdera Asistanı (⌘K)" }).click();
  const dialog = page.getByRole("dialog", { name: "Abdera Asistanı" });
  await say(page, dialog, text);
  return dialog;
}

async function say(page: Page, dialog: Locator, text: string) {
  await dialog.getByLabel("Asistana yaz").fill(text);
  const answered = page.waitForResponse((response) => response.url().includes("/api/assistant/chat"), { timeout: 60_000 });
  await dialog.getByRole("button", { name: "Gönder", exact: true }).click();
  const response = await answered;
  expect(response.ok(), `sohbet ucu ${response.status()}`).toBeTruthy();
}

// Tek ders "Onayla ve taşı", her hafta "Onayla, programı değiştir".
async function confirmMove(dialog: Locator) {
  await dialog.getByRole("button", { name: /^Onayla( ve taşı|, programı değiştir)$/ }).click();
}

async function chooseOnce(dialog: Locator) {
  await dialog.getByRole("button", { name: "Yalnız bu ders" }).click();
}

async function op(id: string, week: string, request: string, expected: string, run: () => Promise<string>) {
  try {
    rows.push({ id, week, request, expected, actual: await run(), passed: true });
  } catch (error) {
    const message = (error instanceof Error ? error.message : String(error)).split("\n").filter(Boolean).slice(0, 3).join(" ").replace(/\u001b\[[0-9;]*m/g, "");
    rows.push({ id, week, request, expected, actual: message.slice(0, 300), passed: false });
  }
}

test("asistanla bir aylık işlem simülasyonu", async ({ page }) => {
  const login = await page.request.post(`${API}/api/dev/auth/login`, { data: { role: "Admin" } });
  expect(login.ok()).toBeTruthy();
  const me = await getJson<{ id: string }>(page, "/api/auth/me");
  await page.addInitScript(({ userId, version }) => window.localStorage.setItem(`abdera:seen-release:${userId}`, version), { userId: me.id, version: RELEASES[0]!.version });
  const students = new Map((await getJson<{ id: string; firstName: string; lastName: string }[]>(page, "/api/students")).map((student) => [`${student.firstName} ${student.lastName}`, student.id]));

  const monday = next(1), thursday = next(4), friday = next(5), saturday = next(6);
  const fourNovember = new Date(2026, 10, 4), sixNovember = new Date(2026, 10, 6), holiday = new Date(2026, 10, 9);

  await op("O01", weekOf(startOfToday()), "Deniz Arslan'ın gelişimini getir", "Devam oranı analiz ekranıyla aynı (%geldi/işaretli, 3 ay); çalıştığı eser görünür", async () => {
    const analysis = await getJson<{ months: { attended: number; rated: number }[]; pieces: { title: string; finished: boolean }[] }>(page, `/api/students/${students.get("Deniz Arslan")}/analysis?months=3`);
    const percent = Math.round((analysis.months.reduce((sum, month) => sum + month.attended, 0) / analysis.months.reduce((sum, month) => sum + month.rated, 0)) * 100);
    const piece = analysis.pieces.filter((item) => !item.finished).at(-1);
    const dialog = await chat(page, "Deniz Arslan'ın gelişimini getir");
    await expect(dialog.getByText(`%${percent}`, { exact: true })).toBeVisible();
    if (piece) await expect(dialog.getByText(piece.title)).toBeVisible();
    return `%${percent}${piece ? `, ${piece.title}` : ""}`;
  });

  await op("O02", weekOf(startOfToday()), "Aidat günü gelenleri listele", "Ödenmemiş + kısmi ödenmiş aidatlar (ödenmişler yok), toplam kalan tutar doğru", async () => {
    const dues = await openDues(page);
    const total = dues.reduce((sum, due) => sum + due.amount - due.totalPaid, 0);
    const dialog = await chat(page, "Aidat günü gelenleri listele");
    await expect(dialog.getByText(money(total)).first()).toBeVisible();
    for (const due of dues) await expect(dialog.getByText(due.studentName, { exact: true })).toBeVisible();
    for (const paid of ["Ada Çetin", "Bora Aydın", "Ege Kurt"]) await expect(dialog.getByText(paid, { exact: true })).toHaveCount(0);
    return `${dues.length} aidat, ${money(total)}`;
  });

  await op("O03", weekOf(startOfToday()), "Deniz Arslan'a aidat hatırlatması (karttan, onaylı)", "Onaydan sonra 1 hatırlatma sıraya alınır", async () => {
    const dialog = await chat(page, "Aidat günü gelenleri listele");
    await dialog.locator("li").filter({ hasText: "Deniz Arslan" }).getByRole("button", { name: "Hatırlatma gönder" }).click();
    await dialog.getByRole("button", { name: "Gönder", exact: true }).first().click();
    await expect(dialog.getByText("1 hatırlatma sıraya alındı.")).toBeVisible();
    return "1 hatırlatma sıraya alındı";
  });

  await op("O04", weekOf(startOfToday()), "Deniz Arslan'ın aidat durumu → tekrar hatırlatma", "Aynı aidat için ikinci hatırlatma gönderilmez, nedeniyle \"gönderilmedi\" denir", async () => {
    const dialog = await chat(page, "Deniz Arslan'ın aidat durumu");
    await dialog.getByRole("button", { name: "Hatırlatma gönder" }).click();
    await dialog.getByRole("button", { name: "Gönder", exact: true }).first().click();
    await expect(dialog.getByText("0/1 hatırlatma sıraya alındı.", { exact: false })).toBeVisible();
    await expect(dialog.getByText(/Gönderilmedi/)).toBeVisible();
    return "0/1, gönderilmedi";
  });

  await op("O05", weekOf(startOfToday()), "Vadesi geçenlere toplu hatırlatma", "Yalnızca WhatsApp izni veren velilere ve henüz hatırlatılmamış aidatlara gider (Deniz'inki O03'te gitti); gönderilmeyenlerin nedeni söylenir", async () => {
    const overdue = (await openDues(page)).filter((due) => due.dueDate < dateKey(startOfToday()));
    let expected = 0;
    for (const due of overdue) {
      const [guardian] = await getJson<{ notificationConsent: boolean }[]>(page, `/api/students/${due.studentId}/guardians`);
      if (guardian?.notificationConsent && due.studentName !== "Deniz Arslan") expected += 1;
    }
    const dialog = await chat(page, "Aidat günü gelenleri listele");
    await dialog.getByRole("button", { name: "Vadesi geçenlere hatırlatma" }).click();
    await dialog.getByRole("button", { name: `${overdue.length} hatırlatma gönder` }).click();
    await expect(dialog.getByText(`${expected}/${overdue.length} hatırlatma sıraya alındı.`, { exact: false })).toBeVisible();
    await expect(dialog.getByText(/velinin WhatsApp bildirim izni yok/)).toBeVisible();
    return `${expected}/${overdue.length} (izinli veli sayısına göre)`;
  });

  await op("O06", weekOf(startOfToday()), "Defne Eren'den ödeme al (karttaki bağlantı → Aidatlar)", "Aidatlar'daki ödeme penceresi açılır, tam tutar alınınca aidat Ödendi olur", async () => {
    const dialog = await chat(page, "Aidat günü gelenleri listele");
    await dialog.locator("li").filter({ hasText: "Defne Eren" }).getByRole("link", { name: "Ödeme al" }).click();
    const payment = page.getByRole("dialog", { name: "Ödeme al" });
    await payment.getByRole("button", { name: /al$/ }).click();
    await expect(page.getByText(/tahsil edildi/)).toBeVisible();
    const due = (await getJson<Due[]>(page, "/api/billing/dues")).find((item) => item.studentName === "Defne Eren");
    expect(due?.status).toBe("Paid");
    return `Defne Eren ${due?.status}`;
  });

  await op("O07", weekOf(startOfToday()), "Aidat günü gelenleri listele (tahsilattan sonra)", "Defne Eren listeden düşer", async () => {
    const dialog = await chat(page, "Aidat günü gelenleri listele");
    await expect(dialog.getByText("Deniz Arslan", { exact: true })).toBeVisible();
    await expect(dialog.getByText("Defne Eren", { exact: true })).toHaveCount(0);
    return "Defne Eren listede yok";
  });

  await op("O08", weekOf(monday), "Lara Arslan'ın pazartesi dersini 17:00'ye al (yalnız bu ders)", `${dayText(monday)} 16:00 dersi 17:00'ye taşınır; eski ders "taşındı" olur`, async () => {
    const dialog = await chat(page, "Lara Arslan'ın pazartesi dersini 17:00'ye al");
    await chooseOnce(dialog);
    await confirmMove(dialog);
    await expect(dialog.getByText(/saatine taşındı/)).toBeVisible();
    const lessons = await lessonsOf(page, "Lara Arslan", monday, addDays(monday, 1));
    expect(sameInstant(lessons.find((lesson) => lesson.status === "Normal")?.startAt, at(monday, 17))).toBeTruthy();
    expect(lessons.some((lesson) => lesson.status === "Rescheduled" && sameInstant(lesson.startAt, at(monday, 16)))).toBeTruthy();
    return describe(lessons);
  });

  await op("O09", weekOf(friday), "Lina Koç'un cuma dersini salı 17:30'a al", "Mert Yılmaz salı 17:30'da Emir Aydın'la dolu: çakışma uyarısı, onay yok, takvim değişmez", async () => {
    const before = await lessonsOf(page, "Lina Koç", startOfToday(), addDays(startOfToday(), 14));
    const dialog = await chat(page, "Lina Koç'un cuma dersini salı 17:30'a al");
    await chooseOnce(dialog);
    await expect(dialog.getByText(/çakışıyor/)).toBeVisible();
    await expect(dialog.getByText("Bu ders taşınacak")).toHaveCount(0);
    const after = await lessonsOf(page, "Lina Koç", startOfToday(), addDays(startOfToday(), 14));
    expect(describe(after)).toBe(describe(before));
    return "çakışma uyarısı, değişiklik yok";
  });

  await op("O10", weekOf(friday), "Mina Şahin'in dersini her hafta perşembe 16:00'ya taşı", `${dayText(friday)} dersinden itibaren program her perşembe 16:00; o tarihten sonra cuma dersi kalmaz`, async () => {
    const dialog = await chat(page, "Mina Şahin'in dersini her hafta perşembe 16:00'ya taşı");
    await expect(dialog.getByText("Haftalık program değişecek")).toBeVisible();
    await confirmMove(dialog);
    await expect(dialog.getByText(/programı .* oldu/)).toBeVisible();
    const lessons = (await lessonsOf(page, "Mina Şahin", friday, addDays(friday, 21))).filter((lesson) => lesson.status === "Normal");
    expect(lessons.length).toBeGreaterThanOrEqual(2);
    for (const lesson of lessons) {
      const start = new Date(lesson.startAt);
      expect(`${start.getDay()} ${start.getHours()}:${start.getMinutes()}`).toBe("4 16:0");
    }
    return describe(lessons);
  });

  await op("O11", weekOf(thursday), "Ege Kurt'un perşembe dersini yarım saat erkene al", `${dayText(thursday)} 19:30 dersi 19:00'a alınır`, async () => {
    const dialog = await chat(page, "Ege Kurt'un perşembe dersini yarım saat erkene al");
    await chooseOnce(dialog);
    await confirmMove(dialog);
    await expect(dialog.getByText(/saatine taşındı/)).toBeVisible();
    const lessons = (await lessonsOf(page, "Ege Kurt", thursday, addDays(thursday, 1))).filter((lesson) => lesson.status === "Normal");
    expect(sameInstant(lessons[0]?.startAt, at(thursday, 19))).toBeTruthy();
    return describe(lessons);
  });

  await op("O12", weekOf(startOfToday()), "Kerem Aksoy'un geçen salı dersi Mazeretli → Kerem'e telafi oluştur", "Telafi hakkı kullanılır, hakkın süresi içinde Makeup ders açılır", async () => {
    const past = (await lessonsOf(page, "Kerem Aksoy", addDays(startOfToday(), -10), startOfToday())).filter((lesson) => lesson.status !== "Cancelled").at(-1)!;
    const mark = await page.request.post(`${API}/api/lessons/${past.id}/attendance`, { data: { status: "Excused", note: "Simülasyon" } });
    expect(mark.ok()).toBeTruthy();
    const dialog = await chat(page, "Kerem'e telafi oluştur");
    const slot = dialog.getByRole("button", { name: /\d{2}:\d{2}$/ }).first();
    const slotText = await slot.innerText();
    await slot.click();
    await dialog.getByRole("button", { name: "Telafiyi aç" }).click();
    await expect(dialog.getByText(/telafi dersi açıldı/)).toBeVisible();
    const credits = await getJson<{ status: string; sourceLessonId: string; expiresAt: string; usedLessonId: string | null }[]>(page, `/api/students/${students.get("Kerem Aksoy")}/makeup-credits`);
    const credit = credits.find((item) => item.sourceLessonId === past.id);
    expect(credit?.status).toBe("Used");
    const makeup = (await lessonsOf(page, "Kerem Aksoy", startOfToday(), addDays(startOfToday(), 30))).find((lesson) => lesson.status === "Makeup");
    expect(makeup).toBeTruthy();
    expect(new Date(makeup!.startAt).getTime()).toBeLessThan(new Date(credit!.expiresAt).getTime());
    return `seçilen ${slotText}; ${describe([makeup!])}`;
  });

  await op("O13", weekOf(startOfToday()), "Onur Koç'un dersleri", "Onur Koç öğretmen: öğrenci Aras/Lina Koç için kart açılmaz, ders taşınmaz", async () => {
    const dialog = await chat(page, "Onur Koç'un dersleri");
    await expect(dialog.getByText("Aras Koç", { exact: true })).toHaveCount(0);
    await expect(dialog.getByText("Lina Koç", { exact: true })).toHaveCount(0);
    return "öğrenci kartı yok";
  });

  await op("O14", weekOf(startOfToday()), "Arslan'ın dersleri → \"Deniz olanın\"", "Model iki Arslan'ı sorar (kart açmaz); ikinci mesajda Deniz'in programı (her perşembe 17:30) kartta görünür", async () => {
    const dialog = await chat(page, "Arslan'ın dersleri");
    await expect(dialog.getByText(/Lara/).first()).toBeVisible();
    await expect(dialog.getByText(/Deniz/).first()).toBeVisible();
    await expect(dialog.getByText(/Her hafta Perşembe/)).toHaveCount(0);
    await say(page, dialog, "Deniz olanın");
    await expect(dialog.getByText(/Her hafta Perşembe 17:30/)).toBeVisible();
    return "önce soru, sonra program";
  });

  await op("O15", weekOf(fourNovember), "Ada Çetin'in 4 Kasım dersini cuma 18:00'e al", "Aynı haftanın cuması: 4 Kasım 18:00 → 6 Kasım Cuma 18:00", async () => {
    const dialog = await chat(page, "Ada Çetin'in 4 Kasım dersini cuma 18:00'e al");
    await chooseOnce(dialog);
    await expect(dialog.getByText("Bu ders taşınacak")).toBeVisible();
    await confirmMove(dialog);
    await expect(dialog.getByText(/saatine taşındı/)).toBeVisible();
    const lessons = await lessonsOf(page, "Ada Çetin", fourNovember, addDays(sixNovember, 1));
    expect(lessons.find((lesson) => lesson.status === "Normal" && sameInstant(lesson.startAt, at(sixNovember, 18)))).toBeTruthy();
    expect(lessons.find((lesson) => sameInstant(lesson.startAt, at(fourNovember, 18)))?.status).toBe("Rescheduled");
    return describe(lessons);
  });

  await op("O16", weekOf(holiday), "9 Kasım resmi tatil → Lara'nın dersini 9 Kasım'a taşı", "Tatil uyarısı, onay yok", async () => {
    const created = await page.request.post(`${API}/api/school-calendar-days`, { data: { date: dateKey(holiday), type: "Holiday", label: "Benchmark tatili" } });
    expect([200, 201, 409]).toContain(created.status());
    const dialog = await chat(page, "Lara'nın dersini 9 Kasım'a taşı");
    await chooseOnce(dialog);
    await expect(dialog.getByText(/resmi tatil \(Benchmark tatili\)/)).toBeVisible();
    await expect(dialog.getByText("Bu ders taşınacak")).toHaveCount(0);
    return "tatil uyarısı";
  });

  await op("O17", weekOf(thursday), "Deniz Arslan'ın perşembe dersini sabah 10'a al", "Ayşe Kaya perşembe 16:00–21:00 müsait: müsaitlik uyarısı, onay yok", async () => {
    const dialog = await chat(page, "Deniz Arslan'ın perşembe dersini sabah 10'a al");
    await chooseOnce(dialog);
    await expect(dialog.getByText(/müsait değil \(16:00–21:00\)/)).toBeVisible();
    await expect(dialog.getByText("Bu ders taşınacak")).toHaveCount(0);
    return "müsaitlik uyarısı";
  });

  await op("O18", weekOf(friday), "Lina Koç'un dersini her hafta salı 17:30'a al", "Her salı 17:30 Emir Aydın'la çakışır: uyarı + haftalık boş saat önerileri", async () => {
    const dialog = await chat(page, "Lina Koç'un dersini her hafta salı 17:30'a al");
    await expect(dialog.getByText(/çakışıyor|başka dersi var/)).toBeVisible();
    await expect(dialog.getByRole("button", { name: /^Her / }).first()).toBeVisible();
    await expect(dialog.getByText("Haftalık program değişecek")).toHaveCount(0);
    return "uyarı + öneriler";
  });

  await op("O19", weekOf(addDays(startOfToday(), 14)), "Mina Şahin'in dersleri", "Kart yeni programı gösterir: her perşembe 16:00", async () => {
    const dialog = await chat(page, "Mina Şahin'in dersleri");
    await expect(dialog.getByText(/Her hafta Perşembe 16:00/)).toBeVisible();
    return "Her hafta Perşembe 16:00";
  });

  await op("O20", weekOf(saturday), "Bora'nın dersini bir saat erkene çek", `${dayText(saturday)} 19:30 dersi 18:30'a alınır`, async () => {
    const dialog = await chat(page, "Bora'nın dersini bir saat erkene çek");
    await chooseOnce(dialog);
    await confirmMove(dialog);
    await expect(dialog.getByText(/saatine taşındı/)).toBeVisible();
    const lessons = (await lessonsOf(page, "Bora Aydın", saturday, addDays(saturday, 1))).filter((lesson) => lesson.status === "Normal");
    expect(sameInstant(lessons[0]?.startAt, at(saturday, 18, 30))).toBeTruthy();
    return describe(lessons);
  });

  await op("O21", weekOf(startOfToday()), "Ada'ya telafi oluştur", "Kullanılabilir hak yok mesajı", async () => {
    const dialog = await chat(page, "Ada'ya telafi oluştur");
    // Kart ya da modelin cevabı: ikisi de hakkın olmadığını söyler; telafi dersi açılmaz.
    await expect(dialog.getByText(/telafi hakkı (yok|görünmüyor|bulunmuyor)/).first()).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Telafiyi aç" })).toHaveCount(0);
    return "hak yok";
  });

  await op("O22", weekOf(startOfToday()), "Kerem Aksoy'un aidat durumu", "Kısmi ödenmiş Ekim aidatının kalanı (₺4.000) Ödenmedi olarak görünür", async () => {
    const dialog = await chat(page, "Kerem Aksoy'un aidat durumu");
    await expect(dialog.getByText(money(4000)).first()).toBeVisible();
    await expect(dialog.getByText("Ödenmedi", { exact: true })).toBeVisible();
    return money(4000);
  });

  await op("O23", weekOf(startOfToday()), "Kerem'in kalan borcunu al (karttaki bağlantı)", "Pencere kalan tutarla (₺4.000) açılır, alınca aidat Ödendi", async () => {
    const dialog = await chat(page, "Kerem Aksoy'un aidat durumu");
    await dialog.getByRole("link", { name: "Ödeme al" }).click();
    const payment = page.getByRole("dialog", { name: "Ödeme al" });
    await expect(payment.getByRole("button", { name: `${money(4000)} al` })).toBeVisible();
    await payment.getByRole("button", { name: `${money(4000)} al` }).click();
    await expect(page.getByText(/tahsil edildi/)).toBeVisible();
    const due = (await getJson<Due[]>(page, "/api/billing/dues")).find((item) => item.studentName === "Kerem Aksoy");
    expect(due?.status).toBe("Paid");
    return `Kerem Aksoy ${due?.status}`;
  });

  await op("O24", weekOf(startOfToday()), "Kerem'in borcu var mı", "Ödenmemiş aidat yok", async () => {
    const dialog = await chat(page, "Kerem'in borcu var mı");
    await expect(dialog.getByText(/(borcu|ödenmemiş aidat\w*|açık aidat\w*) (yok|bulunmuyor|görünmüyor)/).first()).toBeVisible();
    await expect(dialog.getByText(money(4000))).toHaveCount(0);
    return "borç yok";
  });

  await op("O25", weekOf(startOfToday()), "Fiyat politikası nerede → ekrana git", "Aidatlar ekranı Fiyat politikası sekmesiyle açılır", async () => {
    const dialog = await chat(page, "Fiyat politikası nerede");
    await dialog.getByRole("link", { name: "Fiyat politikası ekranına git" }).click();
    await expect(page.getByText("Ücret tarifesi", { exact: true })).toBeVisible();
    return "Fiyat politikası açık";
  });

  // Örnek verideki haftalık seriler 24 Kasım'da biter; ay sonu işlemleri o tarihe kadar.
  const nineteenNovember = new Date(2026, 10, 19), seventeenNovember = new Date(2026, 10, 17), eighteenNovember = new Date(2026, 10, 18);

  await op("O26", weekOf(nineteenNovember), "Deniz Arslan'ın 19 Kasım dersini 18:00'e al", "19 Kasım 17:30 dersi 18:00'e; önceki perşembe değişmez", async () => {
    const dialog = await chat(page, "Deniz Arslan'ın 19 Kasım dersini 18:00'e al");
    await chooseOnce(dialog);
    await confirmMove(dialog);
    await expect(dialog.getByText(/saatine taşındı/)).toBeVisible();
    const lessons = await lessonsOf(page, "Deniz Arslan", addDays(nineteenNovember, -7), addDays(nineteenNovember, 1));
    expect(lessons.filter((lesson) => lesson.status === "Normal").map((lesson) => new Date(lesson.startAt).getTime())).toEqual([at(addDays(nineteenNovember, -7), 17, 30), at(nineteenNovember, 18)].map((date) => date.getTime()));
    expect(lessons.find((lesson) => sameInstant(lesson.startAt, at(nineteenNovember, 17, 30)))?.status).toBe("Rescheduled");
    return describe(lessons);
  });

  await op("O27", weekOf(seventeenNovember), "Emir Aydın'ın 17 Kasım dersinden itibaren her hafta çarşamba 17:00'ye al", "10 Kasım salı dersi yerinde kalır; 17 Kasım salı dersi kalkar, 18 Kasım'dan itibaren her çarşamba 17:00", async () => {
    const dialog = await chat(page, "Emir Aydın'ın 17 Kasım dersinden itibaren her hafta çarşamba 17:00'ye al");
    await expect(dialog.getByText("Haftalık program değişecek")).toBeVisible();
    await confirmMove(dialog);
    await expect(dialog.getByText(/programı .* oldu/)).toBeVisible();
    const lessons = (await lessonsOf(page, "Emir Aydın", addDays(seventeenNovember, -7), addDays(seventeenNovember, 9))).filter((lesson) => lesson.status === "Normal");
    expect(lessons.some((lesson) => sameInstant(lesson.startAt, at(addDays(seventeenNovember, -7), 17, 30)))).toBeTruthy();
    expect(lessons.some((lesson) => sameInstant(lesson.startAt, at(eighteenNovember, 17)))).toBeTruthy();
    expect(lessons.some((lesson) => sameInstant(lesson.startAt, at(seventeenNovember, 17, 30)))).toBeFalsy();
    return describe(lessons);
  });

  await op("O28", weekOf(addDays(startOfToday(), 14)), "Lina Koç'un son üç haftadaki iki dersi Mazeretli → Lina'ya telafi oluştur", "İki hak listelenir; süresi en erken dolan (eski dersin hakkı) kullanılır, diğeri kalır", async () => {
    // Örnek veride bazı dersler hakkı açılmadan "Mazeretli" yazılmıştır; aynı durumu tekrar
    // işaretlemek değişiklik sayılmaz. Bu yüzden henüz mazeretli olmayan dersler seçilir.
    const past = (await lessonsOf(page, "Lina Koç", addDays(startOfToday(), -20), startOfToday()))
      .filter((lesson) => (lesson.status === "Normal" || lesson.status === "Completed") && lesson.attendanceStatus !== "Excused")
      .slice(-2);
    expect(past).toHaveLength(2);
    for (const lesson of past) expect((await page.request.post(`${API}/api/lessons/${lesson.id}/attendance`, { data: { status: "Excused", note: "Simülasyon" } })).ok()).toBeTruthy();
    const dialog = await chat(page, "Lina'ya telafi oluştur");
    await expect(dialog.getByRole("button", { name: "Bu hakkı kullan" })).toHaveCount(2);
    await dialog.getByRole("button", { name: /\d{2}:\d{2}$/ }).first().click();
    await dialog.getByRole("button", { name: "Telafiyi aç" }).click();
    await expect(dialog.getByText(/telafi dersi açıldı/)).toBeVisible();
    const credits = await getJson<{ status: string; sourceLessonId: string }[]>(page, `/api/students/${students.get("Lina Koç")}/makeup-credits`);
    expect(credits.find((credit) => credit.sourceLessonId === past[0]!.id)?.status).toBe("Used");
    expect(credits.find((credit) => credit.sourceLessonId === past[1]!.id)?.status).toBe("Available");
    return `eski hak kullanıldı, yeni hak duruyor`;
  });

  await op("O29", weekOf(holiday), "Lara Arslan'ın dersleri (9 Kasım tatil, ders önceden planlı)", "Kart \"o gün ders yok\" demez; tatile rağmen planlı ders olduğunu söyler", async () => {
    const planned = (await lessonsOf(page, "Lara Arslan", holiday, addDays(holiday, 1))).filter((lesson) => lesson.status === "Normal");
    const dialog = await chat(page, "Lara Arslan'ın dersleri");
    if (planned.length) await expect(dialog.getByText(/resmi tatil \(Benchmark tatili\) ama o gün planlı dersi görünüyor/)).toBeVisible();
    else await expect(dialog.getByText(/resmi tatil \(Benchmark tatili\); o gün ders yok/)).toBeVisible();
    return planned.length ? "tatilde planlı ders uyarısı" : "tatil, ders yok";
  });

  await op("O31", weekOf(new Date(2026, 10, 26)), "Deniz Arslan'ın 26 Kasım dersini 18:00'e al (seri 24 Kasım'da bitiyor)", "Söylenen günü anarak o gün ders olmadığını söyler; onay yok", async () => {
    const dialog = await chat(page, "Deniz Arslan'ın 26 Kasım dersini 18:00'e al");
    // Model ya kendisi söyler ya da kartı açar; kart o günü anarak ders olmadığını söyler.
    await expect(dialog.getByText(/26 Kasım/).first()).toBeVisible();
    await expect(dialog.getByText(/(ders|program)\w* (yok|görünmüyor|bulunmuyor)|planlı ders\w* (yok|görünmüyor)/).first()).toBeVisible();
    await expect(dialog.getByText("Bu ders taşınacak")).toHaveCount(0);
    return "26 Kasım'da ders yok mesajı";
  });

  await op("O30", weekOf(addDays(startOfToday(), 28)), "Ay sonu: Aidat günü gelenleri listele", "Bir ayın tahsilatlarından sonra liste ve toplam API ile birebir aynı", async () => {
    const dues = await openDues(page);
    const total = dues.reduce((sum, due) => sum + due.amount - due.totalPaid, 0);
    const dialog = await chat(page, "Aidat günü gelenleri listele");
    await expect(dialog.getByText(money(total)).first()).toBeVisible();
    for (const due of dues) await expect(dialog.getByText(due.studentName, { exact: true })).toBeVisible();
    for (const paid of ["Defne Eren", "Kerem Aksoy"]) await expect(dialog.getByText(paid, { exact: true })).toHaveCount(0);
    return `${dues.length} aidat, ${money(total)}`;
  });

  const passed = rows.filter((row) => row.passed).length;
  const report = [
    "# Asistan - bir aylık işlem simülasyonu",
    "",
    `Tarih: ${new Date().toLocaleString("tr-TR")} · ${passed}/${rows.length} işlem beklenen sonucu verdi`,
    "",
    "| # | Hafta | İstek | Beklenen | Gerçekleşen | Sonuç |",
    "|---|---|---|---|---|---|",
    ...rows.map((row) => `| ${row.id} | ${row.week} | ${row.request} | ${row.expected} | ${row.actual.replaceAll("|", "/")} | ${row.passed ? "✓" : "✗"} |`),
  ].join("\n");
  mkdirSync(RESULTS_DIR, { recursive: true });
  writeFileSync(join(RESULTS_DIR, "month-latest.md"), `${report}\n`);
  console.log(report);
  expect(rows.filter((row) => !row.passed).map((row) => row.id)).toEqual([]);
});
