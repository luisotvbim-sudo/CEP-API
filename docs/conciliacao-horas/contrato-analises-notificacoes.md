# Contrato funcional — análises e notificações

Regras aprovadas em 29/09/2026, consolidadas em 04/10/2026. A [especificação funcional](especificacao-funcional.md) preserva RN/RF/CA/D e a visão futura; [implementação HTTP e worker](../time-notifications.md) descreve os contratos existentes. Este arquivo conserva decisões funcionais sem transformar o texto de proposta antigo em estado atual.

## Períodos e configuração

- API é responsável pelo cálculo, período, tolerância e destinatários; React/WPF/jobs não mantêm fórmulas próprias.
- Dia civil e corte em `America/Sao_Paulo`. Semana começa segunda e sprint inicia dia 1 ou 15 até o corte atual. Não há jornada após meia-noite no contrato aprovado.
- Instantes persistidos em UTC, intervalos início inclusivo/fim exclusivo e um corte único para as duas fontes e todo o lote.
- Diferença = Monday − VR; divergência absoluta do período soma magnitudes diárias. Sinais opostos não apagam diferenças entre dias.
- Tolerância diária global inicial de 30 minutos, simétrica, comparada em segundos antes de arredondamento. Limite exato é permitido; diferença continua visível.
- Configurações/agendas globais versionadas e auditadas. Pessoas, relatórios, envios e notificações mantêm organização e escopo; “todos” é resolvido pelo servidor dentro da organização autorizada.
- Duração desconhecida fica nula; fonte falha ou incompleta não é zero nem resultado coerente. Dados atuais/provisórios e importados devem ser identificados.

## Processamentos iniciais

| Momento em São Paulo | Regra aprovada |
|---|---|
| Processamento diário | Relatório do dia civil anterior, inclusive fim de semana, sem popup; identificar batidas VR ímpares, timer Monday atravessando fechamento e diferenças calculáveis acima do limite |
| 10h | Reavaliar somente ontem civil e avisar quem ainda tiver problema confirmado; segunda considera domingo, não sexta e não pendências antigas |
| 11h50 | Comparar hoje até o corte, orientar ajuste se acima do limite ou informar coerência parcial e lembrar pausa próxima ao almoço |
| 17h | Mesma comparação parcial, com mensagem de fim de expediente |
| Envio instantâneo | Administrador seleciona uma pessoa ou todos do escopo, mensagem e período diário/semanal/sprint; prévia e confirmação sem duplicar pedido |

Avisos automáticos não são enviados aos sábados/domingo nem acumulados para segunda. Essa restrição não suspende relatórios diários nem recuperação das notificações já geradas. Feriados, escalas, ausências e eventual restrição ao envio manual seguem pendentes.

Durante expediente, somar intervalos VR fechados válidos e entrada aberta até corte; Monday aberto vai até o mesmo corte. Entrada aberta esperada durante expediente não é batida ímpar de dia encerrado. Não inferir direção de batida inválida. Timer que atravessa fechamento não equivale a timer normal aberto hoje.

## Responsabilidades e persistência

O motor puro de domínio recebe registros normalizados/qualidade/corte/tolerância; serviço autoriza e obtém fontes/configuração; persistência conserva relatório e versão; agendador executa o mesmo caminho e entrega resultado individual. Adaptadores não definem tolerância ou mensagens. Unknown permanece nulo e não pode ser certificado pela interface.

Relatórios são snapshots. Nova execução pode produzir nova análise; consultar snapshot não altera fontes ou resultado antigo. A implementação atual exige todos os valores diários de um total para agregar esse total, sem subtotal certificado dos dias calculáveis. A política de sessões Monday distintas simultâneas segue explicitada na RN-03; o motor as soma após deduplicar mesma chave externa.

Pedidos/execuções/entrega são idempotentes; resultado e notificação por pessoa são persistidos atomicamente. “Enfileirada”, “recebida pelo cliente” e “lida” são estados distintos. Leitura não resolve ocorrência nem altera as horas originais. Alterações de regra não mudam silenciosamente avisos emitidos.

## Recuperação do cliente

Ao iniciar Windows/reconectar, o aplicativo restaura autenticação e busca **todas as páginas de notificações pendentes**, inclusive as geradas enquanto PC estava desligado. Sem sessão válida, solicita login e busca depois. Não descartar avisos apenas pela idade.

Preservar data/período/análise originais, identificar atraso e agrupar popups; não apresentar atraso como conferência atual. Deduplicar por ID persistido por conta e API. Confirmar recebimento após gravação local bem-sucedida. Receber/sincronizar não marca lida; não lidas continuam na central. Tokens permanecem no host nativo com DPAPI e não chegam ao React.

O agendador do servidor recupera slots do mesmo dia com corte original; dias passados nunca enfileirados não são reconstruídos. Notificações já geradas continuam recuperáveis independentemente do dia. Habilitar/alterar configuração não recria slots anteriores à mudança.

## Integração e aceite

Utilizar rotas/DTOs publicados em [OpenAPI](../openapi-current.json), não nomes imaginados por este texto. Configuração, versões, autorização e isolamento são revalidados no servidor. Fixtures só em prévia/testes identificados. HTTP 202 confirma pedido persistido, não recebimento; salvar agenda não comprova disparo.

Verificar fronteiras de semana/sprint/mês/ano, corte único, sinais, limite exato, lacunas, batidas abertas/fechadas, timer atravessando dia, soma de sessões, idempotência/concorrência, mudança/exclusão de agenda, acesso revogado, reconexão e todas as páginas pendentes. Homologação real de fontes e recepção Windows exige prova própria, além de CI.

Calendário completo, workflow de justificativas/casos, prazo máximo de atualidade, recuperação de execução em dia anterior, escalonamento e mensagem livre sem análise seguem em [decisões e pendências](../DECISOES-PENDENCIAS.md). Não ampliar escopo ou habilitar agendas por supor essas regras.
