# Análises e notificações — implementação

Contrato funcional aprovado: [regras do produto](conciliacao-horas/contrato-analises-notificacoes.md). Este documento descreve a implementação desta branch, não a versão atualmente implantada na VM.

## Motor e armazenamento

`TimeAnalysisEngine` é o único motor de períodos/cálculos. São Paulo define o dia de negócio; instantes persistidos permanecem UTC. A infraestrutura lê as duas fontes com o mesmo corte, inclui cronômetros sobrepostos e armazena o resultado individual. Sem fonte completa não há afirmação de coerência. Semana começa segunda-feira; sprint 1–14 e 15–fim do mês, limitada ao instante do pedido. Valores usam segundos; tolerância global inicial de **30 minutos**, simétrica, com ocorrência apenas acima do limite. Saldos opostos não anulam a divergência absoluta.

A migration `GlobalTimeAnalysisNotifications` cria o schema `time_control` e tabelas `app_settings`, `notification_schedules`, `notification_dispatches`, `analysis_reports`, `notifications`. Configurações/agendas são globais; relatórios, envios e destinatários permanecem organizacionais. A migration concede ao papel runtime somente acesso aos dados, inclusive em instalações existentes.

Há agendas iniciais às 10h, 11h50 e 17h. O envio automático nasce **desativado** e pode ser habilitado na tela global após configurar e homologar as fontes. O worker funciona por padrão; `TimeNotifications:WorkerEnabled=false` desativa processamento em ambientes de teste. Sem worker os pedidos permanecem pendentes, não são entregues.

## API

Todas as rotas abaixo usam prefixo `/api/v1` e autenticação. O OpenAPI gerado pela aplicação é o contrato tipado de referência.

| Rotas | Acesso |
|---|---|
| `GET/PATCH /time-control/settings` | SystemAdmin e OrganizationAdmin; configuração global, sem organização selecionada. PATCH exige versão atual. |
| `GET/POST /time-control/notification-schedules` | Mesmos administradores globais. |
| `PATCH/DELETE /time-control/notification-schedules/{id}` | Versão obrigatória (query no DELETE); exclusão lógica/auditoria. Horário com precisão de minuto; sem horários duplicados. |
| `GET /organization/time-control/notification-dispatches/preview` | Administrador no escopo; devolve período, corte e quantidade elegível, sem enviar. |
| `POST /organization/time-control/notification-dispatches` | Administrador no escopo; mensagem, `userId` ou null para todos, período daily/weekly/sprint, `requestId`. HTTP 202 significa fila persistida. |
| `GET /organization/time-control/notification-dispatches` | Histórico paginado do escopo, estado, quantidade e falha de fonte. |
| `GET /organization/time-control/analyses` | Relatórios persistidos com escopo atual de pessoa/time/organização; filtros de pessoa, período, dia e ocorrência. Não gera uma nova consulta externa. |
| `GET /me/notifications` | Caixa individual paginada; filtros unreadOnly e pendingOnly. Pendente para entrega = ainda não entregue e não lida. |
| `POST /me/notifications/received` | Confirma recebimento de até 100 IDs próprios; não marca leitura. |
| `POST /me/notifications/{id}/read` | Marca somente notificação própria como lida; não resolve ocorrência. |

SystemAdmin passa `organizationId` nas rotas organizacionais. “Todos” significa contas ativas associadas a identidades na organização autorizada. Membros não enviam avisos; consultam a própria caixa e os relatórios permitidos. Alterações globais, exclusões e pedidos administrativos têm auditoria.

## Processamento e proteção contra excesso

- Worker a cada 30 segundos, com exclusão mútua PostgreSQL entre instâncias.
- Relatório do dia anterior gerado uma vez por dia, inclusive fim de semana, sem popup. Não existem jornadas após meia-noite.
- Agendas disparam somente de segunda a sexta. Às 10h, só problemas confirmados do dia civil anterior; na segunda-feira não volta à sexta-feira. Falha de fonte sozinha não vira cobrança às 10h.
- Em cada execução, cada pessoa recebe no máximo uma notificação consolidada. Resultado e notificação são persistidos atomicamente. Retentativas de processamento não duplicam registros.
- Envio manual com mesma chave e conteúdo devolve o pedido existente; reutilização com conteúdo diferente gera conflito. Há intervalo mínimo de um minuto entre pedidos manuais da mesma organização, além do rate limiting por conta.
- Excluir/desativar agenda impede novas execuções; histórico já emitido é preservado. O processamento revalida habilitação e acesso antes de gerar resultados.
- Cliente Windows busca todas as páginas pendentes ao iniciar/reconectar, mantém recibos por conta e agrupa o atraso em um único aviso. Central preserva não lidas; ler e receber são operações distintas.

## Limites explícitos

A análise ao vivo também atende a [verificação de ações de energia](power-action-check.md). As duas rotas pessoais reutilizam `FreshTimeAnalysisService` e `TimeAnalysisEngine`, sem gerar relatórios ou notificações.

- O agendador recupera horários devidos do mesmo dia civil após consulta lenta ou indisponibilidade, preservando o corte original; cliente identifica atraso e agrupa popups. Habilitação/alteração da configuração global não recria horários anteriores à alteração. Execuções de dias anteriores que nunca foram enfileiradas não são reconstruídas. Notificações já geradas no servidor continuam pendentes e são recuperadas integralmente, independentemente do dia.
- Não há calendário de feriados/férias aprovado. A restrição implementada é sábado/domingo para agendas. Envios manuais continuam disponíveis.
- Dias sem dados VR não viram zero. Sem calendário de dias dispensados, uma semana/sprint com dias incompletos pode não ter saldo agregado conclusivo; os valores e ocorrências diários continuam visíveis.
- Relatórios são snapshots imutáveis, consultados por data de geração; não há workflow de justificativa/aprovação nesta entrega. Consulta da lista não atualiza fontes. Novos envios e execuções produzem nova análise.
- Não foi implementado bloqueio de desligamento, assinatura do instalador ou atualização MSIX nesta entrega.

## Publicação

Validar build, unidades, integração PostgreSQL, migration sem alteração de modelo pendente e stack HTTPS com papel runtime limitado. A migration deve ser aplicada explicitamente com backup validado antes da API atualizada. Integrar backend antes do frontend que consome essas rotas. Não habilitar automaticamente fontes externas nem publicar seus tokens no cliente. Após implantação, homologar um envio para conta de teste, recuperação offline, deduplicação e só então habilitar agendas globais.
