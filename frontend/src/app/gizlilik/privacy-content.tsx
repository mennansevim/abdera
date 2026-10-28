"use client";

import Link from "next/link";
import { useSyncExternalStore } from "react";
import { SchoolBrandMark } from "@/components/school-brand";
import { useModuleEnabled, useSchoolProfile } from "@/lib/instance";

// Gizlilik metni okulun kendi adını, unvanını ve başvuru adresini taşır (docs/multi-tenant/ARCHITECTURE.md
// §5.1). Veri sorumlusu okuldur; unvan girilmemişse okulun adı, başvuru adresi girilmemişse okulun
// kendisine başvuru yolu yazılır.

const noSubscribe = () => () => {};

function usePrivacySections() {
  const profile = useSchoolProfile();
  const controller = profile.legalName || profile.name;
  // Sunucu çiziminde adres bilinmez; istemcide hidrasyon uyuşmazlığı olmadan doldurulur.
  const panel = useSyncExternalStore(noSubscribe, () => window.location.host, () => "");
  const CONTACT_EMAIL = profile.contactEmail ?? "";
  // Asistan yapay zekâ modülüne bağlı; modül kapalı okulda DeepSeek'e aktarım yoktur, bölüm de yazılmaz.
  const assistant = useModuleEnabled("ai");
  return { sections: buildSections(controller, panel, CONTACT_EMAIL, assistant), contactEmail: CONTACT_EMAIL, shortName: profile.shortName };
}

function buildSections(controller: string, panel: string, CONTACT_EMAIL: string, assistant: boolean) {
  const sections: { title: string; body: string[] }[] = [
  {
    title: "Kapsam",
    body: [
      `Bu metin, ${controller} adına işletilen okul yönetim paneli${panel ? ` (${panel})` : ""} ve bu panel üzerinden gönderilen WhatsApp bildirimleri kapsamında işlenen kişisel verileri açıklar. Veri sorumlusu ${controller}.`,
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
  ...(assistant ? [{
    title: "Yapay zekâ asistanı",
    body: [
      "Panelde yalnızca okul personelinin (yönetici ve öğretmen) kullandığı bir asistan bulunur. Personelin yazdığı soruyu yanıtlamak için gerekli veriler (ders programı ve saatleri, yoklama, telafi hakları, gelişim notları ve aidat durumu) yapay zekâ hizmet sağlayıcısı DeepSeek'e (Hangzhou DeepSeek Artificial Intelligence Co., Ltd., Çin Halk Cumhuriyeti) aktarılır.",
      "Aktarım öncesinde öğrenci ve öğretmen adları kod ile değiştirilir; veli bilgileri, telefon numarası, e-posta, doğum tarihi ve adres gönderilmez. Asistan hiçbir kaydı kendiliğinden değiştirmez; ders saati veya telafi gibi her işlem personelin ekranda onayıyla yapılır.",
      "Bu aktarım, 6698 sayılı KVKK'nın 9. maddesi kapsamında yurt dışına veri aktarımıdır. Aktarıma ilişkin sorularınızı ve itirazlarınızı aşağıdaki adrese iletebilirsiniz. Hizmet sağlayıcısı değişirse bu bölüm güncellenir.",
    ],
  }] : []),
  {
    title: "Saklama ve güvenlik",
    body: [
      "Veriler, yalnızca şifreli (HTTPS) bağlantı üzerinden erişilen bir sunucudaki veritabanında tutulur; yedekler şifrelenerek saklanır. Panele yalnızca yetkili okul personeli erişebilir.",
      "Sistemin teknik işletmecisi (veri işleyen), arıza giderme amacıyla ve yalnızca gerektiğinde sunucunun sistem kayıtlarını (log) görüntüleyebilir. Bu kayıtlar işletmecide saklanmaz ve her görüntüleme kayıt altına alınır.",
      "Muhasebe ve yasal yükümlülükler gereği saklanması gereken ödeme kayıtları dışındaki veriler, öğrencinin okulla ilişkisi sona erdikten sonra talep üzerine silinir.",
    ],
  },
  {
    title: "Haklarınız ve veri silme",
    body: [
      "6698 sayılı KVKK'nın 11. maddesi kapsamında verilerinize erişme, düzeltilmesini veya silinmesini isteme ve işlemeye itiraz etme hakkına sahipsiniz.",
      CONTACT_EMAIL
        ? `Talebinizi ${CONTACT_EMAIL} adresine e-posta ile iletebilirsiniz. Talepler en geç 30 gün içinde yanıtlanır.`
        : "Talebinizi okul yönetimine yazılı olarak iletebilirsiniz. Talepler en geç 30 gün içinde yanıtlanır.",
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
      <p className="text-meta mt-2">Son güncelleme: 28 Ekim 2026</p>
      {sections.map((section) => <section key={section.title} className="mt-8">
        <h2 className="text-lg font-bold">{section.title}</h2>
        {section.body.map((paragraph) => <p key={paragraph} className="mt-3 leading-relaxed">{paragraph}</p>)}
      </section>)}
      {contactEmail && (
        <p className="mt-10 leading-relaxed">
          İletişim: <a href={`mailto:${contactEmail}`} className="font-bold text-[var(--brand-strong)] underline">{contactEmail}</a>
        </p>
      )}
    </main>
  </div>;
}
