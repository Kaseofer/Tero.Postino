FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# copy csproj and restore as distinct layers
COPY src/Tero.Postino.Api/Tero.Postino.Api.csproj src/Tero.Postino.Api/
COPY src/Tero.Postino.Application/Tero.Postino.Application.csproj src/Tero.Postino.Application/
COPY src/Tero.Postino.Infrastructure/Tero.Postino.Infrastructure.csproj src/Tero.Postino.Infrastructure/
COPY src/Tero.Postino.Domain/Tero.Postino.Domain.csproj src/Tero.Postino.Domain/

RUN dotnet restore src/Tero.Postino.Api/Tero.Postino.Api.csproj

# copy everything else and publish
COPY . .
RUN dotnet publish src/Tero.Postino.Api/Tero.Postino.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "Tero.Postino.Api.dll"]
