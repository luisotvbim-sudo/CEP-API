# Auditoria e refatoração interna — 06/10/2026

Escopo autorizado: limpeza e SOLID/Clean Code pragmáticos preservando comportamento, commits selecionáveis e aplicação somente em Docker local de teste. Base `a2715de`; branch `codex/backend-deep-cleanup`. Não representa produção, merge, release ou homologação Windows/fontes reais.

## Entregas por tema

| Commit | Resultado |
|---|---|
| `b118cdc` | Mapeamento comum de auditoria, mantendo o JSON de detalhes e escopos dos controllers. |
| `36df148` | Projeções paginadas de usuários e auditoria compartilhadas; filtros de autorização continuam antes da projeção. |
| `039189a` | Regressão PostgreSQL para paginação, detalhes JSON, equivalência das consultas administrativas e isolamento entre organizações. |
| `79056cb` | Fixture de energia usa corte de negócio fixo, sem alterar relógio de autenticação nem produção. |
| `8111737` | Processador recebe `FreshTimeAnalysisService` por DI, como os demais consumidores. |
| `2d0b683` | `TimeNotificationScheduler` enfileira slots; `TimeNotificationProcessor` mantém lock, revalidações, leitura atual e publicação transacional. |
| `ac29fa1` | Outbox, dispatcher e worker em arquivos próprios, sem alteração de tipos, SQL, retry ou entrega. |

Não houve alteração de rotas/DTOs, fórmula, tolerância, período, configuração de fontes, schema ou migration. O endpoint legado de accept continua por compatibilidade. Ausência de consumidor local não demonstra obsolescência; o inventário transversal está no orquestrador.

## Responsabilidades e invariantes conferidos

- `OrganizationScopeService`, `TimeControlAccessService` e controllers continuam responsáveis por autorização organizacional/pessoal. Os helpers recebem consultas já filtradas; não escolhem organização.
- `AuthenticationSessionService`, `CredentialAuthenticationService` e `RefreshTokenSessionService` mantêm responsabilidades separadas, locks por conta, rotação, revogação de família e expiração absoluta web. Não introduzido replay automático.
- `FreshTimeAnalysisService` continua lendo fontes para um único corte e delegando cálculo a `TimeAnalysisEngine`; falha e incompletude mantêm valores desconhecidos. DI elimina construção manual no fluxo produtivo de notificações.
- O scheduler é chamado dentro do lock de sessão PostgreSQL existente. Preserva relatório civil inclusive fim de semana, avisos em dias úteis, recuperação somente de slots do mesmo dia e a barreira `UpdatedAt`. Não foi transformado em serviço independente que dispense o lock.
- O processor mantém rede fora da transação curta e revalidação de agenda/configuração sob `FOR SHARE` antes da publicação. Deduplicação, limites e cortes capturados não mudaram.
- `WorkforceDirectorySyncService` mantém orçamento por fonte, coordenação exclusiva do DbContext, progresso independente, observação das tarefas e finalização com token independente. Writer/normalizer e reader Monday já têm responsabilidades separadas; não reescritos sem necessidade funcional.
- Adaptadores Monday/VR preservam IDs externos, paginação limitada, atribuição não ambígua, consultas VR com concorrência limitada e lacunas explícitas. O parser não passou a inferir cobertura, titular, folga ou direção de batidas.
- Outbox preserva payload protegido, `FOR UPDATE SKIP LOCKED`, transação de entrega e retry; entrega continua ao menos uma vez. Enfileirar não prova SMTP aceito.
- Tratamento HTTP mantém ProblemDetails com código/correlação e respostas genéricas. Os logs de exceções inesperadas ainda incluem o objeto da exceção no handler/framework: requer revisão específica de observabilidade se houver necessidade de endurecer redaction, considerando também os logs do middleware; não tratado como garantia absoluta de anonimização.

## Falha de teste confirmada

A fixture de energia iniciava Monday à meia-noite e VR às 00h33m20s, usando corte real. Antes dessa batida, o motor corretamente limita a diferença ao tempo transcorrido; a expectativa fixa de 2.000s podia falhar. Agora somente o controller e serviço de análise do teste usam 05/10/2026 às 12h São Paulo. JWT e sessão seguem relógio real. Testes focados: 5/5; não houve mudança da decisão produtiva.

## Validação local

- `dotnet build CEP-API.sln -c Release -m:1`: sucesso, zero erros/avisos.
- `dotnet test tests/CepApi.UnitTests -c Release --no-build`: 119/119.
- `dotnet test tests/CepApi.IntegrationTests -c Release --no-build`: 92/92 com PostgreSQL real via Testcontainers.
- Após extrair scheduler: 14/14 casos de notificações, além da suíte final completa.
- `dotnet ef migrations has-pending-model-changes --no-build --configuration Release --project src/CepApi.Infrastructure/CepApi.Infrastructure.csproj --startup-project src/CepApi.Api/CepApi.Api.csproj`: nenhuma alteração pendente.
- `git diff --check`: sem erros.

TRX locais: `cleanup-deterministic.trx`, `cleanup-scheduler.trx`, `cleanup-final-unit.trx`, `cleanup-final-integration.trx`. Não contêm uma evidência de CI remota por si só.

## Aplicação somente em Docker local

APIs atualizadas com `docker compose up -d --no-deps --no-build api`, sem executar migration. A imagem de teste foi construída deste worktree; o mock deriva dela e conserva a camada CA local e usuário `app`.

- Revisão funcional das duas imagens: `ac29fa11032c1459f6a7729695edb90e0743cc30`, verificada por label e digest do container.
- Teste: `sha256:ddbed2cfb5749ccb664b1bfdf4c65fe26eae1829b444da166e6fae5f92be706f`.
- Mock com CA: `sha256:35bc03b4afa5183d0849247802575913ee8f1b5643da3cc2999659650fce42fd`.
- HTTPS local 8443/9443 e API direta 8080: `/health/live` e `/health/ready` 200; `/api/v1/me` sem sessão 401.
- IDs dos containers PostgreSQL e fonte mock preservados; mesmos volumes de Data Protection, mounts de configuração e rede mock `internal=true`. Não alterados Compose/Front pelo executor da API. A outra frente atualizou o Front concorrentemente para `cep-front-refactor:1463382`.
- Imagens anteriores retidas localmente em tags `before-deep-cleanup`; volumes/contas/fontes não foram recriados. Não chamada sincronização nem SMTP real nesta validação.
- OpenAPI obtido da API direta e de dentro do container mock: documentos idênticos, 53 caminhos/77 schemas. Comparação recursiva com snapshot versionado: operações, parâmetros, respostas e schemas iguais; única diferença é a ordem de `Organizations`/`OrganizationUsers` na lista de tags, sem mudança de contrato. Proxy serve HTML do Front em `/swagger`; comparação usa o endpoint direto da API.

Os checks HTTP verificam disponibilidade e proteção sem sessão; comportamento autenticado, isolamento, concorrência e persistência foram validados pelos testes PostgreSQL. Não comprovam execução operacional de todos os jobs nem cobertura das fontes. Commits documentais posteriores não mudam binários.

## Dependências de revisão

PR deve usar `codex/sync-seventeen-days` como base (`a2715de`), preservando a seleção independente: senha mínima de seis está no PR #22 e janela de 17 dias no PR #23. Não misturar esses requisitos com a refatoração nem considerar a cadeia integrada em main.

Homologação real Monday/VR, SMTP, Windows/energia e produção não fazem parte desta validação. CI/PR e ambientes locais são estados separados. Não há dependência nova no Front porque o contrato foi preservado.
