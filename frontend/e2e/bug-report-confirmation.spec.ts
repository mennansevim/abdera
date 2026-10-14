import { expect, test } from "@playwright/test";

// W6: görev canlıya çıkınca bildirim "Çözüldü" olur; bildirimi yapan Hata bildir sayfasında
// "Onaylandı / Hâlâ hatalı" diye teyit eder.
test("the reporter confirms a resolved report or sends it back as still broken", async ({ page }) => {
  const confirmations: { id: string; works: boolean; note: string | null }[] = [];
  const report = (id: string, description: string) => ({
    id, kind: "Bug", pagePath: "/dashboard/calendar", description, userAgent: null, appVersion: "2026.10.13",
    reporterName: null, reporterEmail: null, status: "Resolved", githubIssueNumber: 22, triageNote: null,
    createdAt: "2026-10-13T09:38:00Z", resolvedAt: "2026-10-13T12:00:00Z", feedbackNote: null,
  });
  let awaiting = [report("report-1", "Telafi saati seçilemiyor."), report("report-2", "Takvimde ders kayboluyor.")];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (pathname === "/api/auth/me") {
      await route.fulfill({ json: { id: "user-1", email: "teacher@example.com", role: "Teacher", mustChangePassword: false, teacherId: "teacher-1", instrumentIds: [] } });
      return;
    }
    if (pathname === "/api/me/notifications") {
      await route.fulfill({ json: { items: [], unreadCount: 0 } });
      return;
    }
    if (pathname === "/api/bug-reports/awaiting-me") {
      await route.fulfill({ json: awaiting });
      return;
    }
    const confirmation = pathname.match(/^\/api\/bug-reports\/([^/]+)\/confirmation$/);
    if (confirmation && request.method() === "POST") {
      confirmations.push({ id: confirmation[1], ...request.postDataJSON() });
      awaiting = awaiting.filter((item) => item.id !== confirmation[1]);
      await route.fulfill({ status: 204, body: "" });
      return;
    }
    await route.fulfill({ json: [] });
  });

  await page.goto("/dashboard/bug-report");
  await expect(page.getByText("Çözüldü, teyidini bekliyoruz", { exact: true })).toBeVisible();

  const first = page.getByRole("listitem").filter({ hasText: "Telafi saati seçilemiyor." });
  await first.getByRole("button", { name: "Onaylandı" }).click();
  await expect(first).toHaveCount(0);

  const second = page.getByRole("listitem").filter({ hasText: "Takvimde ders kayboluyor." });
  await second.getByRole("button", { name: "Hâlâ hatalı" }).click();
  await second.getByLabel("Ne hâlâ çalışmıyor? (isteğe bağlı)").fill("Cuma günü yine kayboldu.");
  await second.getByRole("button", { name: "Gönder" }).click();
  await expect(page.getByText("Çözüldü, teyidini bekliyoruz", { exact: true })).toHaveCount(0);

  expect(confirmations).toEqual([
    { id: "report-1", works: true, note: null },
    { id: "report-2", works: false, note: "Cuma günü yine kayboldu." },
  ]);
});

// W7: ne istendiği anlaşılamayan bildirimde bildirimi yapana soru gösterilir; cevabı aynı
// sayfadan yazar, bildirim yeniden incelemeye düşer.
test("the reporter answers the question on an unclear report", async ({ page }) => {
  const sent: { id: string; details: string }[] = [];
  let awaiting = [{
    id: "report-9", kind: "Bug", pagePath: "/dashboard", description: "Perşembe 15.00 ile cuma 15.00 öğrencileri yer değişsin.",
    userAgent: null, appVersion: "2026.10.14", reporterName: null, reporterEmail: null, status: "NeedsInfo",
    githubIssueNumber: null, triageNote: "Hangi öğrenciler, hangi tarihten itibaren?", createdAt: "2026-10-14T12:03:00Z",
    resolvedAt: null, feedbackNote: null,
  }];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (pathname === "/api/auth/me") return route.fulfill({ json: { id: "user-1", email: "teacher@example.com", role: "Teacher", mustChangePassword: false, teacherId: "teacher-1", instrumentIds: [] } });
    if (pathname === "/api/me/notifications") return route.fulfill({ json: { items: [], unreadCount: 0 } });
    if (pathname === "/api/bug-reports/awaiting-me") return route.fulfill({ json: awaiting });
    const details = pathname.match(/^\/api\/bug-reports\/([^/]+)\/details$/);
    if (details && request.method() === "POST") {
      sent.push({ id: details[1], ...request.postDataJSON() });
      awaiting = [];
      return route.fulfill({ status: 204, body: "" });
    }
    return route.fulfill({ json: [] });
  });

  await page.goto("/dashboard/bug-report");
  await expect(page.getByText("Bildirimini anlayamadık, biraz detay verir misin?")).toBeVisible();
  await expect(page.getByText("Soru: Hangi öğrenciler, hangi tarihten itibaren?")).toBeVisible();
  await page.getByLabel("Cevabın").fill("Ali ile Ayşe, gelecek haftadan itibaren.");
  await page.getByRole("button", { name: "Detayı gönder" }).click();
  await expect(page.getByText("Bildirimini anlayamadık, biraz detay verir misin?")).toHaveCount(0);
  expect(sent).toEqual([{ id: "report-9", details: "Ali ile Ayşe, gelecek haftadan itibaren." }]);
});
