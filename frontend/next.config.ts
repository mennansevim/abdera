import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Docker imajını küçük tutmak için standalone çıktı yerelde kullanılır. Vercel
  // Services kendi build çıktısını topladığı için orada bu ayar kapatılır; aksi halde
  // Vercel'in onBuildComplete adımı next-server.js.nft.json bulamaz.
  output: process.env.VERCEL ? undefined : "standalone",
  // Üst dizinde (bu repoya ait olmayan, başka bir projeden kalma) bir package-lock.json
  // bulunduğu için Turbopack'in kök dizini otomatik algılaması belirsizleşiyor - sabitle.
  turbopack: {
    root: __dirname,
  },
};

export default nextConfig;
