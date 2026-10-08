# Telemetria estruturada do desktop — implementação proposta (#47)

Esta entrega prepara a API para o piloto manual da [Issue #45](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues/45). Código, migration e testes locais não comprovam implantação. O cliente desktop correspondente é responsabilidade do CEP-FRONT (#48). Ativação da coleta, migration de produção, backup e acesso operacional ainda exigem revisão antes do rollout.

## Contrato de ingestão

`POST /api/v1/desktop-telemetry/events` exige Bearer de sessão API válida. A origem desktop não é comprovada criptograficamente; qualquer cliente autenticado pode relatar eventos, que permanecem dados não confiáveis. Recebe JSON `{ "events": [ ... ] }` com 1–50 itens e até 32 KiB. Cada item contém:

| Campo | Regra |
|---|---|
| `eventId` | UUID não vazio, estável no reenvio, único dentro do lote |
| `installationId` | UUID aleatório e estável na instalação nativa; não deriva de hardware, nome, SID ou IP |
| `occurredAt` | UTC; máximo de cinco minutos no futuro e idade não maior que `DesktopTelemetry:RetentionDays` |
| `code` | código fechado da tabela abaixo |
| `phase` | `startup`, `authorization`, `schedule`, `cancel`, `reconcile` ou `update`, coerente com o código |
| `outcome` | `success`, `denied`, `failure` ou `uncertain`, coerente com o código |
| `action` | `shutdown`, `restart`, `hibernate` ou `null`; obrigatório em `power_*`, exceto cancelamento falho sem ação conhecida |
| `errorCode` | `none`, `transport_unavailable`, `http_error`, `invalid_response`, `service_unavailable`, `permission_denied`, `timeout` ou `internal_error` |
| `appVersion` | versão `major.minor.patch`, sufixo curto opcional, máximo 32 caracteres |
| `operationId` | UUID do pedido nativo de energia em suas fases; `null` fora de `power_*` e no cancelamento falho sem ação conhecida |

`operationId` relaciona as fases da operação no Windows; **não** é o ID de correlação HTTP da API. Organização e usuário vêm exclusivamente do Bearer validado. O servidor atribui `receivedAt`, distinto do horário reportado `occurredAt`. Nenhum campo aceita texto livre, stack trace, nome de máquina, e-mail, token, PIN ou conteúdo Monday/VR.

O POST pessoal exige organização operacional na sessão. Um `SystemAdmin` sem `org_id` recebe 403; a coleta deste piloto cobre contas organizacionais. O desktop trata 403 como falha não transitória da fila, sem tentar escolher organização pelo cliente.

| Código | Fase | Resultado possível |
|---|---|---|
| `desktop_started`, `desktop_start_failed` | startup | success, failure respectivamente |
| `power_check_allowed`, `power_check_denied`, `power_check_failed` | authorization | success, denied, failure respectivamente |
| `power_schedule_confirmed`, `power_schedule_failed` | schedule | success; failure/uncertain |
| `power_cancel_confirmed`, `power_cancel_failed` | cancel | success; uncertain |
| `power_recovery_required`, `power_reconciled` | reconcile | uncertain; success/uncertain |
| `update_check_failed`, `update_install_started`, `update_install_failed` | update | failure, success, failure respectivamente |

Resposta HTTP 200: `{ "acceptedEventIds": [ ... ], "rejectedEventIds": [ ... ] }`. IDs aceitos incluem duplicatas já persistidas no mesmo escopo; `(OrganizationId, UserId, EventId)` é a chave de idempotência, inclusive sob concorrência. Itens com ID válido mas dados inválidos, relógio fora da janela ou campo adicional entram em `rejectedEventIds`; os válidos do mesmo lote são persistidos numa transação. O desktop remove ambos os conjuntos da fila e só reenvia nos erros transitórios (rede/429/5xx). Lote vazio, grande demais, campo de envelope adicional, ID vazio/duplicado ou JSON estruturalmente inválido retorna erro HTTP e não grava o lote. O cliente deve isolar um item malformado que cause 400 para não bloquear os posteriores.

Todos os eventos são **relatos do cliente**, sem valor autorizativo. `power_schedule_confirmed` significa apenas que o host observou confirmação de agendamento; não prova desligamento efetivo. A API continua decidindo a liberação, WPF revalida e o serviço Windows executa. A trilha local do serviço e eventos do Windows continuam necessários para investigar o resultado final.

`power_recovery_required` pode ter `errorCode=none` para pendência persistida ou um código fechado de falha de comunicação/serviço quando a reconciliação não conseguiu consultar o serviço.

## Leitura, isolamento e retenção

`GET /api/v1/organization/desktop-telemetry/events` exige `OrganizationAdmin` ou `SystemAdmin` com organização selecionada para este último. A consulta filtra obrigatoriamente a organização autorizada, retorna 1–100 itens e aceita filtro `installationId`. A resposta inclui `userId` para distinguir titulares da própria organização, sem nome/e-mail. Paginação usa `before` (UTC), `beforeEventId` e `beforeUserId` juntos: o próximo pedido informa `receivedAt`, `eventId` e `userId` do último item da página. Cada leitura válida gera auditoria mínima `desktop_telemetry.read`, com ator, organização e contagem, sem copiar conteúdo dos eventos. Não existe leitura cruzada entre organizações.

Um serviço remove eventos pelo `receivedAt` na inicialização e a cada 24 horas. `DesktopTelemetry:RetentionDays` aceita 7–365, com **padrão provisório de 30 dias**; definir retenção, acesso de suporte e base legal antes de ativar produção. O cliente deve descartar itens locais anteriores à janela para evitar reenvio inútil. Ingestão tem limite próprio inicial de seis lotes por minuto por conta (`RateLimiting:DesktopTelemetryPerMinute`), separado do limite geral usado na leitura; 429 requer reenvio posterior com os mesmos IDs.

## Migration e rollback

`DesktopTelemetryEvents` adiciona `time_control.desktop_telemetry_events` e índices de retenção/consulta, com chaves estrangeiras para organização e usuário. Concede ao papel runtime SELECT, INSERT e DELETE. Não altera tabelas anteriores; a imagem antiga ignora a nova tabela se for restaurada. **Rollback de imagem não reverte schema**. Fazer backup validado e migration em janela controlada, implantar API, verificar com dados descartáveis e somente depois habilitar cliente. O método `Down` exclui a tabela e seus dados; nunca acioná-lo automaticamente como rollback.

## Verificação local

`dotnet build CEP-API.sln -m:1` e `dotnet test CEP-API.sln -m:1 --no-restore` passaram: 136 testes unitários e 96 testes de integração, estes com PostgreSQL descartável/Testcontainers. `DesktopTelemetryTests` cobre reenvio concorrente, escopo, rejeição de itens inválidos/antigos/futuros, leitura e cursor em lote com mesmo `receivedAt`. `dotnet ef migrations has-pending-model-changes` confirmou modelo sem diferenças. `docs/openapi-current.json` foi regenerado da aplicação em Development. CI, implantação e homologação Windows/VM continuam etapas distintas.
