# CEP API

Backend multiempresa em ASP.NET Core 10, Identity, EF Core e PostgreSQL. Atende administração de organizações/usuários/produtos, autorização dos plugins Revit/ZWCAD e CEP Horas: integrações Monday/VR Mais, histórico, análises, notificações e decisões de energia para o desktop.

Comece por [AGENTS.md](AGENTS.md), [contexto atual](docs/CONTEXTO-ATUAL.md) e [índice da documentação](docs/README.md). A [especificação funcional](docs/conciliacao-horas/especificacao-funcional.md) distingue implementação da visão futura; [decisões e pendências](docs/DECISOES-PENDENCIAS.md) registra o que permanece aberto. Coordenação transversal: [CEP-ORQUESTRADOR](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR).

## Funcionamento atual

A consulta de [acompanhamento pessoal](docs/personal-overview.md) retorna períodos oficiais, situação e dias de atenção, sem gerar relatórios ou autorizar energia.

- Isolamento por organização, papéis SystemAdmin/OrganizationAdmin/User e acesso de membro/líder recalculado pelos vínculos vigentes hoje.
- Login nativo com JWT RS256 e refresh rotativo; [sessão web](docs/browser-sessions.md) de até sete dias com cookie protegido de mesma origem. Revogação da família e versão de segurança são verificadas no bearer.
- Convites sem cadastro público, [ativação sem sessão](docs/invitation-activation.md), [reenvio administrativo](docs/people-invitation-resend.md), recuperação e outbox SMTP transacional.
- [Associação/importação](docs/workforce-admin-integration.md) por IDs Monday/VR; atualização normal de 20 dias inclusivos e inicial/full administrativa de 90 dias. Fontes falham independentemente.
- [Histórico diário](docs/workforce-daily-history.md) importado e [motor/relatórios/notificações](docs/time-notifications.md) com corte único São Paulo, tolerância global inicial de 30 minutos e valores desconhecidos nulos. GET análises lê snapshots; não atualiza fontes.
- [Energia](docs/power-action-check.md) decidida pela API; [PIN dedicado](docs/power-admin-unlock.md) abre liberação individual de cinco minutos. A execução Windows pertence ao CEP-FRONT.
- [Telemetria desktop proposta](docs/desktop-telemetry.md): eventos estruturados reportados pelo cliente; implantação e ativação dependem de validação separada.
- [Grants dos plugins](docs/plugin-integration.md) de no máximo 72 horas; existência deste contrato não comprova consumo pelos plugins reais.

Calendário completo, workflow de justificativas/casos, ranking avançado e exportações não são capacidades completas atuais. Código, GitHub e versão implantada devem ser conferidos separadamente.

## Desenvolvimento com containers

Use `compose.yaml` somente em desenvolvimento; produção possui [guia próprio](deploy/README.md).

```bash
docker compose up --build -d
```

API em `http://localhost:8080`, Swagger em `/swagger` e Mailpit em `http://localhost:8025`. Compose aplica migration antes da API. Configure as credenciais de bootstrap em ambiente seguro e execute uma única vez `docker compose run --rm api bootstrap-admin`. Não incluir valores no histórico de comandos ou no Git. Bootstrap só cria o primeiro SystemAdmin e exige domínio permitido.

## Desenvolvimento sem containers

Requer .NET SDK 10 e PostgreSQL configurado. Forneça a conexão e demais segredos por mecanismo externo; use configuração de desenvolvimento e serviços descartáveis para testes.

```bash
dotnet tool restore
dotnet restore CEP-API.sln
dotnet run --project src/CepApi.Api -- migrate
dotnet run --project src/CepApi.Api -- bootstrap-admin
dotnet run --project src/CepApi.Api
```

Bootstrap requer `BootstrapAdmin__Email`, `BootstrapAdmin__Password` e opcionalmente `BootstrapAdmin__DisplayName`, fornecidos de forma segura. Nos ambientes não Development, entrega de convites/recuperação usa SMTP. Development pode registrar códigos em log local: proteja esse ambiente e não copie seus logs para documentação.

## Configuração

ASP.NET usa `__` para chaves hierárquicas do ambiente; arquivos de secrets também podem fornecer essas chaves. Valores reais permanecem fora do repositório.

