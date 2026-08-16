FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/QrSimple.Api/QrSimple.Api.csproj src/QrSimple.Api/
RUN dotnet restore src/QrSimple.Api/QrSimple.Api.csproj

COPY src/QrSimple.Api/ src/QrSimple.Api/
RUN dotnet publish src/QrSimple.Api/QrSimple.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp (via ZXing.Net.Bindings.SkiaSharp, used for QR generation) needs
# libfontconfig1 to load its native library -- same dependency the devcontainer
# base image was missing; see AGENTS.md's environment gotchas.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "QrSimple.Api.dll"]
