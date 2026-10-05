# Acompanhamento pessoal de horas

Contrato aditivo da [demanda #16](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues/16). O [OpenAPI gerado](openapi-current.json) define os schemas completos. A consulta é independente da autorização de energia, de relatórios persistidos e de importações do histórico.

## Consulta

`GET /api/v1/me/time-control/overview?period=daily`, autenticado. `period` aceita `daily` (padrão), `weekly`, `sprint` e `previousDay`. A API resolve os períodos oficiais pelo calendário São Paulo e retorna `periods` com suas datas. Não aceita selecionar titular/organização: parâmetros extras não alteram a identidade da sessão; SystemAdmin sem organização operacional recebe 403.

`Cache-Control: no-store`. HTTP 400 para período inválido, 401/403 para acesso, 429 para limite por conta e 503 com `overview_timeout` para prazo total excedido. Erro HTTP não é indisponibilidade de transporte nem autorização de energia. API antiga deve ser tratada pelo consumidor como capacidade ausente (404).

## Resposta e significado

Os sete campos de `PersonalOverviewResponse` são obrigatórios; `analysis` pode ser nulo.

| Campo | Significado |
|---|---|
| `period`, `periods`, `cutoff` | Seleção e períodos oficiais, com um único corte capturado antes das fontes |
| `status` | `regular`, `difference`, `incomplete`, `notAssociated` ou `inactiveIdentity` |
| `analysis` | `TimeAnalysisResponse` existente: configuração/versionamento, dias, totais, saldo assinado, divergência absoluta e fontes |
| `attentionDays` | Dias com diferença confirmada ou informação insuficiente, ocorrências existentes e `partial` |
| `availableSourceDays` | Valores diários de leituras individuais completas no mesmo corte; não são subtotais certificados do período |

Regularidade requer fontes completas, dias calculáveis e ausência de problema de integridade. Diferença utiliza `above_tolerance` do motor compartilhado, inclusive seu limite simétrico estritamente superior à tolerância. Informação insuficiente prevalece no período sem apagar os dias cuja diferença é confirmada. Saldo zero não elimina diferenças de sinais opostos. Dia atual continua parcial mesmo quando calculável.

Associação inexistente/inativa retorna estado explícito, `analysis: null` e listas diárias vazias, sem consultar fontes. Desconhecido permanece nulo. O motor conserva a semântica anterior: falha de uma fonte mantém os totais da análise inconclusivos. A projeção por fonte usa o mesmo motor e somente snapshots completos, direcionados às identidades desta pessoa e abrangendo o período solicitado; não usa lote organizacional ou data de importação como cobertura individual. Não publica prazo de frescor, calendário, workflow, percentual ou severidade nova.

## Custo, concorrência e efeitos

Uma consulta lógica por conector para a pessoa/período compõe resumo e detalhe; a projeção de valores disponíveis não repete consultas externas. Os conectores mantêm paginação e limites internos. Prazo total: 90 s; timeout HTTP dos conectores: 45 s por chamada. Chamadas HTTP paginadas dependem das fontes: não prometer duas chamadas de rede. Cancelamento HTTP é propagado. O limite por conta existente é aplicado; o consumidor deduplica equivalentes, serializa consultas e descarta seleções superadas antes de chamar fontes. Não há polling nem tentativa automática após erro/429.

GET usa leituras sem tracking e não grava relatórios, notificações, recibos, importações ou liberações de energia. Sem migration. Publicar API compatível antes do consumidor; entrega em branch não comprova deploy nem fontes reais.