| Grupo | Responsabilidade |
|---|---|
| `ConnectionStrings__Postgres` | Conexão de execução; produção separa proprietário/migration de runtime |
| `Jwt__PrivateKeyPem`, `Jwt__KeyId`, `Jwt__PreviousPublicKeys` | Assinatura RS256 e rotação, mantendo públicas anteriores pela janela necessária |
| `DataProtection__KeysPath` | Chaves persistentes dos cookies e payloads da outbox |
| `Email__*` | SMTP com TLS em produção e link seguro de ativação |
| `WorkforceIntegrations__Monday__*` e `WorkforceIntegrations__VrMais__*` | Habilitação, origem, IDs/configuração e credenciais exclusivamente no servidor |
| `ReverseProxy__KnownProxies` | Proxies confiáveis para IP/esquema; não confiar em qualquer origem |
| `TimeNotifications__WorkerEnabled` | Processador; desabilitar mantém pedidos pendentes |

As integrações externas e o envio automático de avisos nascem desabilitados. Habilitar exige configuração e homologação; salvar agenda não prova disparo. SMTP enfileirado não prova entrega.

Os e-mails de ativação e recuperação compartilham um HTML simples em `SecurityEmailTemplate.cs`, com versão alternativa em texto. `Email__InvitationActivationUrl` e `Email__PasswordRecoveryUrl` configuram os destinos HTTPS dos botões, por padrão `https://plugincep.com.br/?convite=1` e `https://plugincep.com.br/`. O botão de recuperação abre o portal; o código deve ser usado na tela em que foi solicitado.

O convite dá boas-vindas ao CEP Horas pela organização e orienta ativar a conta, instalar com auxílio da TI e entrar com e-mail e senha. O segundo botão, **Baixar CEP Horas para Windows — MSI**, usa `Email__DesktopDownloadUrl` (padrão `https://plugincep.com.br/download`). Nos arquivos Compose, `DESKTOP_DOWNLOAD_URL` configura esse valor. O endereço precisa ser HTTPS público, sem credenciais ou dados do convite; a página deve disponibilizar o MSI versionado antes de publicar esta mudança. A API não anexa o instalador, não acrescenta código/e-mail ao download e não altera a recuperação ou a ativação sem sessão. Prévia sintética: [convite com MSI](docs/convite-download-msi.md).

## Contrato e pontos de código

[OpenAPI atual](docs/openapi-current.json) possui 52 caminhos e 72 schemas na base revisada em 04/10/2026. Regenerar em alterações de contrato; a quantidade é evidência da revisão, não requisito fixo de produto. Controllers estão em `src/CepApi.Api/Controllers`; [contexto atual](docs/CONTEXTO-ATUAL.md) aponta serviços e motor por fluxo. Exemplos HTTP: `requests/cep-api.http`.

## Validação

```bash
dotnet build CEP-API.sln -m:1
dotnet test tests/CepApi.UnitTests
dotnet test tests/CepApi.IntegrationTests
```

Integração usa PostgreSQL real via Testcontainers e exige Docker. Para validar imagem, migrations e HTTPS com dados descartáveis em PowerShell 7:

```powershell
docker build -t cep-api:security-review .
./deploy/Test-ProductionStack.ps1
```

Esses comandos são procedimentos; este texto não registra sua execução. Mudanças funcionais devem verificar os casos afetados de segurança, isolamento, concorrência, fontes, períodos e idempotência. Testes locais/CI não substituem fontes reais, SMTP, VM ou homologação Windows.

## Operação

- `/health/live`: processo; `/health/ready`: acesso PostgreSQL. Saúde não comprova SHA implantado ou execução dos jobs.
- `/.well-known/jwks.json`: chaves públicas.
- `/swagger`: somente fora de produção.
- [Deploy](deploy/README.md), [atualização noturna](deploy/nightly/README.md) e [monitor local](deploy/monitoring/README.md) descrevem scripts versionados. A configuração instalada deve ser verificada na VM.

Migrations em produção são explícitas, com backup validado antes da mudança. Rollback da imagem não reverte schema. Leia a [base de segurança](docs/security-hardening.md) antes de configurar o ambiente.
