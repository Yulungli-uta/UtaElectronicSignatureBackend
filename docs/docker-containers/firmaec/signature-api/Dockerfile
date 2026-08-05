FROM mcr.microsoft.com/dotnet/sdk:9.0.309-bookworm-slim AS build
WORKDIR /source

COPY UtaElectronicSignature.sln ./
COPY src/UtaElectronicSignature.API/UtaElectronicSignature.API.csproj src/UtaElectronicSignature.API/
COPY src/UtaElectronicSignature.Application/UtaElectronicSignature.Application.csproj src/UtaElectronicSignature.Application/
COPY src/UtaElectronicSignature.Contracts/UtaElectronicSignature.Contracts.csproj src/UtaElectronicSignature.Contracts/
COPY src/UtaElectronicSignature.Domain/UtaElectronicSignature.Domain.csproj src/UtaElectronicSignature.Domain/
COPY src/UtaElectronicSignature.FirmaEc/UtaElectronicSignature.FirmaEc.csproj src/UtaElectronicSignature.FirmaEc/
COPY src/UtaElectronicSignature.Infrastructure/UtaElectronicSignature.Infrastructure.csproj src/UtaElectronicSignature.Infrastructure/
RUN dotnet restore src/UtaElectronicSignature.API/UtaElectronicSignature.API.csproj

COPY src ./src
RUN dotnet publish src/UtaElectronicSignature.API/UtaElectronicSignature.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.12-bookworm-slim AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
COPY docs/docker-containers/firmaec/signature-api/entrypoint.sh /usr/local/bin/signature-api-entrypoint
RUN chmod 0555 /usr/local/bin/signature-api-entrypoint

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["/usr/local/bin/signature-api-entrypoint"]
