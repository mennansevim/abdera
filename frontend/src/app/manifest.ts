import type { MetadataRoute } from "next";

// Ana ekrana eklenebilir uygulama (issue #16): öğretmenler ders aralarında telefondan çalışıyor;
// tarayıcı sekmesi yerine ana ekrandan tam ekran açılsın. Ayrı bir mobil uygulama değil, aynı web
// uygulamasının manifesti (CLAUDE.md "veli/öğrenci mobil uygulaması" yasağına girmez). Çevrimdışı
// önbellek (service worker) bilinçli olarak yok: bayat ders/yoklama verisi göstermek, hiç
// göstermemekten kötü.
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Abdera Sanat Akademi",
    short_name: "Abdera",
    description: "Abdera Müzik Okulu Yönetim Sistemi",
    lang: "tr",
    start_url: "/dashboard",
    scope: "/",
    display: "standalone",
    background_color: "#fdf3e7",
    theme_color: "#a65858",
    icons: [
      { src: "/icon.png", sizes: "192x192", type: "image/png" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png" },
    ],
  };
}
