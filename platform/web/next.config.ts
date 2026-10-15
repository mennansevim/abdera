import type { NextConfig } from "next";

// Platform API'si CORS açmaz ve oturum çerezi SameSite=Strict'tir: tarayıcı API'ye yalnızca
// panelle AYNI origin'den konuşur. Yayında Caddy `/api`'yi platform API'sine yönlendirir.
// Yerel geliştirmede aynı şeyi Next.js yapar: `/api/*` istekleri PLATFORM_API_URL'e
// (varsayılan http://localhost:8090) aktarılır. Yalnızca `next dev`'de etkin - üretim imajına
// geliştirme adresi gömülmesin.
const devApiUrl = (process.env.PLATFORM_API_URL ?? "http://localhost:8090").replace(/\/$/, "");

const nextConfig: NextConfig = {
  // Docker imajını küçük tutmak için standalone çıktı (Dockerfile bunu kopyalar).
  output: "standalone",
  // Üst dizinde başka bir package-lock.json bulunduğu için Turbopack'in kök dizini
  // otomatik algılaması belirsizleşiyor - sabitle (frontend/next.config.ts ile aynı gerekçe).
  turbopack: {
    root: __dirname,
  },
  async rewrites() {
    if (process.env.NODE_ENV !== "development") return [];
    return [{ source: "/api/:path*", destination: `${devApiUrl}/api/:path*` }];
  },
};

export default nextConfig;
