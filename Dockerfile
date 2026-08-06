FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props CEP-API.sln ./
COPY src/CepApi.Domain/CepApi.Domain.csproj src/CepApi.Domain/
COPY src/CepApi.Application/CepApi.Application.csproj src/CepApi.Application/
COPY src/CepApi.Infrastructure/CepApi.Infrastructure.csproj src/CepApi.Infrastructure/
COPY src/CepApi.Api/CepApi.Api.csproj src/CepApi.Api/
COPY tests/CepApi.UnitTests/CepApi.UnitTests.csproj tests/CepApi.UnitTests/
COPY tests/CepApi.IntegrationTests/CepApi.IntegrationTests.csproj tests/CepApi.IntegrationTests/
RUN dotnet restore CEP-API.sln

COPY . .
RUN dotnet publish src/CepApi.Api/CepApi.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "CepApi.Api.dll"]
