# Contrato funcional — análises, configurações e notificações

Atualizado em 29/09/2026. Decisões aprovadas pelo responsável do produto nesta conversa.

Este aditivo prevalece sobre documentos anteriores nos assuntos abaixo. É um requisito de implementação, não uma declaração de API disponível. Não altera OpenAPI nem comprova publicação. A branch `codex/notification-schedules` possui uma proposta anterior de agendas por organização: deverá ser adaptada para configurações globais antes de integrar este desenho. Os demais requisitos do produto continuam válidos.

## 1. Regras aprovadas

- Configurações compartilhadas por todas as organizações, sem override por organização. Todo administrador autorizado a acessar a tela de configurações pode alterá-las; não restringir exclusivamente ao SystemAdmin. O servidor deve validar essa permissão. Membro e Líder não ganham administração por liderar um time.
- Tolerância diária inicial aprovada: **30 minutos nos dois sentidos**, configurável em minutos; diferença exatamente igual ao limite permanece dentro da tolerância. Horários e mensagens dos avisos ajustáveis. Administrador pode criar, editar e excluir agendamentos. Exclusão impede novos disparos e preserva histórico/auditoria.
- Horário de negócio e todas as fronteiras de datas em `America/Sao_Paulo`. Proposta técnica: persistir instantes em UTC, manter relógio do servidor sincronizado e resolver os períodos no servidor em São Paulo. Nunca usar o relógio/fuso do PC como autoridade.
- Semana começa segunda-feira. Sprint do mês vigente: dias 1 a 14 e dia 15 até o último dia do mês, incluindo fevereiro. Não há sobreposição no dia 15.
- Envio instantâneo: administrador seleciona um usuário ou todos do escopo autorizado, escreve mensagem e escolhe fechamento diário, semanal ou por sprint. Cada destinatário recebe sua própria análise junto da mensagem.
- Fechamento diário instantâneo: início do dia atual até o instante do envio. Semanal: segunda-feira atual até esse instante. Sprint: início da sprint atual até esse instante. No dia 22, analisar do dia 15 até o momento do envio no dia 22. “Fechamento” aqui não bloqueia registros nem torna um dia em andamento definitivo.
- Dados continuam isolados por organização. Configuração global não concede acesso global a pessoas. “Todos” é resolvido pelo servidor no escopo autorizado; SystemAdmin seleciona a organização para as operações organizacionais.

## 2. Processamentos e avisos iniciais

| Momento em São Paulo | Regra |
|---|---|
| Virada do dia | Gerar relatório de ocorrências do dia encerrado: quantidade ímpar de batidas VR e cronômetros Monday ainda em execução; também identificar diferenças acima da tolerância quando calculáveis. |
| 10h | Reavaliar somente o dia civil imediatamente anterior e avisar usuários que ainda tenham ajustes: batidas ímpares, cronômetros abertos ou diferença acima do limite. Não incluir pendências antigas nem substituir por último dia útil; segunda-feira considera domingo, não sexta-feira. |
| 11h50 | Comparar o dia até o instante de corte. Acima da tolerância, pedir ajuste; dentro dela, informar coerência até aquele momento e lembrar de pausar perto do almoço. |
| 17h | Mesma conferência parcial, com mensagem adequada ao fim do expediente. |

Não há jornadas após meia-noite: a virada do dia em São Paulo é a fronteira do fechamento diário, sem exceção de jornada noturna. Avisos automáticos agendados não são enviados aos sábados e domingos. Essa restrição não suspende o processamento diário de relatórios nem muda a regra já aprovada de recuperar notificações pendentes ao abrir o aplicativo. Não criar avisos de fim de semana para envio acumulado na segunda-feira. Feriados e eventual restrição ao envio manual ainda precisam de definição.

Durante o expediente, somar intervalos VR fechados e o intervalo desde a última entrada válida até o corte; no Monday incluir sessões abertas até o mesmo corte. Entrada aberta durante o expediente é esperada, não é automaticamente erro de batida ímpar. Não supor a direção de uma batida inválida/ambígua. Cronômetro aberto no expediente não equivale ao cronômetro que atravessou o fechamento.

Dados ausentes, importação incompleta ou fonte indisponível não viram zero nem resultado “horas corretas”. Exibir qualidade e atualização de cada fonte. Totais parciais são identificados como parciais. Correções são feitas nas fontes e reavaliadas sem apagar a ocorrência histórica.

## 3. Centralização obrigatória das análises

