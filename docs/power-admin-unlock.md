# Liberação administrativa temporária de energia

O contrato depende da migration `PowerActionOverrides` e de provisionamento seguro do PIN. Antes de publicar mudança de schema ou configuração, revisar manutenção, backup e compatibilidade; conferir o timer instalado na VM. Este documento não afirma que o deploy está pendente ou que o timer esteja suspenso.

`POST /api/v1/me/time-control/power-action-unlock` exige sessão autenticada e um único campo JSON, `pin`: string com exatamente seis dígitos ASCII. Não enviar usuário, organização, administrador ou prazo. O PIN permanece somente em memória no formulário e no corpo HTTPS; nunca registrar, persistir, devolver, colocar na URL ou enviar ao serviço Windows. Apagar o campo após o envio.

O PIN é global, compartilhado e dedicado ao controle de energia. Não é a senha de login de nenhum usuário. `time_control.power_pin_configuration` guarda somente hash salgado Identity V3 (PBKDF2-SHA512, 210 mil iterações), versão e data de rotação. Usar esse algoritmo não envolve consultar credenciais de contas. Nenhum PIN padrão existe na migration; o valor real nunca integra Git, docs, testes, logs ou OpenAPI.

## Contrato

Sucesso HTTP **200**, `Cache-Control: no-store`:

```text
PowerActionUnlockResponse {
  override: true,
  unlockedUntil: date-time UTC,
  serverTime: date-time UTC
}
```

`unlockedUntil = serverTime + 5 minutos` exatamente, após validar o PIN pelo relógio do servidor. `time_control.power_action_overrides` persiste organização, usuário solicitante, versão do PIN, versão de segurança derivada do security stamp do solicitante e datas. Não contém PIN ou hash do PIN. Uma nova validação válida reinicia a janela por 5 minutos e conta como tentativa; PIN inválido não prorroga nem apaga uma janela existente.

`POST /power-action-check` e `GET /power-action-status` ganham campos obrigatórios `override: boolean` e `unlockedUntil: date-time|null`. Durante a janela retornam `decision: "allowed"`, `code: "administrative_override"`, `override: true`, `unlockedUntil` e `analysis: null` para shutdown, restart e hibernate, independentemente de diferença, fontes ou associação. Fora dela retornam a decisão normal, `override: false`, `unlockedUntil: null`. As rotas completas mantêm o prefixo `/api/v1/me/time-control`.

A janela vale enquanto `agora < unlockedUntil`; no instante exato da expiração a regra normal volta. Reinício ou nova sessão não amplia o prazo. Rotação do PIN, alteração do security stamp, suspensão ou mudança de organização do solicitante invalidam a janela. Não cria sessão administrativa e não devolve segredos.

Erros seguem ProblemDetails (`title`, `status`, `code`, `correlationId`; `errors` opcional):

| HTTP | code | Significado |
|---|---|---|
| 400 | validation_failed | Corpo/PIN ausente ou fora do formato. |
| 401 | unauthorized | Sessão inválida/ausente. |
| 403 | invalid_admin_pin | PIN inválido. |
| 403 | forbidden | Conta sem organização ativa/acesso operacional. |
| 429 | power_unlock_rate_limited / rate_limit_exceeded | Limite persistido ou HTTP atingido. |
| 503 | power_pin_not_configured | API acessível, mas PIN não provisionado. |
| 500 | unexpected_error | Falha técnica; não há liberação. |

Limite HTTP: 5 pedidos/minuto por solicitante (`RateLimiting:PowerUnlockPerMinute`). Limite persistido: 5 verificações/15 minutos por usuário, 20/15 minutos por organização e 50/15 minutos globais, contando sucessos e falhas. Auditoria e lock PostgreSQL da configuração do PIN fazem esses limites sobreviverem a reinícios e valerem entre instâncias. Auditoria registra autorização/negação, solicitante, organização, versão do PIN e prazo, sem PIN ou hashes. HTTP 429 do middleware precede a execução; tentativas limitadas pelo banco também têm auditoria. Erros de formato não verificam o hash e são limitados pelo middleware.

## Desktop

WPF envia o PIN somente à API, descarta o segredo e revalida `power-action-check` imediatamente antes de cada ação. O serviço Windows recebe somente a ação validada, nunca o PIN. React pode exibir o prazo, mas não autoriza ações; não confiar em booleano ou relógio local. Nenhum endpoint executa ação no Windows.

Preservar a contingência existente somente quando a API está realmente inacessível. `401`, `403`, `429`, `500`, `503`, PIN inválido e `indeterminate` não são offline. Não existe fallback novo para PIN local.

## Provisionamento e rotação

Aplicar a migration `PowerActionOverrides`. Na VM, via SSH com TTY:

```bash
cd /opt/cep-api
sudo docker compose --env-file .env -f compose.production.yaml run --rm --no-deps migrate configure-power-pin
```

Não usar `-T`, pipe, argumento, variável de ambiente ou arquivo contendo o PIN. O comando pede PIN e confirmação via `Console.ReadKey(intercept: true)`, sem eco. Persiste somente hash e nova versão em transação; não inicia workers, não aplica migration automaticamente e não exibe o hash. O serviço migrate usa credenciais do proprietário; o operador precisa de autorização na VM. Escape cancela sem alteração. A rotação invalida todas as janelas anteriores. Auditoria: `power.pin_rotated`, sem segredo.

Antes do provisionamento, desbloqueio retorna 503; verificações normais continuam funcionando. O valor real é introduzido pelo operador no terminal seguro, sem ferramentas que registrem os caracteres digitados. A existência do comando não comprova provisionamento na VM.

Schemas completos: `docs/openapi-current.json`, exportado da aplicação. Migration: `PowerActionOverrides`, compatível com a versão anterior e com privilégios explícitos para o papel runtime.
