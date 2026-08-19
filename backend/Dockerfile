# Çok aşamalı build - üretim imajı yalnızca yayınlanmış (published) çıktıyı içerir,
# SDK'yı taşımaz.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Abdera.slnx ./
COPY src/Abdera.Api/Abdera.Api.csproj src/Abdera.Api/
COPY tests/Abdera.Tests/Abdera.Tests.csproj tests/Abdera.Tests/
RUN dotnet restore src/Abdera.Api/Abdera.Api.csproj

COPY src/Abdera.Api/ src/Abdera.Api/
RUN dotnet publish src/Abdera.Api/Abdera.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Alpine'ın minimal tabanında eksik olan, uygulamanın gerçekten kullandığı native
# kütüphaneler: tzdata (SCHOOL_TIMEZONE=Europe/Istanbul çözümlemesi için - IClock.cs),
# krb5-libs (Npgsql bağlantı sırasında libgssapi_krb5.so.2 arar, yoksa çöker),
# icu-data-full (icu-libs'in İngilizce-dışı yerel ayarları içeren tam veri paketi -
# okulun kendi yerel ayarı tr-TR olduğu için varsayılan icu-data-en yetersiz kalır).
RUN apk add --no-cache tzdata krb5-libs icu-libs icu-data-full

# Auth__KeysDirectory (Data Protection anahtarları) buraya yazılır - bir volume ile
# eşleşecek, "app" kullanıcısının (1654:1654) yazabilmesi için COPY'den önce oluşturulup
# sahiplik veriliyor; COPY --chown olmadan varsayılan olarak root sahipli gelirdi.
RUN mkdir -p /app/keys && chown app:app /app/keys

# Root olmayan kullanıcı - imaj varsayılan olarak zaten "app" kullanıcısını içerir.
USER app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Abdera.Api.dll"]
