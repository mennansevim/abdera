import { ImageResponse } from "next/og";

// Nötr uygulama ikonu (favicon, ana ekran): açık kitap. Ürünün adı ya da logosu taşınmaz - okul
// kendi logosunu yüklediyse manifest onu kullanır (app/manifest.ts).
export function appIcon(size: number) {
  const page = { width: size * 0.3, height: size * 0.42, background: "#fffaf3", borderRadius: size * 0.04 };
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", gap: size * 0.02, background: "#7a5148", borderRadius: size * 0.22 }}>
        <div style={{ ...page, transform: "skewY(8deg)" }} />
        <div style={{ ...page, transform: "skewY(-8deg)" }} />
      </div>
    ),
    { width: size, height: size },
  );
}
