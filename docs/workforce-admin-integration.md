# Contrato administrativo — Monday e VR Mais

Este contrato cobre a etapa de descoberta, associação e convite das pessoas que usarão o CEP Horas. A atualização manual de 17 dias pode ser solicitada por qualquer usuário autenticado da organização, limitada às pessoas do seu escopo. Reprocessamento completo, associação, convite e demais mutações administrativas exigem `OrganizationAdmin`; um `SystemAdmin` pode executá-las somente quando seleciona explicitamente a organização por `organizationId`. As consultas de pessoas e histórico também aceitam `User`, mas a API limita o Líder aos times que lidera e o Membro aos próprios dados.

Para usuários da organização, o escopo vem do token e um `organizationId` diferente é rejeitado. Para `SystemAdmin`, `organizationId` é obrigatório nas rotas sob `/organization`.

## Fluxo

1. O administrador solicita a sincronização dos diretórios.
2. A API consulta as duas fontes, persiste identidades por `source + externalId` e mantém o último retrato válido de cada fonte.
3. O front lista identidades ainda não associadas e permite pesquisar por nome, e-mail ou ID externo.
4. O administrador confirma uma identidade Monday e uma identidade VR Mais para a mesma pessoa.
5. A API grava a associação, cria o convite e enfileira o e-mail.
6. Ao aceitar o convite, a conta criada é ligada à associação. Consultas futuras usam os IDs gravados, não nome ou e-mail, para delimitar os dados da pessoa.

## Sincronização

### `POST /api/v1/organization/time-control/synchronizations`

Executa a sincronização das duas fontes. Sem `full=true`, cobre hoje e os 16 dias anteriores: Membro atualiza a própria associação; Líder, sua associação e as dos membros de seus times vigentes; Coordenador, todas as pessoas associadas da organização. Um usuário sem nenhuma identidade ativa associada no escopo recebe HTTP 409 (`sync_scope_empty`). Uma organização não pode iniciar outra sincronização enquanto uma execução estiver com estado `running` (HTTP 409, `sync_already_running`).

Os conectores executam independentemente: o diretório e os registros de uma fonte são aplicados assim que ficam disponíveis, sem esperar a outra. A persistência é serializada no contexto da requisição; não há acesso concorrente ao mesmo `DbContext`. A tentativa usa um único período para ambas as fontes.

`WorkforceIntegrations__SynchronizationSourceTimeoutSeconds` limita a coleta de diretório e registros de cada fonte em conjunto (padrão 120 segundos, limitado a 1–120). Esse prazo também cobre paginação e leitura do corpo HTTP, que o timeout individual de cabeçalhos não cobre. Ao esgotar o prazo, a fonte termina com `source_sync_timeout`; seu histórico anterior é preservado e a outra fonte continua. Um prazo não comprova ausência de dados nem reduz silenciosamente o intervalo solicitado.

Cancelamento/desconexão encerra as fontes ainda em andamento com `sync_cancelled`, usando uma janela independente de até 15 segundos para persistir o estado e liberar nova tentativa. Resultados já gravados permanecem. Se o processo morrer ou o banco impedir a finalização, a recuperação histórica de tentativas com mais de duas horas permanece necessária ao tentar novamente. Isso não transforma o POST em job durável nem elimina limites do proxy.

Na carga inicial administrativa, ter somente o diretório Monday não conclui o bootstrap: enquanto faltar o diretório de uma fonte, uma nova solicitação administrativa normal recarrega os diretórios e a janela inicial de 90 dias. O escopo de Membro/Líder permanece limitado às associações autorizadas.

A resposta contém um resultado geral e um resultado independente para `monday` e `vrMais`:

```json
{
  "id": "0199...",
  "status": "succeeded",
  "startedAt": "2026-09-21T01:00:00Z",
  "completedAt": "2026-09-21T01:00:02Z",
  "sources": [
    {
      "source": "monday",
      "status": "succeeded",
      "receivedCount": 50,
      "createdCount": 2,
      "updatedCount": 1,
      "deactivatedCount": 0,
      "timeRecordReceivedCount": 120,
      "timeRecordCreatedCount": 8,
      "timeRecordUpdatedCount": 3,
      "timeRecordRemovedCount": 0,
      "completeSnapshot": true,
      "coverageFrom": "2026-09-19",
      "coverageTo": "2026-09-20",
      "errorCode": null,
      "errorMessage": null
    }
  ]
}
```

Uma fonte pode falhar sem descartar o resultado válido da outra. Nesse caso, o lote fica `partiallySucceeded`. Identidades ausentes só são marcadas como inativas quando a fonte declara que o retrato recebido está completo.

O primeiro pedido administrativo sem diretório persistido faz a carga inicial de 90 dias. Pedidos normais seguintes usam somente identidades externas já associadas e ativas, sem recarregar os diretórios. O VR Mais recebe os IDs de funcionários e as datas na requisição. O diretório Monday reúne os usuários ativos atribuídos na coluna Pessoa `PROFISSIONAL`, mesmo que não estejam inscritos no board; a coluna `R.T.` não identifica o profissional nem atribui horas. Os itens são filtrados por `PROFISSIONAL` na origem. Um subitem usa seu próprio `PROFISSIONAL` quando preenchido e, caso contrário, herda o profissional do item pai. O conector lê os subitens dos pais filtrados e também consulta diretamente subitens com coluna própria de profissional, para não perder aqueles cujo pai pertence a outra pessoa. A sessão é atribuída ao profissional efetivo, não a `started_user_id`. Como a API do Monday não filtra por data dentro do histórico do cronômetro, a aplicação seleciona localmente as sessões dos 17 dias após a leitura. Chaves externas únicas tornam a importação idempotente. Registros anteriores à retenção de 90 dias são removidos após sincronização bem-sucedida da fonte.

