# One Dockerfile for every service. Build from the repo root:
#   docker build --build-arg PROJECT=Ingest -t webhookgateway-ingest .
# PROJECT is one of Ingest, FanOut, Dispatcher, ControlPlane, Migrator.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJECT
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props WebhookGateway.slnx ./
COPY src ./src
RUN dotnet publish src/${PROJECT}/WebhookGateway.${PROJECT}.csproj -c Release -o /app --no-self-contained /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
# The migrator reads SQL files from here; harmless for the other services.
COPY db/migrations /app/db/migrations
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    ENTRY_DLL=WebhookGateway.${PROJECT}.dll
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ENTRY_DLL\" \"$@\"", "--"]
