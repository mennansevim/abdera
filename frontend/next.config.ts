import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Docker imajını küçük tutmak için standalone çıktı (frontend/Dockerfile bunu kopyalar).
  output: "standalone",
  // Üst dizinde (bu repoya ait olmayan, başka bir projeden kalma) bir package-lock.json
  // bulunduğu için Turbopack'in kök dizini otomatik algılaması belirsizleşiyor - sabitle.
  turbopack: {
    root: __dirname,
  },
};

export default nextConfig;
