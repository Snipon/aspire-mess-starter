# Build context: the src/ directory, e.g.  podman build -f Api/Dockerfile -t api src
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Api/Api.csproj", "Api/"]
COPY ["ServiceDefaults/ServiceDefaults.csproj", "ServiceDefaults/"]
RUN dotnet restore "Api/Api.csproj"
COPY Api/ Api/
COPY ServiceDefaults/ ServiceDefaults/
RUN rm -rf Api/bin Api/obj ServiceDefaults/bin ServiceDefaults/obj
# openapi/Api.json is committed; skip regenerating it inside the image build.
RUN dotnet publish "Api/Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:OpenApiGenerateDocuments=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
# TLS is terminated by the ingress (Azure Container Apps); the app listens on plain HTTP.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
