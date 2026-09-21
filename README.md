# CEP API

Backend multiempresa para administrar usuários dos plugins Revit e ZWCAD e a aplicação de conciliação de horas. A solução usa ASP.NET Core 10, PostgreSQL, ASP.NET Core Identity, access tokens JWT, refresh tokens rotativos e grants offline RS256 de 72 horas.

## Recursos

- Organizações isoladas, com estados ativo, suspenso e arquivado.
- Papéis `SystemAdmin`, `OrganizationAdmin` e `User`.
- Convites e recuperação de senha por SMTP, sem cadastro público.
- Acesso separado aos produtos Revit e ZWCAD.
- Sessões revogáveis, lockout, rate limiting e detecção de reutilização de refresh token.
- Auditoria administrativa, JWKS público, Swagger e health checks.
- Equipes de controle de ponto e vínculos efetivos de membros e gestores.
- Migrations explícitas, testes unitários e fluxo de integração com PostgreSQL real.

## Controle de ponto

A primeira etapa do backend implementa o cadastro de equipes e os vínculos de membros e gestores com vigência e histórico. Os endpoints ficam em `api/v1/organization/time-control/teams` e são restritos ao administrador da organização. O servidor impede nomes duplicados, vínculos sobrepostos e associação de usuários de outra organização.

A importação administrativa do Monday e do Ponto VR Mais persiste identidades e apontamentos com retenção móvel de 90 dias. Calendário, regras de tolerância, tratamento de divergências e conciliação diária completa continuam nas próximas etapas descritas na [especificação funcional](docs/conciliacao-horas/especificacao-funcional.md).

### Cadastro administrativo das identidades externas

O backend já possui o fluxo administrativo que antecede a conciliação:

- `POST /api/v1/organization/time-control/synchronizations` lê os diretórios do Monday e do VR Mais e persiste o resultado por organização.
- `GET /api/v1/organization/time-control/external-identities` permite pesquisar e filtrar identidades ativas, associadas ou pendentes.
- `POST /api/v1/organization/time-control/people/invitations` associa uma identidade Monday a uma identidade VR Mais e envia o convite do usuário.
- `GET /api/v1/organization/time-control/people` lista as associações e o estado do convite/usuário.
- `GET /api/v1/organization/time-control/history` consulta os registros persistidos de pessoas autorizadas em períodos de até 60 dias.

O aceite do convite liga a conta criada à associação já aprovada pelo administrador. IDs externos não podem ser usados por duas pessoas, as sincronizações são isoladas por organização e falhas das fontes são apresentadas separadamente. Consulte [o contrato administrativo](docs/workforce-admin-integration.md) para os filtros, respostas e estados.

## Início rápido com containers

Este Compose é para desenvolvimento. Para a VM, use exclusivamente [compose.production.yaml](compose.production.yaml) e siga [o guia de produção](deploy/README.md).

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

Os e-mails são enfileirados junto com a operação no banco e entregues em segundo plano. Uma falha temporária do SMTP não desfaz o cadastro; a fila tenta novamente até a expiração do código.

## Configuração de produção

As chaves usam a sintaxe hierárquica do ASP.NET Core (`__` em variáveis de ambiente):

- `ConnectionStrings__Postgres`: conexão PostgreSQL.
- `Jwt__PrivateKeyPem`: chave RSA privada PEM; obrigatória em produção. Quebras de linha podem ser fornecidas como `\n`.
- `Jwt__KeyId`: identificador público da chave ativa.
- `Jwt__PreviousPublicKeys__0__KeyId` e `Jwt__PreviousPublicKeys__0__PublicKeyPem`: chaves anteriores aceitas durante rotação.
- `Email__Host`, `Email__Port`, `Email__UseSsl`, `Email__Username`, `Email__Password`, `Email__FromAddress` e `Email__FromName`: SMTP.
- `Email__UseSsl=true` usa TLS direto (geralmente 465); `false` exige STARTTLS (geralmente 587). Transporte sem TLS só é permitido fora de produção, explicitamente com `Email__AllowInsecureTransport=true`.
- `DataProtection__KeysPath`: diretório persistente das chaves que protegem o conteúdo da fila de e-mails; obrigatório em produção.
- `WorkforceIntegrations__Monday__Enabled`, `WorkforceIntegrations__Monday__Token`, `WorkforceIntegrations__Monday__BoardId` e `WorkforceIntegrations__Monday__ApiVersion`: leitura dos usuários ativos inscritos no board configurado.
- `WorkforceIntegrations__VrMais__Enabled` e `WorkforceIntegrations__VrMais__Token`: leitura do cadastro de colaboradores no Ponto VR Mais.
- `ReverseProxy__KnownProxies__0`: IP do proxy confiável. Nunca confie em cabeçalhos de IP de qualquer origem.

Arquivos montados em `/run/secrets` também são lidos como configuração hierárquica, por exemplo `Jwt__PrivateKeyPem`. No Compose de produção, as credenciais do banco para execução e migração são diferentes.
Tokens das integrações devem ser fornecidos pelo mesmo mecanismo de segredos, nunca gravados em `appsettings.json` ou no repositório.

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

Os testes de integração e segurança exigem Docker e usam PostgreSQL real. Incluem recuperação, revogação, concorrência, isolamento entre organizações, retentativa de SMTP e proxy. A coleção de exemplos está em `requests/cep-api.http` e o contrato do cliente em `docs/plugin-integration.md`.

Para validar a imagem, as migrations e o fluxo HTTPS com dados descartáveis (PowerShell 7):

```powershell
docker build -t cep-api:security-review .
./deploy/Test-ProductionStack.ps1
```

Endpoints de operação:

- `GET /health/live`: processo em execução.
- `GET /health/ready`: acesso ao PostgreSQL confirmado.
- `GET /.well-known/jwks.json`: chaves públicas de assinatura.
- `GET /swagger`: documentação interativa somente fora de produção.
