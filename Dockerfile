# syntax=docker/dockerfile:1
# One image = API + Blazor UI + catalog data. Build: docker build -t butikkpilot .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project/package file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ButikkPilot.Contracts/*.csproj src/ButikkPilot.Contracts/
COPY src/ButikkPilot.Core/*.csproj src/ButikkPilot.Core/
COPY src/ButikkPilot.Infrastructure/*.csproj src/ButikkPilot.Infrastructure/
COPY src/ButikkPilot.RobotAdapter/*.csproj src/ButikkPilot.RobotAdapter/
COPY src/ButikkPilot.Api/*.csproj src/ButikkPilot.Api/
COPY src/ButikkPilot.Web/*.csproj src/ButikkPilot.Web/
RUN dotnet restore src/ButikkPilot.Api && dotnet restore src/ButikkPilot.Web

COPY src/ src/
COPY data/ data/
RUN dotnet publish src/ButikkPilot.Web -c Release -o /out/web --no-restore \
 && dotnet publish src/ButikkPilot.Api -c Release -o /out/app --no-restore \
 && cp -r /out/web/wwwroot /out/app/wwwroot \
 && test -f /out/app/wwwroot/index.html && test -f /out/app/data/products.json

# Chiseled = no shell, no package manager, runs as non-root (uid 1654) by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /out/app/ ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "ButikkPilot.Api.dll"]
