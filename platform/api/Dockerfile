# Abdera Platform API - okul uygulamasının imajıyla aynı desen (backend/Dockerfile).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Abdera.Platform.Api/Abdera.Platform.Api.csproj Abdera.Platform.Api/
RUN dotnet restore Abdera.Platform.Api/Abdera.Platform.Api.csproj
COPY Abdera.Platform.Api/ Abdera.Platform.Api/
RUN dotnet publish Abdera.Platform.Api/Abdera.Platform.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
# krb5-libs: Npgsql bağlantıda libgssapi_krb5 arar.
RUN apk add --no-cache tzdata krb5-libs
# Data Protection anahtarları (okul sırlarını çözer) ve SSH anahtarı kalıcı dizinlerde.
RUN mkdir -p /app/keys /run/secrets && chown app:app /app/keys
USER app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8090
EXPOSE 8090
ENTRYPOINT ["dotnet", "Abdera.Platform.Api.dll"]
