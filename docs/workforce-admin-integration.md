# Contrato administrativo — Monday e VR Mais

Este contrato cobre a etapa de descoberta, associação e convite das pessoas que usarão o CEP Horas. A atualização manual de sete dias pode ser solicitada por qualquer usuário autenticado da organização, limitada às pessoas do seu escopo. Reprocessamento completo, associação, convite e demais mutações administrativas exigem `OrganizationAdmin`; um `SystemAdmin` pode executá-las somente quando seleciona explicitamente a organização por `organizationId`. As consultas de pessoas e histórico também aceitam `User`, mas a API limita o Líder aos times que lidera e o Membro aos próprios dados.

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

Executa a sincronização das duas fontes. Sem `full=true`, cobre hoje e os seis dias anteriores: Membro atualiza a própria associação; Líder, sua associação e as dos membros de seus times vigentes; Coordenador, todas as pessoas associadas da organização. Um usuário sem nenhuma identidade ativa associada no escopo recebe HTTP 409 (`sync_scope_empty`). Uma organização não pode iniciar outra sincronização enquanto uma execução estiver com estado `running` (HTTP 409, `sync_already_running`).

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

O primeiro pedido administrativo sem diretório persistido faz a carga inicial de 90 dias. Pedidos normais seguintes usam somente identidades externas já associadas e ativas, sem recarregar os diretórios. O VR Mais recebe os IDs de funcionários e as datas na requisição. O Monday filtra itens e subitens na origem pelo campo Pessoa/Responsável; cada sessão é atribuída a esse responsável, não a `started_user_id`. Como a API do Monday não filtra por data dentro do histórico do cronômetro, a aplicação seleciona localmente as sessões dos sete dias após ler somente os itens dos responsáveis solicitados. Chaves externas únicas tornam a importação idempotente. Registros anteriores à retenção de 90 dias são removidos após sincronização bem-sucedida da fonte.

Em um pedido normal, `completeSnapshot=true` significa que a fonte completou o período **e as pessoas solicitadas**, não que toda a organização foi recarregada. `receivedCount=0` indica que o diretório não foi consultado nessa tentativa.

O conector identifica a coluna do tipo Pessoa cujo título contém “respons” (ou a única coluna Pessoa do board). Isso é feito separadamente para o board principal e para o board oculto de subitens clássicos. Se a coluna não puder ser identificada ou uma atividade tiver mais de um responsável, a fonte Monday falha explicitamente (`monday_responsible_column_unavailable` ou `monday_multiple_responsibles`); não distribui nem duplica horas por suposição.

Para preencher a lacuna de uma instalação antiga com apenas 60 dias ou reprocessar correções antigas, o coordenador pode usar `POST /api/v1/organization/time-control/synchronizations?full=true`. Isso recarrega diretórios e os últimos 90 dias. Membro e Líder recebem HTTP 403 (`full_sync_forbidden`) ao solicitar `full=true`.

Consultas de estado:

- `GET /api/v1/organization/time-control/synchronizations/latest`
- `GET /api/v1/organization/time-control/synchronizations/{batchId}`

Membro e Líder consultam somente as sincronizações que eles próprios solicitaram; Coordenador vê todas as tentativas da organização. Uma atualização de sete dias não prova cobertura completa da organização. A listagem de identidades externas continua restrita à administração.

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
WorkforceIntegrations__Monday__BoardId=9920862624
WorkforceIntegrations__Monday__ApiVersion=2026-07
WorkforceIntegrations__VrMais__Enabled=true
WorkforceIntegrations__VrMais__Token=<segredo>
```

Os tokens pertencem ao servidor e não são retornados em respostas, logs ou Swagger. Em produção, forneça-os por arquivos em `/run/secrets` ou pelo gerenciador de segredos da plataforma.

## Limite desta entrega

Esta etapa entrega o cadastro administrativo, a associação das identidades, o convite e o histórico bruto persistido. A conciliação diária completa — calendário, tolerâncias, qualidade, classificação de divergências, justificativas e fluxo do gestor — continua separada para não atribuir significado trabalhista a dados ainda não homologados.