Implementar um único caso de uso de análise no backend, reutilizado por consultas, relatórios, fechamento noturno, avisos automáticos e envio instantâneo. React, WPF, controllers e jobs não implementam fórmulas próprias.

Separar responsabilidades:

1. Resolvedor de período e relógio injetável: determina início, fim exclusivo e instante de corte em São Paulo.
2. Adaptadores Monday/VR: importam e normalizam dados; não decidem tolerância ou mensagem.
3. Motor puro de análise no domínio: recebe intervalos normalizados, qualidade, corte e versão de regras; calcula totais, diferença e ocorrências.
4. Serviço de aplicação: autoriza o escopo, obtém os dados e configurações e chama o motor.
5. Persistência de relatórios e notificações: conserva resultado, referências, versão das regras e momento de geração. Reprocessamento cria revisão rastreável.
6. Agendador e entrega: chamam o mesmo serviço e distribuem o resultado individual. Não recalculam horas.

Saldo = Monday menos VR. Divergência absoluta do período = soma das magnitudes diárias; dias positivos e negativos não apagam problemas entre si. A tolerância é diária, não multiplicada ou aplicada apenas ao saldo semanal/sprint. Dias incompletos devem ser discriminados, não classificados como conciliados.

Capturar um único instante de corte e uma versão de configuração por execução, inclusive envio em lote. Armazenar intervalos como início inclusivo/fim exclusivo para evitar duplicação nas fronteiras. Versionar regras/configuração; uma alteração posterior não modifica silenciosamente uma notificação já emitida.

Proposta de persistência: schema `time_control`, tabelas `app_settings` e `notification_schedules` globais, sem `organization_id`. Tabelas de análises, destinatários, ocorrências, execuções e notificações mantêm organização e escopo. Usar migrations EF e auditoria, incluindo autoria, valores anteriores/novos e versão; não criar tabela genérica de strings sem validação. Não alterar manualmente schema em produção.

Execuções e envio precisam ser idempotentes: reinício, duas instâncias, retry ou clique repetido não duplicam a mesma ocorrência/entrega. Usar fila persistida/outbox e resultado por destinatário. “Enfileirada”, “entregue ao cliente” e “lida” são estados distintos; leitura não resolve o problema. A recuperação das notificações já geradas durante a indisponibilidade do PC está definida abaixo; recuperação de execuções não realizadas por indisponibilidade do servidor permanece pendente.

## 4. Informações necessárias no futuro contrato HTTP

Esta é uma lista de capacidades e dados requeridos, não rotas ou DTOs já implementados. Nomes definitivos serão definidos no backend e publicados no OpenAPI real antes da integração.

| Capacidade | Dados e comportamento necessários |
|---|---|
| Ler/alterar configurações globais | Tolerância diária em minutos, fuso, versão/controle de concorrência, última alteração e permissão efetiva. Validação no servidor. |
| Gerenciar agendas | Identificador, horário local, mensagem, finalidade da regra, ativo, versão; listar/criar/editar/excluir com auditoria. Mensagem não determina a lógica por interpretação de texto. |
| Consultar análise | Pessoa/escopo autorizado, período resolvido, corte, versão da regra, totais em segundos, diferença assinada, divergência absoluta, resultados diários, ocorrências e qualidade/atualização por fonte. Duração aceita mais de 24h; valor desconhecido permanece nulo. |
| Preparar/enviar aviso instantâneo | Um usuário ou todos do escopo, mensagem, modo diário/semanal/sprint, chave idempotente; devolver operação e resultados por destinatário. Revalidar autorização no envio; prévia deve informar período/corte e que o resultado pode ser reavaliado. |
| Consultar relatório de ocorrências | Dia/pessoa/tipo/status, evidências e revisões, com paginação e escopo autorizado. |
| Caixa individual | Notificações do usuário autenticado, paginação, não lidas, mensagem, análise associada, leitura e destino autorizado. Revalidar acesso no detalhe. |
| Histórico administrativo | Autor, horário, escopo/quantidade, resultado por destinatário e falhas, sem expor dados de outras organizações. |

Respostas de erro seguem ProblemDetails com code e correlationId. Autorização e controle de concorrência são responsabilidade do servidor, inclusive para configurações globais. Nenhum token de fonte externa chega ao frontend.

## 5. Telas solicitadas ao frontend

Implementar a especificação visual agora; integrar ações somente após contrato real disponível. Fixtures devem ficar em testes/prévia explicitamente identificada, nunca simulando operação real.

