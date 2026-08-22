# syntax=docker/dockerfile:1
# Imagem de produção do Host .NET 8. Build context = raiz do repositório.
# Só o Host e suas dependências entram no restore; tests e frontend ficam fora (ver .dockerignore).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ ./src/

RUN dotnet restore src/Host/SpotifyDataAnalysis.Api/SpotifyDataAnalysis.Api.csproj

RUN dotnet publish src/Host/SpotifyDataAnalysis.Api/SpotifyDataAnalysis.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

ENTRYPOINT ["dotnet", "SpotifyDataAnalysis.Api.dll"]
