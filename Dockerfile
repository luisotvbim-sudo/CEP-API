FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props CEP-API.sln ./
COPY src/CepApi.Domain/CepApi.Domain.csproj src/CepApi.Domain/
COPY src/CepApi.Application/CepApi.Application.csproj src/CepApi.Application/
COPY src/CepApi.Infrastructure/CepApi.Infrastructure.csproj src/CepApi.Infrastructure/
COPY src/CepApi.Api/CepApi.Api.csproj src/CepApi.Api/
COPY tests/CepApi.UnitTests/CepApi.UnitTests.csproj tests/CepApi.UnitTests/
COPY tests/CepApi.IntegrationTests/CepApi.IntegrationTests.csproj tests/CepApi.IntegrationTests/
RUN dotnet restore src/CepApi.Api/CepApi.Api.csproj

COPY . .
RUN dotnet publish src/CepApi.Api/CepApi.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs icu-data-full tzdata krb5-libs \
    && mkdir -p /var/lib/cep-api/keys \
    && chown -R app:app /var/lib/cep-api
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "CepApi.Api.dll"]
