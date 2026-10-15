// Modül seçimi kuralları (okul uygulaması Shared/Modules/ModuleCatalog.cs ve platform
// Schools.ValidateModules ile aynı): çekirdek modüller her zaman açıktır ve istekte GÖNDERİLMEZ;
// seçilen opsiyonel modülün bağımlılıkları da seçili olmalıdır. Katalog sürüme bağlıdır
// (/api/releases/{v}/modules), bu yüzden kurallar sabit liste yerine katalogdan hesaplanır.
import type { ModuleEntry } from "./releases";

// Modüllerin kısa açıklamaları (sihirbaz kartları). Katalogda olmayan anahtar açıklamasız görünür.
export const MODULE_DESCRIPTIONS: Record<string, string> = {
  "core.people": "Öğrenci, veli ve öğretmen kayıtları.",
  "core.scheduling": "Takvim, seri dersler ve ders değişiklikleri.",
  "core.attendance": "Yoklama, mazeret ve telafi hakları.",
  "core.billing": "Aylık aidat, indirimler ve tahsilat.",
  "core.notifications": "Personele ekran içi bildirimler.",
  "core.ops": "Sağlık denetimi ve günlük yedek.",
  progress: "Öğrenci gelişim notları ve dönem karnesi.",
  library: "Nota ve kaynak kütüphanesi.",
  show: "Yıl sonu gösterisi programı ve sahne akışı.",
  banking: "Sanal IBAN ile gelen havalenin aidata otomatik işlenmesi.",
  groups: "Grup dersleri; grup tarifesi ve öğretmen gelir payı.",
  finance: "Gelir-gider defteri ve haftalık öğretmen ödemeleri.",
  profitability: "Aylık kârlılık ve büyüme önerileri.",
  whatsapp: "Veliye WhatsApp ile ders/aidat hatırlatmaları (Meta hesabı gerekir).",
  email: "SMTP üzerinden e-posta hatırlatmaları.",
  ai: "Gelişim ve kârlılık ekranlarında yapay zekâ yorumları (API anahtarı gerekir).",
  "parent-portal": "Velinin kendi çocuğunun ders ve aidatını gördüğü portal.",
};

// Bir modülün (dolaylı dahil) bütün bağımlılıkları.
export function dependenciesOf(key: string, catalog: ModuleEntry[], seen = new Set<string>()): Set<string> {
  const entry = catalog.find((m) => m.key === key);
  for (const required of entry?.requires ?? []) {
    if (!seen.has(required)) {
      seen.add(required);
      dependenciesOf(required, catalog, seen);
    }
  }
  return seen;
}

// Seçili opsiyonel modüllerden hangileri `key`'e bağımlı (kapatmayı engelleyenler).
export function dependentsOf(key: string, selected: Set<string>, catalog: ModuleEntry[]): ModuleEntry[] {
  return catalog.filter((m) => !m.isCore && selected.has(m.key) && m.key !== key && dependenciesOf(m.key, catalog).has(key));
}

// Modülü aç: opsiyonel bağımlılıkları da açılır. Dönen ikinci değer otomatik açılanlar (bilgi notu için).
export function enableModule(key: string, selected: Set<string>, catalog: ModuleEntry[]): { next: Set<string>; added: string[] } {
  const next = new Set(selected);
  next.add(key);
  const added: string[] = [];
  for (const dependency of dependenciesOf(key, catalog)) {
    const entry = catalog.find((m) => m.key === dependency);
    if (entry && !entry.isCore && !next.has(dependency)) {
      next.add(dependency);
      added.push(dependency);
    }
  }
  return { next, added };
}

// Katalogda olmayan ya da çekirdek anahtarları ayıklar - istek yalnızca opsiyonel modül taşır.
export function optionalOnly(selected: Iterable<string>, catalog: ModuleEntry[]): string[] {
  const optional = new Set(catalog.filter((m) => !m.isCore).map((m) => m.key));
  return [...selected].filter((key) => optional.has(key));
}

// Entegrasyon anahtarları (platform Provisioning/SchoolEnvBuilder.AllowedIntegrationKeys).
export interface IntegrationField {
  key: string;
  label: string;
  hint?: string;
  secret: boolean;
  optional?: boolean;
}

export const INTEGRATION_FIELDS: Record<string, IntegrationField[]> = {
  whatsapp: [
    { key: "WhatsApp__AccessToken", label: "Erişim jetonu (Access Token)", secret: true },
    { key: "WhatsApp__AppSecret", label: "Uygulama sırrı (App Secret)", hint: "Webhook imzasını doğrulamak için.", secret: true },
    { key: "WhatsApp__PhoneNumberId", label: "Telefon numarası kimliği (Phone Number ID)", secret: false },
    { key: "WhatsApp__BusinessAccountId", label: "İşletme hesabı kimliği (WABA ID)", secret: false, optional: true },
    { key: "WhatsApp__WebhookVerifyToken", label: "Webhook doğrulama jetonu", secret: true },
  ],
  email: [
    { key: "Email__Smtp__Host", label: "SMTP sunucusu", secret: false },
    { key: "Email__Smtp__Port", label: "SMTP portu", hint: "Genellikle 587.", secret: false },
    { key: "Email__Smtp__Username", label: "Kullanıcı adı", secret: false },
    { key: "Email__Smtp__Password", label: "Parola", secret: true },
    { key: "Email__Smtp__FromAddress", label: "Gönderen adresi", secret: false },
  ],
  ai: [{ key: "Ai__ApiKey", label: "API anahtarı", secret: true }],
};

export const INTEGRATION_TITLES: Record<string, string> = {
  whatsapp: "WhatsApp",
  email: "E-posta (SMTP)",
  ai: "Yapay zekâ",
};

export const OPS_ALERT_FIELD: IntegrationField = {
  key: "Ops__AlertRecipients",
  label: "Okul alarmlarının gideceği e-postalar",
  hint: "Virgülle ayır. Boşsa alarmlar yalnızca platforma düşer.",
  secret: false,
  optional: true,
};
