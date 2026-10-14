import { expect, test } from "@playwright/test";

// Hata bildirimi (2026-10-14): "Veliye seçenekleri kopyala" her günden yalnızca EN ERKEN boş
// saati yazıyordu; Cuma 16-18 ve Cumartesi 10-11 boşken mesajda görünmüyordu. Mesaj artık her
// günün boş aralıklarını yazar.
test("the guardian message lists every free range of each day, not just the earliest start", async ({ page }) => {
  // 15 dakikalık adımlarla boş başlangıçlar (sunucunun döndüğü biçim), 45 dakikalık ders.
  const starts = (day: string, from: string, to: string) => {
    const result: { dayOfWeek: string; startTime: string; firstOpenDate: string }[] = [];
    const [fh, fm] = from.split(":").map(Number);
    const [th, tm] = to.split(":").map(Number);
    for (let minutes = fh * 60 + fm; minutes <= th * 60 + tm; minutes += 15) {
      const time = `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}:00`;
      result.push({ dayOfWeek: day, startTime: time, firstOpenDate: "2026-10-16" });
    }
    return result;
  };
  const teacher = (id: string, slots: ReturnType<typeof starts>) => ({
    teacherId: id, teacherName: `Öğretmen ${id}`, instrumentIds: ["guitar"], weeklyLessonCount: 3,
    weeklyAvailableMinutes: 600, weeklyBookedMinutes: 120, upcomingTimeOff: [], slots,
  });

  await page.route("**/api/**", async (route) => {
    const pathname = new URL(route.request().url()).pathname;
    const json = (body: unknown) => route.fulfill({ json: body });
    if (pathname === "/api/auth/me") return json({ id: "admin-1", email: "admin@example.com", role: "Admin", mustChangePassword: false });
    if (pathname === "/api/me/notifications") return json({ items: [], unreadCount: 0 });
    if (pathname === "/api/instruments") return json([{ id: "guitar", code: "GUITAR", name: "Gitar" }]);
    if (pathname === "/api/teachers") return json([{ id: "t1", firstName: "A", lastName: "B", status: "Active", instrumentIds: ["guitar"] }]);
    if (pathname === "/api/scheduling/open-slots") {
      return json({
        from: "2026-10-14", durationMinutes: 45, teachersWithoutAvailability: [],
        teachers: [
          // İki öğretmenin boşlukları gün içinde birleşir; çakışan başlangıçlar tek sayılır.
          teacher("t1", [...starts("Friday", "15:00", "16:30"), ...starts("Saturday", "09:00", "11:15")]),
          teacher("t2", [...starts("Friday", "16:00", "18:15"), ...starts("Thursday", "17:45", "17:45")]),
        ],
      });
    }
    return json({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  await page.goto("/dashboard/availability");
  await page.getByRole("button", { name: "Gitar", exact: true }).click();
  await page.getByRole("button", { name: "Veliye seçenekleri kopyala" }).click();

  await expect(page.getByRole("status").filter({ hasText: "uygun saatlerimiz" })).toContainText(
    "Gitar için uygun saatlerimiz: Perşembe 17:45, Cuma 15:00–19:00 arası, Cumartesi 09:00–12:00 arası. Hangisi size uygun?",
  );
});
