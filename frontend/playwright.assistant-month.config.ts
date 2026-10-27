import { defineConfig, devices } from "@playwright/test";

// Asistanın bir aylık işlem simülasyonu (e2e/assistant-month.sim.ts). Varsayılan e2e koşusuna
// girmez: gerçek API + örnek okul verisi ister ve veriyi değiştirir. Çalıştırma:
// tools/assistant-benchmark/README.md.
export default defineConfig({
  testDir: "./e2e",
  testMatch: "assistant-month.sim.ts",
  timeout: 20 * 60_000,
  // Kartlar dil modelinin cevabından sonra açılır; beklemeler gerçek model süresine göre.
  expect: { timeout: 20_000 },
  workers: 1,
  reporter: "list",
  outputDir: ".playwright-artifacts/assistant-month",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3001",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    ...devices["Desktop Chrome"],
  },
});