Em um pedido normal, `completeSnapshot=true` significa que a fonte completou o período **e as pessoas solicitadas**, não que toda a organização foi recarregada. `receivedCount=0` indica que o diretório não foi consultado nessa tentativa.

O conector escolhe primeiro a coluna Pessoa com título exato `PROFISSIONAL` (sem distinguir maiúsculas/minúsculas), depois uma única coluna cujo título contém “respons”, ou a única coluna Pessoa existente. A escolha é feita no board principal e, se houver coluna Pessoa, no board oculto de subitens clássicos. Se a coluna necessária for ambígua, a fonte falha com `monday_responsible_column_unavailable`. Um item com vários profissionais ainda contribui com as identidades ao diretório, mas suas sessões são ignoradas; subitens sem profissional próprio herdam essa ambiguidade e também são ignorados. Um subitem com profissional próprio único pode ser atribuído normalmente. Não se distribuem nem duplicam horas por suposição. Quando o board de subitens não possui coluna Pessoa, os subitens herdam do pai. A configuração real do board precisa ser homologada; não inferir seu estado pela documentação.

Para recarregar diretórios ou reprocessar correções fora da janela normal, o coordenador pode usar `POST /api/v1/organization/time-control/synchronizations?full=true`. Isso recarrega diretórios e os últimos 90 dias. Membro e Líder recebem HTTP 403 (`full_sync_forbidden`) ao solicitar `full=true`.

Consultas de estado:

- `GET /api/v1/organization/time-control/synchronizations/latest`
- `GET /api/v1/organization/time-control/synchronizations/{batchId}`

Membro e Líder consultam somente as sincronizações que eles próprios solicitaram; Coordenador vê todas as tentativas da organização. Uma atualização de 17 dias não prova cobertura completa da organização. A listagem de identidades externas continua restrita à administração.

## Identidades externas

### `GET /api/v1/organization/time-control/external-identities`

Filtros:

- `source=monday|vrMais`
- `activeOnly=true|false`
- `mapped=true|false`
- `search=texto`
- `page` e `pageSize`

Cada item informa `id`, `source`, `externalId`, `displayName`, `email`, `isActive`, `lastSeenAt` e `workforcePersonId`. O front deve enviar o `id` interno da identidade ao criar a associação; nunca deve tentar associar pessoas somente pelo nome.

## Associação e convite

### `POST /api/v1/organization/time-control/people/invitations`

```json
{
  "email": "pessoa@empresa.com",
  "displayName": "Pessoa da Silva",
  "mondayIdentityId": "0199...",
  "vrMaisIdentityId": "0199..."
}
```

As duas identidades precisam estar ativas, pertencer à mesma organização e ainda não estar associadas. O convite criado tem papel `User` e validade de 48 horas. O e-mail só é enfileirado depois que a associação é validada.

### `GET /api/v1/organization/time-control/people`

Aceita `search`, `page` e `pageSize`. Cada item contém as duas identidades, o `userId` quando o convite já foi aceito, e o estado do convite mais recente.

## Histórico administrativo

### `GET /api/v1/organization/time-control/history`

Parâmetros obrigatórios:

- `from=YYYY-MM-DD`
- `to=YYYY-MM-DD`

O intervalo é inclusivo e não pode ultrapassar 90 dias. Filtros opcionais:

- `workforcePersonId`
- `source=monday|vrMais`
- `search=texto`

A resposta agrupa os registros por pessoa associada. Cada registro preserva a chave externa, data efetiva, duração conhecida, situação da fonte e detalhes seguros — por exemplo, batidas do VR ou identificação da atividade do Monday. Registros desconhecidos permanecem com duração `null`; ausência nunca é convertida silenciosamente em zero. A consulta é registrada em auditoria.

## Configuração segura

As integrações ficam desabilitadas por padrão. Em ambiente seguro, configure:

```text
WorkforceIntegrations__Monday__Enabled=true
WorkforceIntegrations__Monday__Token=<segredo>
WorkforceIntegrations__Monday__BoardId=<board-configurado>
WorkforceIntegrations__Monday__ApiVersion=2026-07
WorkforceIntegrations__VrMais__Enabled=true
WorkforceIntegrations__VrMais__Token=<segredo>
```

Os tokens pertencem ao servidor e não são retornados em respostas, logs ou Swagger. Em produção, forneça-os por arquivos em `/run/secrets` ou pelo gerenciador de segredos da plataforma.

## Integração com as análises

O histórico inclui [resumo diário importado](workforce-daily-history.md), sem certificar cobertura ou atualidade. [Análises e notificações](time-notifications.md) já têm motor, tolerância global, ocorrências e relatórios persistidos; fontes ao vivo são consultadas pelo processamento correspondente. Calendário completo, justificativas e workflow de casos permanecem planejados na [especificação](conciliacao-horas/especificacao-funcional.md). Não atribuir significado trabalhista nem homologação a dados importados por existir o código.
