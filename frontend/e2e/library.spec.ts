import { expect, test } from "@playwright/test";
import archive from "../src/data/sheet-music.json";
import { buildLibrary, initialMusicFilters, matchesMusic } from "../src/lib/sheet-music";

test.use({ channel: process.env.E2E_CHROME_CHANNEL });
const catalogue = buildLibrary(null, archive);

test("catalogue contains over 1200 unique licensed downloads and honest level suggestions", () => {
  expect(archive.count).toBeGreaterThan(1200);
  expect(archive.items).toHaveLength(archive.count);
  expect(new Set(archive.items.map(p => p.id)).size).toBe(archive.count);
  expect(new Set(archive.items.map(p => p.downloadUrl)).size).toBe(archive.count);
  for (const piece of archive.items) {
    expect(new URL(piece.downloadUrl).hostname).toBe("www.mutopiaproject.org");
    expect(piece.downloadUrl).toMatch(/(?:\.pdf|-pdfs\.zip)$/);
    expect(piece.license).toMatch(/Public Domain|Creative Commons/);
    expect(piece.sourceUrl).toMatch(/^https:\/\/www\.mutopiaproject\.org\/cgibin\/piece-info.cgi\?id=\d+$/);
  }
  for (let level = 1; level <= 5; level++) {
    const graded = catalogue.filter(p => matchesMusic(p, { ...initialMusicFilters, level: String(level) }));
    expect(graded.length).toBeGreaterThan(0);
    expect(graded.every(p => !!p.levelBasis)).toBeTruthy();
  }
  expect(catalogue.filter(p => p.level === null).length).toBeGreaterThan(1200);
  expect(catalogue.every(p => p.source === "mutopia")).toBeTruthy();
});

test("public search, combined filters, pagination, score details and saved list", async ({ page }) => {
  const errors: string[] = [];
  const apiRequests: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("request", request => { if (new URL(request.url()).pathname.startsWith("/api/")) apiRequests.push(request.url()); });
  await page.goto("/kutuphane");
  await expect(page.getByRole("heading", { name: "Kütüphane", exact: true })).toBeVisible();
  await expect(page.getByRole("listitem")).toHaveCount(24);
  const firstTitle = await page.getByRole("listitem").first().innerText();
  await page.getByRole("button", { name: "Sonraki sayfa" }).click();
  expect(await page.getByRole("listitem").first().innerText()).not.toBe(firstTitle);
  await page.getByRole("button", { name: /^Piyano / }).click();
  await page.getByRole("combobox", { name: "Seviye", exact: true }).selectOption("1");
  await page.getByRole("combobox", { name: "Koleksiyon", exact: true }).selectOption("education");
  await expect(page.getByRole("listitem")).toHaveCount(10);
  const row = page.getByRole("listitem").first();
  await row.getByRole("button", { name: /listeme ekle/ }).click();
  await row.getByRole("button", { name: /notayı aç/ }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("link", { name: "PDF notayı aç", exact: true })).toHaveAttribute("href", /mutopiaproject.*\.pdf$/);
  await expect(page.getByRole("dialog")).toContainText("Creative Commons");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).not.toBeVisible();
  await expect(page.getByRole("combobox", { name: "Seviye", exact: true })).toHaveValue("1");
  await page.reload();
  await page.getByRole("button", { name: /^Çalışma listem/ }).click();
  await expect(page.getByRole("listitem")).toHaveCount(1);
  await page.getByRole("button", { name: "Keşfet", exact: true }).click();
  await page.getByRole("searchbox").fill("Burgmuller");
  await expect(page.getByRole("listitem").first()).toContainText("Burgmüller");
  await page.getByRole("searchbox").fill("no-matching-score-xyz");
  await expect(page.getByRole("heading", { name: "Bu filtrelerle nota bulunamadı" })).toBeVisible();
  await page.getByRole("button", { name: "Tüm notaları göster", exact: true }).click();
  await expect(page.getByRole("listitem")).toHaveCount(24);
  expect(errors).toEqual([]);
  expect(apiRequests).toEqual([]);
});

test("mobile filters, modal, deep link and ZIP download remain usable", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await page.goto("/kutuphane");
  await page.getByRole("button", { name: /^Keman / }).click();
  await page.getByRole("button", { name: "Filtre", exact: true }).click();
  await page.getByRole("combobox", { name: "Seviye", exact: true }).selectOption("5");
  await page.getByLabel("Yalnız solo").check();
  await page.getByRole("button", { name: /^Filtre/ }).click();
  await expect(page.getByRole("listitem").first()).toContainText("Paganini");
  for (const width of [320, 390, 736]) {
    await page.setViewportSize({ width, height: 740 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  }
  await page.getByRole("listitem").first().getByRole("button", { name: /notayı aç/ }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.reload();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.keyboard.press("Escape");
  const zip = archive.items.find(p => p.format === "zip")!;
  await page.goto(`/kutuphane?eser=${zip.id}`);
  await expect(page.getByRole("link", { name: "Nota paketini indir (ZIP)", exact: true })).toHaveAttribute("href", zip.downloadUrl);
  await expect(page.getByRole("button", { name: "Burada önizle" })).toHaveCount(0);
});

test("school sidebar collapses without clearing the library filters", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.route("**/api/**", route => {
    const pathname = new URL(route.request().url()).pathname;
    const body = pathname === "/api/auth/me" ? { id: "test-admin", email: "test@abdera.local", role: "Admin" }
      : /bank-transactions|notifications/.test(pathname) ? { items: [], totalCount: 0 }
      : [];
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/library");
  await expect(page.getByRole("heading", { name: "Kütüphane", exact: true })).toBeVisible();
  await page.getByRole("searchbox").fill("Diabelli");
  // Menü daraltılınca kaybolmaz, ikon şeridine iner; arama metni korunmalı.
  await page.getByRole("button", { name: "Menüyü daralt", exact: true }).click();
  await expect(page.getByRole("button", { name: "Menüyü genişlet", exact: true })).toHaveAttribute("aria-expanded", "false");
  await expect(page.getByRole("searchbox")).toHaveValue("Diabelli");
  await page.getByRole("button", { name: "Menüyü genişlet", exact: true }).click();
  await expect(page.getByRole("button", { name: "Menüyü daralt", exact: true })).toHaveAttribute("aria-expanded", "true");
  expect(errors).toEqual([]);
});
