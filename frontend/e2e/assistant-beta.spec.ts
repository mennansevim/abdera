import { expect, test } from "@playwright/test";

test("beta assistant reads makeup credits and only collects approval", async ({ page }) => {
  const mutatingRequests: string[] = [];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (!["GET", "HEAD", "OPTIONS"].includes(request.method())) mutatingRequests.push(`${request.method()} ${pathname}`);

    if (pathname === "/api/auth/me") {
      return route.fulfill({ json: { id: "admin-1", email: "admin@example.com", role: "Admin", mustChangePassword: false } });
    }
    if (pathname === "/api/me/notifications") return route.fulfill({ json: { items: [], unreadCount: 0 } });
    if (pathname === "/api/students") {
      return route.fulfill({ json: [{ id: "student-1", firstName: "Ahmet", lastName: "Yılmaz", birthDate: "2012-05-14", status: "Active", siblingDiscount: false }] });
    }
    if (pathname === "/api/students/student-1/makeup-credits") {
      return route.fulfill({ json: [{ id: "credit-1", studentId: "student-1", sourceLessonId: "lesson-1", earnedReason: "Excused", earnedAt: "2026-10-20T10:00:00Z", expiresAt: "2026-11-10T10:00:00Z", status: "Available", usedLessonId: null, sourceLessonStartAt: "2026-10-20T10:00:00Z" }] });
    }
    if (pathname === "/api/bank-transactions") return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 1 } });
    if (pathname === "/api/notifications") return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 1 } });
    return route.fulfill({ json: [] });
  });

  await page.goto("/dashboard");
  await page.getByRole("button", { name: "Abdera Asistanı beta" }).click();
  await page.getByLabel("Asistana yaz").fill("Ahmet Yılmaz'ın telafisini uygula");
  await page.getByRole("button", { name: "Mesajı gönder" }).click();

  await expect(page.getByText(/1 telafi hakkından en erken sona erecek olanı kullanma talebi hazırlanacak/)).toBeVisible();
  await page.getByRole("button", { name: "Talebi onayla" }).click();
  await expect(page.getByText(/İşleminiz onay alındıktan sonra gerçekleştirilecektir/)).toBeVisible();
  await expect(page.getByText(/herhangi bir değişiklik yapılmadı/)).toBeVisible();
  expect(mutatingRequests).toEqual([]);
});

test("beta assistant links to the selected student's remaining lessons", async ({ page }) => {
  await page.route("**/api/**", async (route) => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname === "/api/auth/me") return route.fulfill({ json: { id: "admin-1", email: "admin@example.com", role: "Admin", mustChangePassword: false } });
    if (pathname === "/api/me/notifications") return route.fulfill({ json: { items: [], unreadCount: 0 } });
    if (pathname === "/api/students") return route.fulfill({ json: [{ id: "student-1", firstName: "Ahmet", lastName: "Yılmaz", birthDate: "2012-05-14", status: "Active", siblingDiscount: false }] });
    if (pathname === "/api/bank-transactions") return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 1 } });
    if (pathname === "/api/notifications") return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 1 } });
    return route.fulfill({ json: [] });
  });

  await page.goto("/dashboard");
  await page.getByRole("button", { name: "Abdera Asistanı beta" }).click();
  await page.getByLabel("Asistana yaz").fill("Ahmet Yılmaz'ın kalan derslerine git");
  await page.getByRole("button", { name: "Mesajı gönder" }).click();

  const link = page.getByRole("link", { name: /Kalan derslere git/ });
  await expect(link).toHaveAttribute("href", "/dashboard/students/analysis?studentId=student-1");
});

test("beta assistant asks whether a lesson move is one-time or recurring", async ({ page }) => {
  const mutatingRequests: string[] = [];
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (!["GET", "HEAD", "OPTIONS"].includes(request.method())) mutatingRequests.push(`${request.method()} ${pathname}`);
    if (pathname === "/api/auth/me") return route.fulfill({ json: { id: "admin-1", email: "admin@example.com", role: "Admin", mustChangePassword: false } });
    if (pathname === "/api/me/notifications") return route.fulfill({ json: { items: [], unreadCount: 0 } });
    if (pathname === "/api/students") return route.fulfill({ json: [{ id: "student-1", firstName: "Ahmet", lastName: "Yılmaz", birthDate: "2012-05-14", status: "Active", siblingDiscount: false }] });
    if (pathname === "/api/bank-transactions") return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 1 } });
    if (pathname === "/api/notifications") return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 1 } });
    return route.fulfill({ json: [] });
  });

  await page.goto("/dashboard");
  await page.getByRole("button", { name: "Abdera Asistanı beta" }).click();
  await page.getByLabel("Asistana yaz").fill("Ahmet Yılmaz'ın dersini perşembe 14:00'a taşı");
  await page.getByRole("button", { name: "Mesajı gönder" }).click();

  await expect(page.getByText(/bir kerelik mi, yoksa sonraki tüm dersler için sürekli mi/)).toBeVisible();
  await page.getByRole("button", { name: "Bir kerelik" }).click();
  await expect(page.getByText(/bir kerelik talep olarak hazırlanacak/)).toBeVisible();
  await page.getByRole("button", { name: "Talebi onayla" }).click();
  await expect(page.getByText(/İşleminiz onay alındıktan sonra gerçekleştirilecektir/)).toBeVisible();
  expect(mutatingRequests).toEqual([]);
});
