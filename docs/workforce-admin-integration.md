# Contrato administrativo — Monday e VR Mais

Este contrato cobre a etapa de descoberta, associação e convite das pessoas que usarão o CEP Horas. Todos os endpoints exigem uma sessão com papel `OrganizationAdmin` e operam somente na organização presente no token.

## Fluxo

1. O administrador solicita a sincronização dos diretórios.
2. A API consulta as duas fontes, persiste identidades por `source + externalId` e mantém o último retrato válido de cada fonte.
3. O front lista identidades ainda não associadas e permite pesquisar por nome, e-mail ou ID externo.
4. O administrador confirma uma identidade Monday e uma identidade VR Mais para a mesma pessoa.
5. A API grava a associação, cria o convite e enfileira o e-mail.
6. Ao aceitar o convite, a conta criada é ligada à associação. Consultas futuras usam os IDs gravados, não nome ou e-mail, para delimitar os dados da pessoa.

## Sincronização

### `POST /api/v1/organization/time-control/synchronizations`

Executa a sincronização administrativa das duas fontes. Uma organização não pode iniciar outra sincronização enquanto uma execução estiver com estado `running`.

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

Na primeira execução, cada fonte busca os últimos 60 dias. Depois disso, a janela começa um dia antes da última cobertura concluída, evitando lacunas na virada da sincronização e permitindo capturar correções recentes. Chaves externas únicas tornam a importação idempotente. Registros com mais de 90 dias são removidos após uma sincronização bem-sucedida da fonte.

Para reprocessar correções antigas dentro do limite administrativo, use `POST /api/v1/organization/time-control/synchronizations?full=true`. A carga completa continua limitada aos últimos 60 dias.

Consultas de estado:

- `GET /api/v1/organization/time-control/synchronizations/latest`
- `GET /api/v1/organization/time-control/synchronizations/{batchId}`

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

O intervalo é inclusivo e não pode ultrapassar 60 dias. Filtros opcionais:

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
