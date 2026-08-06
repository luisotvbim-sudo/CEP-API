# CEP Plugins API

API multiempresa para administrar usuários dos plugins Revit e ZWCAD. A solução usa ASP.NET Core 10, PostgreSQL, ASP.NET Core Identity, access tokens JWT, refresh tokens rotativos e grants offline RS256 de 72 horas.

## Recursos

- Organizações isoladas, com estados ativo, suspenso e arquivado.
- Papéis `SystemAdmin`, `OrganizationAdmin` e `User`.
- Convites e recuperação de senha por SMTP, sem cadastro público.
- Acesso separado aos produtos Revit e ZWCAD.
- Sessões revogáveis, lockout, rate limiting e detecção de reutilização de refresh token.
- Auditoria administrativa, JWKS público, Swagger e health checks.
- Migrations explícitas, testes unitários e fluxo de integração com PostgreSQL real.

## Início rápido com containers

Pré-requisito: Docker Desktop, Docker Engine ou alternativa compatível com Compose.

```bash
docker compose up --build -d
```

A API estará em `http://localhost:8080`, o Swagger em `http://localhost:8080/swagger` e o Mailpit em `http://localhost:8025`. O Compose executa a migration antes de iniciar a API.

Crie o primeiro administrador global uma única vez:

```bash
docker compose run --rm \
  -e BootstrapAdmin__Email=admin@example.com \
  -e BootstrapAdmin__Password="uma senha longa e exclusiva" \
  -e BootstrapAdmin__DisplayName="Administrador" \
  api bootstrap-admin
```

## Execução sem containers

Requer .NET SDK 10 e uma instância PostgreSQL compatível.

```bash
dotnet tool restore
dotnet restore CEP-API.sln
dotnet run --project src/CepApi.Api -- migrate
```

Configure `BootstrapAdmin__Email`, `BootstrapAdmin__Password` e opcionalmente `BootstrapAdmin__DisplayName` no ambiente, então execute:

```bash
dotnet run --project src/CepApi.Api -- bootstrap-admin
dotnet run --project src/CepApi.Api
```

Em `Development`, códigos de convite e recuperação são registrados somente no log local. Nos demais ambientes, o envio usa SMTP.

## Configuração de produção

As chaves usam a sintaxe hierárquica do ASP.NET Core (`__` em variáveis de ambiente):

- `ConnectionStrings__Postgres`: conexão PostgreSQL.
- `Jwt__PrivateKeyPem`: chave RSA privada PEM; obrigatória em produção. Quebras de linha podem ser fornecidas como `\n`.
- `Jwt__KeyId`: identificador público da chave ativa.
- `Jwt__PreviousPublicKeys__0__KeyId` e `Jwt__PreviousPublicKeys__0__PublicKeyPem`: chaves anteriores aceitas durante rotação.
- `Email__Host`, `Email__Port`, `Email__UseSsl`, `Email__Username`, `Email__Password`, `Email__FromAddress` e `Email__FromName`: SMTP.

Gere uma chave RSA fora do repositório e injete-a pelo gerenciador de segredos da plataforma:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out jwt-private.pem
openssl rsa -in jwt-private.pem -pubout -out jwt-public.pem
```

Para rotacionar, configure a nova chave como ativa e mantenha a chave pública anterior na coleção `PreviousPublicKeys` por pelo menos 72 horas. Nunca versione arquivos `.pem`, `.key`, senhas ou tokens.

Migrations não são aplicadas automaticamente em produção:

```bash
dotnet run --project src/CepApi.Api -- migrate
```

## Desenvolvimento e testes

```bash
dotnet build CEP-API.sln -m:1
dotnet test tests/CepApi.UnitTests
dotnet test tests/CepApi.IntegrationTests
```

Os testes de integração usam Testcontainers e PostgreSQL. Quando o Docker não está disponível localmente, eles são marcados como ignorados; na CI (`CI=true`) a ausência de Docker falha o job. A coleção de exemplos está em `requests/cep-api.http` e o contrato do cliente em `docs/plugin-integration.md`.

Endpoints de operação:

- `GET /health/live`: processo em execução.
- `GET /health/ready`: acesso ao PostgreSQL confirmado.
- `GET /.well-known/jwks.json`: chaves públicas de assinatura.
- `GET /swagger`: documentação interativa.
