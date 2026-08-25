FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# La configuración del build va primero: TargetFramework sale de Directory.Build.props y las
# versiones de los paquetes de Directory.Packages.props. Sin estos archivos, restore falla con
# un TargetFramework vacío antes de mirar una sola dependencia.
COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./

# copy csproj and restore as distinct layers
COPY src/Tero.Postino.Api/Tero.Postino.Api.csproj src/Tero.Postino.Api/
COPY src/Tero.Postino.Application/Tero.Postino.Application.csproj src/Tero.Postino.Application/
COPY src/Tero.Postino.Infrastructure/Tero.Postino.Infrastructure.csproj src/Tero.Postino.Infrastructure/

# Los paquetes Tero.* salen de GitHub Packages, que pide credenciales. nuget.config las lee de
# GITHUB_PACKAGES_USER y GITHUB_PACKAGES_TOKEN, así que quien construya esta imagen tiene que
# pasarlas — por secreto de BuildKit, no por build-arg: un build-arg queda escrito en la
# historia de la imagen.
RUN --mount=type=secret,id=packages_user,env=GITHUB_PACKAGES_USER \
    --mount=type=secret,id=packages_token,env=GITHUB_PACKAGES_TOKEN \
    dotnet restore src/Tero.Postino.Api/Tero.Postino.Api.csproj

# copy everything else and publish
COPY . .
RUN dotnet publish src/Tero.Postino.Api/Tero.Postino.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# La imagen base no trae curl ni wget — deploy/docker-compose.yml (repo Tero) lo usa para el
# healthcheck (GET /alive).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "Tero.Postino.Api.dll"]
