"use client";

import Link from "next/link";
import { useSyncExternalStore } from "react";
import { SchoolBrandMark } from "@/components/school-brand";
import { DEFAULT_PROFILE, useSchoolProfile } from "@/lib/instance";

// Gizlilik metni okulun kendi adını, unvanını ve başvuru adresini taşır (docs/multi-tenant/ARCHITECTURE.md
// §5.1). Profil varsayılansa (bugünkü okul) metin önceki sürümle birebir aynıdır.
const ABDERA = { controller: "Abdera Sanat", panel: "panel.abderasanat.com", email: "admin@abderasanat.com" };

const noSubscribe = () => () => {};

function usePrivacySections() {
  const profile = useSchoolProfile();
  const isDefault = profile.name === DEFAULT_PROFILE.name && !profile.legalName && !profile.contactEmail;
  const controller = isDefault ? ABDERA.controller : profile.legalName || profile.name;
  // Sunucu çiziminde adres bilinmez; istemcide hidrasyon uyuşmazlığı olmadan doldurulur.
  const host = useSyncExternalStore(noSubscribe, () => window.location.host, () => "");
  const panel = isDefault ? ABDERA.panel : host;
  const CONTACT_EMAIL = (isDefault ? ABDERA.email : profile.contactEmail) || ABDERA.email;
  return { sections: buildSections(controller, panel, CONTACT_EMAIL), contactEmail: CONTACT_EMAIL, shortName: profile.shortName };
}

function buildSections(controller: string, panel: string, CONTACT_EMAIL: string) {
  const sections: { title: string; body: string[] }[] = [
  {
    title: "Kapsam",
    body: [
      `Bu metin, ${controller === ABDERA.controller ? "Abdera Sanat'ın" : `${controller} adına işletilen`} okul yönetim paneli${panel ? ` (${panel})` : ""} ve bu panel üzerinden gönderilen WhatsApp bildirimleri kapsamında işlenen kişisel verileri açıklar. Veri sorumlusu ${controller === ABDERA.controller ? "Abdera Sanat'tır" : controller}.`,
    ],
  },
  {
    title: "İşlenen veriler",
    body: [
      "Veli: ad soyad, telefon numarası, bildirim tercihi (rıza) ve WhatsApp üzerinden okula gönderilen mesajlar.",
      "Öğrenci: ad soyad, doğum tarihi, kayıtlı olduğu kurslar, ders programı, yoklama, gelişim notları ve aidat/ödeme kayıtları.",
      "Öğretmen ve personel: ad soyad, iletişim bilgileri ve panel kullanım kayıtları.",
    ],
  },
  {
    title: "Kullanım amaçları",
    body: [
      "Ders programının yürütülmesi; ders hatırlatması, ders iptali veya saat değişikliği bildirimi.",
      "Aidat takibi ve ödeme hatırlatması.",
      "Velinin WhatsApp'tan gönderdiği sorulara (ders saati, aidat durumu, telafi hakkı) yanıt verilmesi ve derse katılım teyidinin alınması.",
      "Bu veriler reklam veya pazarlama amacıyla kullanılmaz, üçüncü kişilere satılmaz.",
    ],
  },
  {
    title: "WhatsApp bildirimleri",
    body: [
      "Bildirimler Meta Platforms'un WhatsApp Business (Cloud API) hizmeti aracılığıyla, yalnızca bildirim almayı kabul eden velilere gönderilir. Tek istisna, öğrenci kaydı yapıldığında veliye gönderilen tek seferlik karşılama mesajıdır. Mesajın iletilmesi için telefon numarası ve mesaj içeriği Meta'ya aktarılır.",
      "Bildirimleri durdurmak için okulun WhatsApp hattına \"dur\", \"iptal\" veya \"stop\" yazmanız yeterlidir. Bekleyen tüm bildirimler iptal edilir ve tek bir teyit mesajı gönderilir.",
    ],
  },
  {
    title: "Saklama ve güvenlik",
    body: [
      "Veriler, yalnızca şifreli (HTTPS) bağlantı üzerinden erişilen bir sunucudaki veritabanında tutulur; yedekler şifrelenerek saklanır. Panele yalnızca yetkili okul personeli erişebilir.",
      "Muhasebe ve yasal yükümlülükler gereği saklanması gereken ödeme kayıtları dışındaki veriler, öğrencinin okulla ilişkisi sona erdikten sonra talep üzerine silinir.",
    ],
  },
  {
    title: "Haklarınız ve veri silme",
    body: [
      "6698 sayılı KVKK'nın 11. maddesi kapsamında verilerinize erişme, düzeltilmesini veya silinmesini isteme ve işlemeye itiraz etme hakkına sahipsiniz.",
      `Talebinizi ${CONTACT_EMAIL} adresine e-posta ile iletebilirsiniz. Talepler en geç 30 gün içinde yanıtlanır.`,
    ],
  },
];
  return sections;
}

export function PrivacyContent() {
  const { sections, contactEmail, shortName } = usePrivacySections();
  return <div className="min-h-dvh">
    <header className="flex min-h-16 items-center justify-between gap-3 border-b border-[var(--line)] bg-[var(--surface)] px-3 py-2 sm:px-5">
      <Link href="/" aria-label={`${shortName} ana sayfa`} className="text-[var(--brand-strong)]"><SchoolBrandMark /></Link>
    </header>
    <main className="mx-auto max-w-3xl px-4 py-8 sm:py-12">
      <h1 className="text-2xl font-bold sm:text-3xl">Gizlilik Politikası</h1>
      <p className="text-meta mt-2">Son güncelleme: 29 Eylül 2026</p>
      {sections.map((section) => <section key={section.title} className="mt-8">
        <h2 className="text-lg font-bold">{section.title}</h2>
        {section.body.map((paragraph) => <p key={paragraph} className="mt-3 leading-relaxed">{paragraph}</p>)}
      </section>)}
      <p className="mt-10 leading-relaxed">
        İletişim: <a href={`mailto:${contactEmail}`} className="font-bold text-[var(--brand-strong)] underline">{contactEmail}</a>
      </p>
    </main>
  </div>;
}
