# Abdera Platform operatör paneli. frontend/Dockerfile ile aynı kalıp:
# output: standalone (next.config.ts) sayesinde yalnızca gerekli dosyalar kopyalanır.
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# Boş bırakılırsa panel API'ye aynı origin'den (/api) konuşur - Caddy yönlendirir.
ARG NEXT_PUBLIC_API_BASE_URL=
ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL}
# Küçük bellekli derleme makinesinde Next.js worker'larının SIGKILL ile düşmesini engeller.
ENV NODE_OPTIONS=--max-old-space-size=768
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
RUN addgroup -S nodejs && adduser -S nextjs -G nodejs

COPY --from=build /app/public ./public
COPY --from=build --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /app/.next/static ./.next/static

USER nextjs
EXPOSE 3100
ENV PORT=3100
ENV HOSTNAME=0.0.0.0

CMD ["node", "server.js"]
