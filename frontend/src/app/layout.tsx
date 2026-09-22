import type { Metadata } from "next";
import localFont from "next/font/local";
import { Providers } from "./providers";
import "./globals.css";
import { FontSizeController } from "@/components/font-size-controller";

// "Sıcak Atölye" yön değişimi (redesign/sicak-atolye): gövde fontu nötr Geist Sans'tan
// daha sıcak/insancıl Figtree'ye, başlık/marka fontu ise Lora italik serife geçti - bkz.
// docs/14-ui-design-prompt.md. Geist Mono geçici şifre/telefon gösteriminde kullanılıyor
// (`font-mono`), kalıyor.
//
// FONTLAR REPODA, next/font/google İLE İNDİRİLMİYOR. Gerekçe (gerçek bir CI hatası):
// next/font/google fontları DERLEME ANINDA fonts.googleapis.com'dan çeker. Docker imajı
// derlenirken bu çekim başarısız olunca `npm run build` "module not found:
// [next]/internal/font/google/lora_*.module.css" ile düşüyor ve compose ayağa kalkmıyordu -
// aynı commit runner üzerinde sorunsuz derlenirken. Üretim imajının derlenmesi üçüncü
// parti bir servise bağlı olmamalı; dosyalar `./fonts` altında duruyor.
//
// Hepsi DEĞİŞKEN (variable) font: aile başına tek dosya tüm ağırlıkları taşır, bu yüzden
// ağırlıklar tek tek listelenmez - aralık verilir. Yalnızca temel `latin` alt kümesi
// indirildi (latin-ext/vietnamese gereksiz ağırlık getirirdi).
const figtree = localFont({
  src: "./fonts/Figtree-latin.woff2",
  variable: "--font-figtree",
  weight: "300 900",
  display: "swap",
});

const lora = localFont({
  src: [
    { path: "./fonts/Lora-latin.woff2", style: "normal", weight: "400 700" },
    { path: "./fonts/Lora-Italic-latin.woff2", style: "italic", weight: "400 700" },
  ],
  variable: "--font-lora",
  display: "swap",
});

const geistMono = localFont({
  src: "./fonts/GeistMono-latin.woff2",
  variable: "--font-geist-mono",
  weight: "100 900",
  display: "swap",
});

export const metadata: Metadata = {
  title: {
    default: "Abdera",
    template: "%s · Abdera",
  },
  description: "Abdera Müzik Okulu Yönetim Sistemi",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="tr"
      className={`${figtree.variable} ${lora.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full flex flex-col">
        <FontSizeController />
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
