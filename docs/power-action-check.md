# Verificação de liberação de energia

Contrato do backend para o desktop CEP Horas. Não executa ações no Windows, não altera registros e não gera notificações.

- `POST /api/v1/me/time-control/power-action-check`, corpo obrigatório `{ "action": "shutdown" | "restart" | "hibernate" }`.
- `GET /api/v1/me/time-control/power-action-status?action=shutdown`, para o menu. `action` é opcional, padrão `shutdown`. Usa exatamente a mesma decisão.
- `POST /api/v1/me/time-control/power-action-unlock`: [PIN dedicado e janela individual de 5 minutos](power-admin-unlock.md). Uma janela ativa libera as três ações antes de consultar fontes; fora dela a análise normal permanece.
- Autenticação Bearer obrigatória. Nenhuma das rotas aceita seleção de pessoa, usuário, organização ou período. O servidor consulta somente a pessoa associada ao usuário autenticado na sua organização ativa, sem exigir papel de coordenador ou vínculo de time.

O período é o dia civil atual em America/Sao_Paulo, até um corte único capturado antes das consultas. As fontes são consultadas ao vivo somente para as identidades da pessoa; relatórios antigos e histórico importado não liberam a ação. Usa o mesmo serviço de análise das notificações, motor de domínio e versão/tolerância global vigente. A decisão vale somente para o instante retornado; o desktop refaz o POST quando o usuário solicita a ação.

## Resposta HTTP 200

Todos os campos são obrigatórios. `analysis` pode ser null quando existe override ativo, a pessoa não está associada ou suas identidades estão inativas:

```text
PowerActionCheckResponse {
  action: "shutdown" | "restart" | "hibernate",
  decision: "allowed" | "blocked" | "indeterminate",
  code: "within_tolerance" | "above_tolerance" | "analysis_incomplete"
        | "workforce_person_not_associated" | "external_identity_inactive" | "administrative_override",
  message: string,
  analysis: TimeAnalysisResponse | null,
  override: boolean,
  unlockedUntil: date-time|null
}
TimeAnalysisResponse {
  from: date (YYYY-MM-DD), to: date, cutoff: date-time,
  toleranceMinutes: integer, settingsVersion: uuid,
  days: [{ day: date, vrSeconds: integer|null, mondaySeconds: integer|null,
           deltaSeconds: integer|null, partial: boolean, issues: string[] }],
  vrSeconds: integer|null, mondaySeconds: integer|null,
  deltaSeconds: integer|null, absoluteDivergenceSeconds: integer|null,
  hasIssues: boolean,
  sources: [{ source: "monday"|"vrMais", status: "complete"|"incomplete",
              errorCode: string|null, observedAt: date-time }]
}
```

- `allowed`: diferença conhecida dentro da tolerância, inclusive no limite exato.
- `blocked`: diferença absoluta diária acima da tolerância. Mensagem objetiva em português. O cliente não calcula diferenças.
- `indeterminate`: fonte ausente/indisponível/incompleta, cobertura insuficiente, identidade inativa, pessoa não associada ou registros sem resultado conclusivo. Não libera a ação. Valores desconhecidos são null. Uma consulta completa sem sessões Monday pode representar zero; VR ausente não vira zero. Dia atual `partial=true` é esperado e, sozinho, não impede liberação; entrada VR ou cronômetro Monday abertos são calculados até o mesmo corte pelo motor.

## Erros

`application/problem+json`: `{ type?: string, title: string, status: integer, detail?: string|null, instance?: string, code: string, correlationId: string, errors?: { [field]: string[] } }`.

| HTTP | code | Significado |
|---|---|---|
| 400 | invalid_power_action / validation_failed | Ação ausente/inválida ou JSON inválido. |
| 401 | unauthorized | Sessão ausente, inválida ou revogada. |
| 403 | forbidden | Conta sem organização operacional. |
| 429 | rate_limit_exceeded | Limite de consultas atingido. |
| 500 | unexpected_error | Falha técnica interna; não há decisão. |

Falha das fontes externas esperada retorna 200 `indeterminate`, com código por fonte; não representa API offline. HTTP 200 não significa liberação: verificar `decision`. Erros HTTP, resposta inválida, autenticação negada ou regra negada não devem ser convertidos em contingência offline. A contingência local que permite a ação quando a API está realmente inacessível é responsabilidade do desktop, fora deste contrato.

O OpenAPI gerado em `/swagger/v1/swagger.json` descreve os DTOs e as rotas. A extensão de PIN exige a migration `PowerActionOverrides` e provisionamento seguro antes de liberar o endpoint de desbloqueio.
