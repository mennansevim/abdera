import type { Metadata } from "next";
import { PrivacyContent } from "./privacy-content";

// Meta (WhatsApp Cloud API) uygulamasının yayınlanması herkese açık bir gizlilik politikası
// URL'si ister. Oturum gerektirmez; /kutuphane ile aynı yalın başlık düzenini kullanır.
// Metin okulun adını/unvanını taşır, bu yüzden içerik istemci bileşeninde (privacy-content.tsx).
export const metadata: Metadata = {
  title: "Gizlilik Politikası",
  description: "Okul yönetim paneli ve WhatsApp bildirimleri için gizlilik politikası ve KVKK aydınlatma metni.",
};

export default function PrivacyPolicyPage() {
  return <PrivacyContent />;
}
