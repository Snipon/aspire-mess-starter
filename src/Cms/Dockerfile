# Build context: the src/ directory, e.g.  podman build -f Cms/Dockerfile -t cms src
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Cms/Cms.csproj", "Cms/"]
COPY ["ServiceDefaults/ServiceDefaults.csproj", "ServiceDefaults/"]
RUN dotnet restore "Cms/Cms.csproj"
COPY Cms/ Cms/
COPY ServiceDefaults/ ServiceDefaults/
# Drop host build output in case the context has no .dockerignore (packages are already cached by the restore layer).
RUN rm -rf Cms/bin Cms/obj ServiceDefaults/bin ServiceDefaults/obj
RUN dotnet publish "Cms/Cms.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Umbraco writes logs, temp files and (without Azure Blob storage) media below the app folder.
RUN mkdir -p umbraco wwwroot/media && chown -R $APP_UID:$APP_UID umbraco wwwroot/media
USER $APP_UID
# TLS is terminated by the ingress (Azure Container Apps); the app listens on plain HTTP.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cms.dll"]