| Tela | Conteúdo e aceite |
|---|---|
| Configurações globais | Tolerância diária em minutos; fuso exibido; aviso de alcance global; autoria da última alteração; validação e conflito de edição concorrente. Acesso para administradores autorizados. |
| Agendamentos | Lista inicial 10h/11h50/17h; criar/editar horário e mensagem, ativar/desativar e excluir com confirmação. Mostrar finalidade da regra separada do texto. Não prometer disparo porque a agenda foi salva. |
| Enviar notificação agora | Usuário ou todos do escopo; mensagem; seletor Diário/Semanal/Sprint; período resolvido pelo servidor, prévia e quantidade de destinatários antes de confirmar. Exibir processamento, sucesso parcial e erros sem reenvio duplicado. |
| Central de notificações | Próprias notificações, não lidas/histórico, mensagem e análise individual anexada. Abrir detalhe e marcar leitura sem encerrar ocorrência. |
| Minha análise | Diário/semanal/sprint; totais VR/Monday, diferença, tolerância, qualidade e corte; detalhe por dia e registros. Dia atual explicitamente provisório. |
| Relatórios de erros | Visão administrativa/gestão no escopo, filtros por dia/pessoa/tipo; batidas ímpares, cronômetros atravessando fechamento e diferenças confirmadas; evidências e situação após reanálise. |
| Histórico de envios | Quem enviou, quando, período, destinatários autorizados e estado real de processamento/entrega/leitura. |

Desktop: implementar inicialização com Windows, bandeja, recepção autenticada e aviso nativo que abre a mesma notificação da central. Backend decide destinatários e análise. Atualizar allowlist do bridge apenas para rotas efetivamente publicadas, preservar DPAPI e não expor tokens ao React. A verificação das ações solicitadas no CEP Horas (`shutdown`, `restart`, `hibernate`) segue o [contrato de liberação de energia](../power-action-check.md); essa decisão não executa a ação no sistema operacional. Web e desktop consomem o mesmo resultado persistido para notificações.

Regra aprovada para PC desligado/offline: ao iniciar a sessão do Windows, o aplicativo inicia automaticamente, restaura a autenticação e busca **todas as notificações pendentes** do usuário, inclusive as geradas enquanto estava desligado. Não descartar lembretes por serem antigos. Se não houver conexão, retomar a busca quando ela voltar; se a sessão não puder ser restaurada, solicitar login e buscar após autenticar. Consumir todas as páginas, sem limitar a recuperação apenas à primeira página ou ao dia atual. Preservar horário original, período e análise anexada, identificando avisos atrasados; não apresentá-los como uma nova conferência das horas atuais. Receber/sincronizar não marca como lida. Deduplicar por identificador persistente para não reapresentar o mesmo aviso a cada reinício; notificações não lidas continuam na central. Confirmar recebimento somente após registro local bem-sucedido, sem perder pendências em caso de falha.

## 6. Decisões ainda abertas antes de ativar envios

- Tolerância já aprovada em 30 minutos, simétrica; comparação em segundos sem arredondar antes de aplicar o limite.
- Feriados, férias e escalas; confirmar se a restrição de fins de semana também impede envio manual. Sábado/domingo estão excluídos dos avisos automáticos e não existem jornadas após meia-noite.
- Eventual escalonamento ao líder. O aviso das 10h considera somente o dia civil anterior, sem pendências antigas.
- Política de recuperação de execuções não realizadas por indisponibilidade do servidor. A recuperação de todas as notificações já geradas enquanto o PC estava desligado está aprovada e não é mais uma dúvida.
- Mensagem quando fonte falha e prazo máximo de atualização para afirmar coerência. Não enviar afirmação de sucesso com dados incompletos.
- Como o administrador escolhe a finalidade de uma nova agenda e se haverá mensagem livre sem análise. Os três comportamentos iniciais e os três períodos de envio instantâneo já estão definidos.

Essas pendências não impedem preparar telas/contratos, mas não podem virar valores presumidos em produção.

## 7. Validação necessária na implementação

Cobrir fronteiras 14/15 e último dia/primeiro dia, fevereiro e ano bissexto, segunda-feira e troca de mês na semana; corte único para as duas fontes; intervalo aberto esperado no expediente e irregular no fechamento; diferença positiva/negativa e limite exato; cancelamento aparente entre dias; dados parciais; idempotência, concorrência, alteração/exclusão de agenda e recuperação após reinício; autorização dos administradores globais e isolamento dos destinatários. Homologar recepção web/Windows sem simular entrega.
