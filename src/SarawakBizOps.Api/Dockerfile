# Build context is the repo root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a .csproj changes.
COPY src/SarawakBizOps.Api/SarawakBizOps.Api.csproj src/SarawakBizOps.Api/
RUN dotnet restore src/SarawakBizOps.Api/SarawakBizOps.Api.csproj

COPY src/SarawakBizOps.Api/ src/SarawakBizOps.Api/
RUN dotnet publish src/SarawakBizOps.Api/SarawakBizOps.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# No secrets live in the image: the JWT key, connection string and seed passwords all
# arrive as environment variables from docker-compose.yml. The .NET 8 image listens on 8080 by default.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SarawakBizOps.Api.dll"]
