import type { MetadataRoute } from "next";

// Ana ekrana eklenebilir uygulama (issue #16): öğretmenler ders aralarında telefondan çalışıyor;
// tarayıcı sekmesi yerine ana ekrandan tam ekran açılsın. Ayrı bir mobil uygulama değil, aynı web
// uygulamasının manifesti (CLAUDE.md "veli/öğrenci mobil uygulaması" yasağına girmez). Çevrimdışı
// önbellek (service worker) bilinçli olarak yok: bayat ders/yoklama verisi göstermek, hiç
// göstermemekten kötü.
//
// Okul başına kurulumda (docs/multi-tenant/ARCHITECTURE.md §5.1) ad ve ikon okulun profilinden
// gelir: API_INTERNAL_URL (çalışma anı değişkeni, ör. http://api:8080) tanımlıysa profil okunur.
// Tanımlı değilse ya da API'ye ulaşılamazsa bugünkü Abdera manifesti aynen döner.
export const dynamic = "force-dynamic";

const ABDERA: MetadataRoute.Manifest = {
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

type Profile = { name: string; shortName: string; primaryColor: string | null; logoUrl: string | null };

async function readProfile(): Promise<Profile | null> {
  const base = process.env.API_INTERNAL_URL;
  if (!base) return null;
  try {
    const response = await fetch(`${base.replace(/\/$/, "")}/api/instance/config`, {
      cache: "no-store",
      signal: AbortSignal.timeout(2000),
    });
    if (!response.ok) return null;
    return ((await response.json()) as { profile: Profile }).profile;
  } catch {
    return null;
  }
}

export default async function manifest(): Promise<MetadataRoute.Manifest> {
  const profile = await readProfile();
  if (!profile || (profile.name === ABDERA.name && profile.shortName === ABDERA.short_name && !profile.logoUrl)) return ABDERA;
  return {
    ...ABDERA,
    name: profile.name,
    short_name: profile.shortName,
    description: `${profile.name} yönetim sistemi`,
    theme_color: profile.primaryColor ?? ABDERA.theme_color,
    // Logo aynı origin'deki /api üzerinden sunulur (Caddy); boyutu bilinmediği için "any".
    icons: profile.logoUrl ? [{ src: profile.logoUrl, sizes: "any" }, ...ABDERA.icons!] : ABDERA.icons,
  };
}
