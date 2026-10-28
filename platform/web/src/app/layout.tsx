import type { Metadata, Viewport } from "next";
import localFont from "next/font/local";
import { Providers } from "./providers";
import "./globals.css";

// FONTLAR REPODA, next/font/google İLE İNDİRİLMİYOR (okul uygulamasının layout.tsx'iyle aynı
// gerekçe): next/font/google fontları derleme anında fonts.googleapis.com'dan çeker; çekim
// başarısız olunca Docker imajı derlenmez. Dosyalar frontend/src/app/fonts'tan kopyalandı.
//
// Hepsi değişken (variable) font: ağırlık tek tek değil aralık olarak verilir. Alt küme
// `latin` + `latin-ext`: Türkçenin ğ Ğ ş Ş İ harfleri yalnızca `latin-ext`'te.
const figtree = localFont({
  src: "./fonts/Figtree-latin-ext.woff2",
  variable: "--font-figtree",
  weight: "300 900",
  display: "swap",
});

const lora = localFont({
  src: [
    { path: "./fonts/Lora-latin-ext.woff2", style: "normal", weight: "400 700" },
    { path: "./fonts/Lora-Italic-latin-ext.woff2", style: "italic", weight: "400 700" },
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
    default: "Miredo",
    template: "%s · Miredo",
  },
  description: "Miredo - okul kurulumlarını yöneten operatör paneli",
  robots: { index: false, follow: false },
};

export const viewport: Viewport = {
  colorScheme: "light dark",
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#fdf3e7" },
    { media: "(prefers-color-scheme: dark)", color: "#1d1612" },
  ],
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
