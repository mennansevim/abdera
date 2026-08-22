import type { Metadata } from "next";
import { Figtree, Geist_Mono, Lora } from "next/font/google";
import { Providers } from "./providers";
import "./globals.css";

// "Sıcak Atölye" yön değişimi (redesign/sicak-atolye): gövde fontu nötr Geist Sans'tan
// daha sıcak/insancıl Figtree'ye, başlık/marka fontu ise Lora italik serife geçti - bkz.
// docs/14-ui-design-prompt.md. Geist Mono aynen kalıyor (tabular-nums zaten Figtree üzerinde çalışıyor,
// mono hiçbir yerde kullanılmıyor - bu redesign'ın kapsamı değil).
const figtree = Figtree({
  variable: "--font-figtree",
  subsets: ["latin"],
  weight: ["500", "600", "700", "800"],
});

const lora = Lora({
  variable: "--font-lora",
  subsets: ["latin"],
  style: ["italic", "normal"],
  weight: ["600", "700"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
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
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
