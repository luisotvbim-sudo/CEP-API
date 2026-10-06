# Resumo diário do histórico

`GET /api/v1/organization/time-control/history` inclui `days: TimeAnalysisDay[]` em cada `WorkforcePersonHistoryResponse`, preservando `records` e parâmetros. Não há migration ou novo endpoint.

Os dias são as datas civis distintas dos registros visíveis, em ordem crescente. O filtro `source` limita registros e dias visíveis, mas os totais de cada dia usam as duas identidades da mesma pessoa. A consulta mantém o isolamento por organização e o escopo vigente de coordenador, líder e membro, inclusive quando há filtro de pessoa. O próprio usuário pode ler sua associação e histórico sem vínculo vigente com time; isso não amplia o acesso a colegas.

`TimeAnalysisEngine.SummarizeImportedDay` reutiliza o motor de análise; diferencia a consulta de dados armazenados da análise com leitura atual das fontes. Retorna durações em segundos, diferença Monday − VR, parcial e motivos de indisponibilidade. Não chama fontes externas nem cria relatórios/notificações.

- Ausência de lançamento Monday não comprova cobertura: total nulo, nunca zero presumido.
- Duração nula/negativa e cronômetro aberto impedem total Monday; não se prolonga timer importado até o instante da consulta.
- VR ausente, múltiplo ou inconsistente não produz diferença; batidas ímpares preservam o motivo do motor.
- No dia corrente, a jornada VR fica nula, sem estimar atividade após a importação. Datas futuras ficam parciais com totais nulos.
- Zero explicitamente válido permanece zero; diferença preserva segundos e sinal.
- Tolerância usa a configuração global atual. Não altera relatórios persistidos anteriormente.

Os totais representam dados importados e não comprovam cobertura completa, atualização ou saldo oficial. O consumidor deve informar essa limitação. Não use apenas um sucesso de sincronização da organização como prova de cobertura individual, pois a execução pode ter sido restrita ao escopo de outro usuário.

Ao modificar este contrato, verificar soma/deduplicação, sinais, ausência, nulos, timer aberto, dias parciais/futuros, filtro de fonte e acesso por organização/pessoa. Esta revisão documental não registra nova execução desses testes.
