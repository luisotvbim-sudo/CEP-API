# Conciliação de horas — especificação funcional do produto

Versão 1.0 · 13/09/2026 · Documento para o arquiteto de software

**Status: proposta de produto, sujeita à validação das regras indicadas.** Este documento consolida a definição da aplicação e os prompts para orientar sua construção. Não comprova funcionalidades implementadas nem acesso disponível às integrações. O [manual de uso proposto](manual.html) descreve a experiência esperada com dados fictícios.

## 1. Objetivo e contexto

Criar uma central de conciliação que compare as horas registradas em atividades do Monday com a jornada registrada no Ponto VR Mais. A aplicação atenderá aproximadamente 50 funcionários, seus gestores e administradores. O resultado deve permitir identificar divergências, entender sua origem, solicitar esclarecimentos, acompanhar ajustes e emitir relatórios.

O funcionário precisa responder: “Minhas horas estão coerentes? Qual dia precisa de atenção? O que devo fazer?”. O gestor precisa responder: “Quais pessoas e períodos precisam de análise? Qual é a dimensão das diferenças? Quem está com a próxima ação?”. O administrador precisa garantir que pessoas, fontes e regras estejam corretamente configuradas.

A interface será uma página React incorporada à aplicação desktop por WebView2. Esta é uma restrição informada pelo solicitante. A arquitetura, os mecanismos de integração, a persistência, a autenticação e os componentes de implementação serão definidos pelo arquiteto.

O produto compara registros; uma diferença não comprova ausência, baixa produtividade ou irregularidade. Reuniões, treinamento, atividades internas, atrasos de lançamento e falhas de importação podem explicar diferenças. O contexto deve acompanhar os números.

### 1.1 Resultado esperado

- O gestor identifica os casos prioritários em uma única visão.
- O funcionário entende cada diferença sem precisar reconstruir manualmente dois relatórios.
- Cada pendência informa motivo, responsável, prazo, próximo passo e histórico.
- Todos os números podem ser rastreados até os registros que os compõem.
- Dados incompletos são destacados antes de qualquer conclusão.

### 1.2 Limites do escopo

- A proposta inicial consulta e concilia dados das fontes. Correções de batidas e lançamentos são realizadas na origem; após atualização, a aplicação reavalia o resultado.
- Registrar uma justificativa ou uma decisão não altera as horas originais.
- Jornada prevista é uma referência adicional, separada da comparação Monday × ponto.
- Banco de horas e horas extras oficiais, quando disponíveis, são apresentados como contexto. Sua apuração formal e o fechamento de folha não estão definidos neste produto.
- Leitura offline, gravação nas fontes, notificações externas e aplicativo móvel próprio não são compromissos do MVP.
- Os papéis de funcionário, gestor e administrador são papéis de negócio propostos; sua correspondência com a aplicação desktop existente precisa ser definida.

## 2. Perfis e permissões

Uma pessoa pode ser funcionário e gestor. Nesse caso, alterna entre “Minha jornada” e “Gestão da equipe”. O perfil ativo e o escopo de dados devem ficar visíveis. A troca de perfil revalida filtros e seleção de pessoas.

| Capacidade | Funcionário | Gestor | Administrador |
|---|---|---|---|
| Consultar jornada e atividades | Somente próprias | Equipes autorizadas e próprios dados | Apenas se também tiver permissão de consulta |
| Consultar ranking de divergências | Não | Equipes autorizadas | Apenas se também tiver papel gerencial |
| Enviar justificativa e responder | Próprios casos | Próprios casos como funcionário | Próprios casos como funcionário |
| Solicitar ajuste ou justificativa | Não | Equipes autorizadas | Apenas com permissão gerencial |
| Aprovar, devolver e reabrir caso | Não | Equipes autorizadas; sem aprovar o próprio caso | Conforme delegação explícita |
| Exportar relatório | Próprios dados | Equipes autorizadas | Conforme permissão de consulta |
| Enviar notificação de cobrança | Não | Pessoas autorizadas | Avisos administrativos autorizados |
| Gerenciar equipes e vínculos | Não | Consultar composição autorizada | Sim |
| Configurar regras e acompanhar falhas | Ver disponibilidade de seus dados | Ver disponibilidade de sua equipe | Sim |
| Consultar auditoria | Histórico de seus casos | Histórico dos casos autorizados | Histórico administrativo autorizado |

A desativação encerra novos acessos e preserva o histórico conforme a política definida pela organização. Mudanças de equipe e de gestor precisam manter datas de vigência. Quem pode consultar períodos anteriores à transferência é uma decisão pendente, registrada em D-05.

## 3. Conceitos e informações do negócio

| Conceito | Significado e informações necessárias |
|---|---|
| Funcionário | Identidade interna, nome, situação, equipe, gestor e vigência do vínculo |
| Correspondência entre fontes | Identificação inequívoca da pessoa no Monday e no Ponto VR Mais; situação e histórico da associação |
| Registro de ponto | Referência de origem, pessoa, data/jornada, batidas ou total consolidado disponível, intervalos, ajustes e estado de completude |
| Lançamento Monday | Referência de origem, responsável pelo tempo, atividade, projeto/board/grupo quando disponíveis, data efetiva, duração e alterações |
| Jornada prevista | Referência de horas esperadas e calendário aplicável, com vigência |
| Conciliação diária | Data/jornada, totais das fontes, diferença, regra utilizada, qualidade dos dados e resultado numérico |
| Divergência | Ocorrência vinculada à pessoa e à jornada; motivo, gravidade, situação do tratamento e responsável |
| Justificativa | Autor, motivo, comentário, data e decisão; anexos opcionais em etapa posterior |
| Notificação | Destinatário, contexto, mensagem, criação, leitura e ligação com a pendência |
| Sincronização | Fonte, tentativa, conclusão, cobertura, registros processados, ignorados, incompletos e falhas |
| Regra | Tolerâncias, atividades elegíveis, calendário e data de vigência |
| Relatório | Escopo, filtros, colunas, data de referência dos dados, geração e autoria |

Um lançamento com vários participantes não implica que toda a duração pertença a cada pessoa. A regra de atribuição precisa estar explícita. Duas importações do mesmo registro não podem duplicar horas.

## 4. Regras de conciliação

### RN-01 — Unidade, data e duração

Exibir durações em horas e minutos: `07h30`, `32h15`, `160h00`. Totais podem ultrapassar 24 horas. Não confundir `07h30` com `7,30 horas`. Se uma exportação oferecer horas decimais, identificar a unidade e a conversão.

Comparar a mesma pessoa, jornada e período nas duas fontes. Usar a data efetiva do trabalho, não a data em que o lançamento foi criado. O fuso de referência, o tratamento de jornadas que cruzam meia-noite e a distribuição de lançamentos que abrangem vários dias dependem de D-02.

### RN-02 — Total de ponto

O total válido representa o tempo de jornada apurado, com os intervalos aplicáveis descontados uma única vez. Caso a fonte forneça total já líquido, não descontar novamente. Preservar a informação de ajustes e a procedência do total. Batida ausente ou sequência inconsistente resulta em dados incompletos, salvo se houver total oficial válido e regra aprovada para utilizá-lo.

### RN-03 — Total Monday

Somar apenas os lançamentos elegíveis atribuídos à pessoa no dia. A configuração define boards, atividades e categorias incluídos ou excluídos. Atividade compartilhada, cronômetro em andamento, duração inválida, sobreposição e registro sem data/pessoa suficientes precisam ser identificados. Não inventar horas nem duplicar lançamentos para preencher lacunas.

### RN-04 — Convenção da diferença

`Diferença diária = horas Monday − horas de ponto`.

- Negativa: menos horas no Monday do que no ponto.
- Positiva: mais horas no Monday do que no ponto.
- Zero: totais iguais, desde que os dados estejam completos e a jornada seja elegível.

Sempre mostrar uma descrição junto ao sinal: “01h30 a menos no Monday”. Não usar “débito” ou “hora extra” como sinônimos automáticos dessa diferença.

### RN-05 — Tolerâncias

Exemplo inicial a validar: até 10 minutos de diferença absoluta = conciliado dentro da tolerância; de 11 a 30 = divergência leve; acima de 30 = divergência crítica. A tolerância usa a magnitude, independentemente do sinal.

Diferença dentro da tolerância continua visível e não vira zero. Limites, precisão, arredondamento e eventual variação por equipe/jornada precisam ser aprovados em D-01. Aplicar a regra vigente na data da análise e registrar a versão; reprocessar períodos anteriores exige indicar o alcance e conservar o histórico.

### RN-06 — Zero não significa ausência de dados

Uma fonte consultada com sucesso e sem lançamento não é o mesmo que uma fonte indisponível. Distinguir:

- Sem lançamento no Monday: consulta completa, nenhum lançamento encontrado para jornada elegível.
- Sem registro de ponto: consulta completa, nenhum registro encontrado para jornada elegível.
- Sem registros nas duas fontes: ambas consultadas, nenhum registro; considerar jornada e exceções antes de abrir caso.
- Dados incompletos: importação parcial, batida faltante, cadastro não associado ou registro inválido.
- Não aplicável: jornada excluída da comparação por regra explícita.
- Em andamento: dia ou prazo de lançamento ainda não encerrado; valores provisórios.

Não substituir dados desconhecidos por zero. Lacunas impedem um resultado conclusivo; pendências de qualidade ficam separadas das divergências numéricas confirmadas.

### RN-07 — Calendário e exceções

Considerar feriados, férias, afastamentos, folgas, finais de semana, escalas e dias dispensados de apontamento. Um fim de semana não é automaticamente uma folga. Identificar registros inesperados em dias dispensados e permitir análise. Exceções aprovadas devem ter motivo, vigência e responsável.

Atividades internas, reuniões e treinamentos precisam de regra: entram no Monday, entram como categoria elegível ou justificam uma diferença? A aplicação deve exibir a política adotada. Não presumir que todo minuto de ponto necessariamente precisa aparecer em uma tarefa de projeto.

### RN-08 — Três dimensões de status

Não misturar números, qualidade e atendimento em uma única etiqueta.

| Dimensão | Valores propostos |
|---|---|
| Qualidade/aplicabilidade | Completo, em andamento, incompleto, desatualizado, não associado, não aplicável |
| Resultado numérico | Conciliado, divergência leve, divergência crítica; indisponível quando não calculável |
| Tratamento | Sem pendência, identificada, aguardando funcionário, aguardando gestor, resolvida por correção, encerrada com justificativa, reaberta |

“Sem lançamento no Monday”, “sem registro de ponto” e “sem registros nas duas fontes” são motivos visíveis. “Aguardando justificativa” corresponde a aguardando funcionário; “aguardando análise” corresponde a aguardando gestor. “Resolvido” é um agrupamento do tratamento, nunca uma alteração dos dados originais.

Precedência visual: falta de associação ou informação impede conclusão; não aplicável é separado; dados provisórios/desatualizados exibem ressalva; depois vêm resultado numérico e tratamento. Um caso pode aparecer como “Crítica · Encerrada com justificativa”.

### RN-09 — Métricas do período

| Métrica | Definição |
|---|---|
| Saldo do período | Soma das diferenças diárias assinadas dos dias calculáveis |
| Divergência acumulada absoluta | Soma do valor absoluto de cada diferença diária calculável |
| Dias divergentes | Dias elegíveis, completos e encerrados, fora da tolerância |
| Taxa de conciliação | Dias conciliados ÷ dias elegíveis, completos e encerrados × 100 |
| Cobertura dos dados | Dias elegíveis, completos e encerrados ÷ dias elegíveis encerrados esperados × 100 |
| Percentual diário da diferença | Diferença diária ÷ horas de ponto do dia × 100, somente se ponto > 0 |
| Percentual de divergência do período | Soma das diferenças absolutas ÷ soma do ponto nos mesmos dias calculáveis × 100, somente se o denominador > 0 |
| Pendências de tratamento | Casos ainda abertos, separados por responsável e vencimento |

Se não houver denominador válido, mostrar “Não calculável”, nunca 0%. Justificativas aceitas não elevam a taxa de conciliação numérica; aparecem na taxa de resolução, se exibida.

O indicador anteriormente descrito como “percentual de horas conciliadas” fica substituído no MVP por **taxa de dias conciliados**, com fórmula explícita. Uma métrica específica por horas depende de definição adicional. Sempre mostrar cobertura e base da amostra junto à taxa.

Os totais gerais de cada fonte podem incluir datas ainda não comparáveis. Por isso, mostrar separadamente totais recebidos e totais da base comparável quando houver lacunas. Não subtrair conjuntos diferentes e chamar o resultado de divergência conciliada.

### RN-10 — Ranking e curvas

O ranking padrão usa divergência acumulada absoluta, em ordem decrescente, sobre os dias completos e encerrados. Permitir ordenar também por dias divergentes, pendências vencidas e percentual. Mostrar cobertura e quantidade de dias analisados para contextualizar cada pessoa.

Exemplo: uma pessoa tem −02h00 em um dia e +02h00 no outro. O saldo é zero, mas a divergência acumulada é 04h00. O ranking precisa destacar esse caso.

No gráfico comparativo, mostrar duas curvas: ponto e Monday. O eixo horizontal representa dias; o vertical representa horas. Ao apontar ou focar uma data, mostrar ambos os valores e a diferença. Lacunas não viram zero e não são conectadas como se houvesse medição. Curvas acumuladas são uma visão opcional; não substituem a análise diária.

Evitar 50 curvas sobrepostas. Usar ranking de barras e abrir a comparação da pessoa selecionada; comparações entre poucas pessoas devem ter legenda e seleção explícita. Esses gráficos representam coerência dos registros, não produtividade.

### RN-11 — Atualizações e histórico

Mostrar a última atualização bem-sucedida de cada fonte e a cobertura por período. Uma tentativa recente que falhou não torna os dados atuais. Preservar o último resultado conhecido com aviso de desatualização quando adequado.

Após alteração na origem, reavaliar os dias afetados, preservar justificativas e registrar o antes/depois. Se a diferença desaparecer, encerrar por correção com evento no histórico. Se um caso encerrado mudar materialmente, reabrir ou sinalizar nova análise conforme D-07. Reimportar dados idênticos não duplica horas, casos ou notificações.

### RN-12 — Dia atual, fechamento e prazos

Hoje e períodos ainda dentro do prazo de lançamento aparecem como provisórios. O MVP pode exibir esses dados, mas cobranças automáticas de atraso só podem usar um prazo aprovado. Fechamento mensal, bloqueio de período e reabertura de competência são evoluções sujeitas a D-07; não devem ser simulados como já existentes.

## 5. Navegação e experiência visual

### 5.1 Direção de design

Inspirar-se na organização do Monday: barra lateral enxuta, cards de indicadores, tabelas semelhantes a boards, faixas ou etiquetas de status, filtros em chips, cantos arredondados e fundo claro. A identidade deve ser própria, com paleta laranja e cinza: laranja nos botões, links e destaques; cinza na navegação, nos textos e nas superfícies neutras. Usar poucos destaques visuais e espaço suficiente para leitura.

Usar cinza para conciliação e situações neutras, laranja claro para atenção e laranja escuro para divergência crítica. Análise pendente e ausência de aplicabilidade usam etiquetas cinza com rótulos distintos. Cor vem sempre acompanhada de texto ou ícone. Nos gráficos, ponto usa linha cinza contínua e Monday, linha laranja tracejada. Dados fictícios de demonstração precisam de identificação explícita.

### 5.2 Estrutura da janela

- Gestor: Visão geral, Minha equipe, Divergências, Relatórios e Notificações. Situação dos dados fica acessível no cabeçalho; área completa de sincronização depende da permissão.
- Funcionário: Meu resumo, Meu calendário, Minhas pendências e Notificações.
- Administrador: Usuários e equipes, Correspondências, Regras e calendário, Sincronização e Auditoria; demais menus dependem de papéis adicionais.
- Cabeçalho: contexto/perfil ativo, período, atualização de cada fonte e acesso às notificações.
- Detalhes: abrir preferencialmente em painel lateral ou página com retorno que preserve filtros e posição da lista.

### 5.3 Comportamentos comuns

- Funcionar ao redimensionar a janela desktop; em janelas menores, recolher a navegação e priorizar os dados essenciais.
- Oferecer navegação por teclado, foco visível, rótulos claros e valores acessíveis dos gráficos.
- Evitar janelas e modais sucessivos. Ao fechar um formulário com resposta não enviada, oferecer continuar ou descartar.
- Preservar filtros no retorno e informar resultados e seleção atual. Se o filtro mudar, limpar seleção de destinatários para evitar envio fora do novo contexto.
- Diferenciar carregamento inicial, atualização em andamento, nenhum registro, nenhum resultado dos filtros, acesso restrito e falha.
- Exibir mensagens com próximo passo: “Não foi possível atualizar o ponto. Últimos dados válidos: ... Tentar novamente”.
- Ações rápidas para pesquisar, atualizar e exportar; atalhos devem estar visíveis na ajuda e não conflitar com a aplicação desktop.
- Definir com o desktop como abrir atividades externas, baixar arquivos, imprimir e encerrar sessão. O manual não presume esse comportamento já entregue.

## 6. Requisitos funcionais e critérios de aceite

### RF-01 — Entrada e contexto de acesso · MVP

Tela de entrada com nome/logo do produto, identificação do usuário, empresa/contexto quando aplicável e mensagens de erro. Permanecer conectado depende da política de acesso. Se o desktop já autenticar o usuário, a forma de entrada pode ser integrada; a experiência precisa ser definida.

**Aceite:** um funcionário vê apenas seus dados; um gestor só vê pessoas autorizadas; alternar para a visão pessoal não mantém uma seleção da equipe; sessão expirada oferece nova entrada preservando o contexto quando possível; o gestor não aprova sua própria justificativa.

### RF-02 — Correspondência de pessoas · MVP

Administrador associa identidades das fontes à pessoa interna. Mostrar “associado”, “sem correspondência”, “duplicado”, “ambíguo” e “inativo”. Pesquisar e revisar associações, visualizar origem e histórico, impedir correspondências conflitantes sem resolução explícita.

**Aceite:** pessoas com nomes iguais não são unidas automaticamente apenas pelo nome; registros sem associação ficam em fila de qualidade; após associação correta, os dias afetados podem ser reavaliados; troca de vínculo conserva autoria e histórico.

### RF-03 — Visão geral do gestor · MVP

Cards: funcionários no escopo, totais comparáveis de ponto e Monday, saldo, divergência acumulada absoluta, taxa de dias conciliados, cobertura, dias/casos críticos, pessoas sem lançamento e pendências aguardando gestor. Identificar a unidade de cada contagem.

Gráficos: comparação diária, barras das maiores divergências, distribuição por motivo e evolução da conciliação. Ranking mostra pessoa, ponto, Monday, saldo, divergência absoluta, percentual, dias afetados, cobertura e pendências. Comparação entre equipes e tendências avançadas entram na segunda etapa.

**Aceite:** todos os cards obedecem ao mesmo período e escopo; clicar no ranking abre a pessoa com o período preservado; saldo zero não oculta dias opostos; registros incompletos aparecem em aviso separado; gráfico e tabela oferecem os mesmos valores.

### RF-04 — Minha equipe · MVP

Tabela com funcionário, equipe, ponto, Monday, saldo, divergência absoluta, dias divergentes, pendências e situação. Busca por nome, ordenação e abertura do detalhe. Seleção múltipla para exportação; cobrança em lote fica na segunda etapa.

**Aceite:** maior divergência absoluta aparece primeiro por padrão; informar “X pessoas encontradas”; identificar seleção da página versus todos os resultados; não incluir pessoas fora do escopo em uma exportação; não perder filtros ao voltar do detalhe.

### RF-05 — Detalhe da pessoa e do dia · MVP

Resumo da pessoa com jornada prevista, totais, cobertura, pendências, gráfico diário e histórico. Tabela por dia: data, previsto, ponto, Monday, diferença, qualidade, resultado e tratamento.

Abrir um dia mostra batidas/intervalos disponíveis, ajustes, total líquido, atividades e duração por atividade, projeto/board/grupo quando disponíveis, itens excluídos do cálculo e motivo, regra aplicada, justificativas, decisões e próximos passos. Oferecer abrir o registro de origem se houver referência utilizável.

**Aceite:** a soma das parcelas elegíveis explica o total; exclusões ficam visíveis; campos não fornecidos pela origem aparecem como indisponíveis; sinal e descrição da diferença concordam; o usuário sabe qual regra classificou o dia.

### RF-06 — Área do funcionário · MVP

Meu resumo apresenta totais pessoais, diferença, dias conciliados, cobertura e pedidos do gestor. Meu calendário mostra situação de cada dia, legenda e seleção do período. Minhas pendências prioriza solicitações com prazo e casos que exigem resposta.

**Aceite:** clicar no calendário abre a mesma conciliação da lista; dias sem jornada e dias sem dados têm símbolos/textos distintos; o funcionário envia uma resposta e acompanha a decisão; o resultado numérico permanece após justificativa aceita; nenhum ranking de colegas fica disponível.

### RF-07 — Central de divergências e justificativas · MVP

Fila com motivo, gravidade, pessoa, data, diferença, responsável atual, prazo e tratamento. Gestor solicita justificativa ou correção; funcionário responde; gestor aceita, devolve para complemento ou encerra conforme regra. Comentários ficam no contexto do caso. Reabertura exige motivo. Anexos entram na segunda etapa.

**Aceite:** toda mudança de tratamento tem autor e data; pedido informa o que precisa ser esclarecido; devolução inclui motivo e próximo passo; resposta enviada aparece para ambos; aceitar justificativa não edita fontes; duplicar clique não cria respostas repetidas.

### RF-08 — Filtros e pesquisa · MVP / segunda etapa

MVP: período predefinido e personalizado, funcionário, equipe, gestor quando aplicável, qualidade, motivo, gravidade, tratamento, direção/faixa da diferença, presença de justificativa, responsável e prazo. Oferecer hoje, semana, mês atual, mês anterior e intervalo livre. Explicar que hoje pode estar provisório.

Combinar critérios com “E”; múltiplas escolhas dentro de um mesmo filtro com “OU”. “Limpar filtros” volta ao padrão informado. Chips mostram critérios ativos. Favoritos e compartilhamento de visualizações ficam na segunda etapa, respeitando permissões de quem abre.

**Aceite:** datas inicial e final são inclusivas e validadas; intervalo invertido tem orientação clara; nome não encontrado oferece limpar pesquisa; chips, gráfico, tabela, cards e exportação usam o mesmo contexto; filtro preservado não restaura acesso a equipe não autorizada.

### RF-09 — Notificações internas · MVP / segunda etapa

MVP: mensagens individuais do gestor, pedido de justificativa/ajuste, resposta enviada, decisão e acesso direto ao caso; central com não lidas, histórico, filtro por tipo, marcar como lida e marcar todas como lidas. Erros de integração são enviados aos responsáveis administrativos; demais usuários veem aviso sobre seus dados.

Segunda etapa: envio em lote por seleção/equipe, prazos configuráveis, lembretes automáticos, proximidade do vencimento, atraso, preferências e aviso de relatório pronto quando a geração for demorada. Mudança material de dados pode gerar aviso; cada sincronização bem-sucedida não precisa notificar todos.

**Aceite:** mostrar destinatário e contexto antes do envio; marcar como lida não equivale a responder; a mesma ocorrência não gera notificações duplicadas; lembretes cessam quando a pendência encerra; o vínculo só abre conteúdo ainda autorizado; envio em lote informa sucessos e falhas sem reenviar aos destinatários já atendidos.

Exemplo: “Olá, Ana. Em 08/09 há 01h30 a menos no Monday em relação ao ponto. Confira as atividades e envie uma justificativa ou ajuste o lançamento até o prazo indicado.”

### RF-10 — Relatórios e exportação · MVP / segunda etapa

MVP: resumo do período, conciliação por pessoa/equipe, divergências detalhadas, registros ausentes e pendências sem resposta, exportados em planilha. Funcionário exporta seus próprios dados. Gestor exporta escopo autorizado, filtros atuais ou seleção explícita.

Segunda etapa: PDF executivo, impressão formatada, justificativas/decisões, evolução mensal, comparação entre períodos e visão de horas extras quando a origem fornecer essa classificação. Não rotular diferença Monday × ponto como hora extra.

Prévia informa tipo, período, filtros, pessoas/linhas, colunas, ordenação, agrupamento e totais. Arquivo inclui geração, autoria, fontes, atualização/cobertura, regras, unidades e descrição dos sinais. Nome sugerido: `conciliacao-equipe-2026-09-01-a-2026-09-30.xlsx`.

**Aceite:** exportar mantém filtros, permissões e ordenação escolhidos; avisar se dados mudaram desde a prévia; campos indisponíveis não viram zero; preservar acentos e durações acima de 24h; sem resultados, explicar e permitir revisar filtros; falha oferece tentar novamente; informar sucesso e destino/ação de abertura conforme comportamento definido para o desktop.

### RF-11 — Administração · MVP / segunda etapa

MVP: usuários ativos/inativos, equipes, responsáveis, correspondência entre fontes, escopo de acesso, tolerância inicial, fontes elegíveis, calendário básico e exceções. Registrar vigência das regras e alterações.

Segunda etapa: jornadas avançadas e múltiplas escalas, prazos por contexto, regras por equipe, delegações temporárias, anexos e políticas avançadas de fechamento. Cadastro básico de jornada e exceções é necessário desde o MVP para evitar falsos alertas.

**Aceite:** validar limites de tolerância e datas de exceções; avisar o alcance de mudança de regra; impedir período inválido; preservar histórico de pessoa desativada; não conceder acesso gerencial apenas por conceder administração técnica.

### RF-12 — Sincronização e qualidade dos dados · MVP

Por fonte: última tentativa, último sucesso, cobertura, andamento, registros processados, duplicados ignorados, dados rejeitados e falhas. Usuário vê quando seus dados foram atualizados; administrador consulta detalhes e tenta novamente quando permitido.

**Aceite:** sucesso no Monday e falha no ponto produzem estados separados; atualização parcial não sinaliza período completo; tentativa repetida não soma horas novamente; administrador consegue localizar registros não associados; aviso informa o que fazer e mantém visível a data do último dado válido.

### RF-13 — Histórico e auditoria · MVP

Registrar criação/revisão de correspondência, alteração de regras, comentários, pedidos, respostas, decisões, reaberturas, atualizações relevantes e exportações. Exibir autoria, data, motivo e valores anteriores/novos quando aplicável.

**Aceite:** pessoa e gestor consultam o histórico autorizado do caso; decisão não apaga resposta anterior; reprocessamento identifica regra e dados utilizados; restrições de acesso continuam valendo no histórico e nos relatórios.

## 7. Fluxo de tratamento

| Estado atual | Ação e responsável | Próximo estado | Efeito esperado |
|---|---|---|---|
| Identificada | Gestor solicita justificativa/correção | Aguardando funcionário | Define pedido, responsável e prazo quando habilitado |
| Identificada | Funcionário envia justificativa espontânea | Aguardando gestor | Gestor recebe resposta e contexto |
| Aguardando funcionário | Funcionário responde | Aguardando gestor | Registra resposta e notifica gestor |
| Aguardando gestor | Gestor pede complemento ou rejeita resposta | Aguardando funcionário | Exige motivo; informa novo próximo passo |
| Aguardando gestor | Gestor aceita justificativa | Encerrada com justificativa | Mantém diferença numérica e decisão |
| Qualquer caso aberto | Dados completos atualizados ficam dentro da tolerância | Resolvida por correção | Registra comparação antes/depois e encerra pedido |
| Caso aberto | Gestor valida exceção de calendário elegível | Encerrada com justificativa | Registra motivo/regra; recalcula aplicabilidade quando cabível |
| Caso encerrado | Gestor reabre com motivo | Reaberta | Indica responsável e preserva histórico |
| Caso encerrado | Atualização altera materialmente os registros | Reaberta ou revisão sinalizada | Comportamento a aprovar em D-07; não apagar decisão anterior |

Vencida é uma condição do prazo, não um estado que substitui “aguardando funcionário” ou “aguardando gestor”. Uma notificação lida não resolve um caso. O funcionário não encerra unilateralmente a análise do gestor. O gestor pode registrar revisão, mas não zerar uma diferença manualmente.

## 8. Jornadas de uso esperadas

### 8.1 Funcionário — conferência e resposta

1. Entrar na visão “Minha jornada” e escolher o período.
2. Conferir a atualização das duas fontes e a cobertura.
3. Abrir o dia destacado no calendário ou em Minhas pendências.
4. Comparar batidas, intervalos e atividades; ler a explicação da diferença.
5. Se o registro estiver errado, corrigi-lo na origem conforme o processo da empresa e atualizar a consulta.
6. Se precisar explicar o caso, enviar justificativa objetiva no próprio dia/solicitação.
7. Acompanhar “aguardando gestor”, responder complementos e consultar a decisão.

### 8.2 Gestor — triagem e acompanhamento

1. Abrir Visão geral e selecionar período/equipe.
2. Conferir cobertura antes de interpretar a taxa de conciliação.
3. Consultar ranking por divergência absoluta, dias afetados ou vencimento.
4. Abrir a pessoa e localizar os dias que explicam o ranking.
5. Analisar registros e contexto; solicitar justificativa ou ajuste com mensagem clara.
6. Na fila “aguardando gestor”, avaliar a resposta e aceitar ou pedir complemento.
7. Exportar o relatório com filtros, atualização e situação do tratamento.

### 8.3 Administrador — preparação e manutenção

1. Definir usuários, equipes, gestores e respectivos acessos.
2. Associar cada pessoa às identidades nas duas fontes.
3. Validar regras, calendário, jornada, elegibilidade das atividades e tolerância.
4. Acompanhar uma carga inicial e resolver dados rejeitados/sem correspondência.
5. Validar amostra de jornadas completas e liberar a consulta aos usuários.
6. Acompanhar falhas, alterações de equipe e vigência de regras.

## 9. Cenários de aceite de ponta a ponta

Os exemplos abaixo usam a tolerância ilustrativa de 10/30 minutos e fontes completas, salvo indicação contrária.

| Cenário | Entrada | Resultado esperado |
|---|---|---|
| CA-01 Totais iguais | Ponto 08h00, Monday 08h00 | Diferença 00h00, conciliado |
| CA-02 Limite de tolerância | Ponto 08h00, Monday 07h50 | −00h10, conciliado dentro da tolerância; diferença preservada |
| CA-03 Faixa leve | Ponto 08h00, Monday 07h40 | −00h20, divergência leve |
| CA-04 Faixa crítica | Ponto 08h00, Monday 06h30 | −01h30, crítica; “a menos no Monday” |
| CA-05 Diferença positiva | Ponto 08h00, Monday 08h45 | +00h45, crítica; “a mais no Monday”, sem chamar de hora extra |
| CA-06 Compensação aparente | Dois dias com −02h00 e +02h00 | Saldo zero; divergência absoluta 04h00; dois dias divergentes |
| CA-07 Fonte indisponível | Ponto falhou; Monday 08h00 | Comparação incompleta; não assumir ponto zero |
| CA-08 Monday vazio confirmado | Ponto 08h00; consulta Monday completa e vazia | Motivo “sem lançamento no Monday”; −08h00 se elegível/encerrado |
| CA-09 Ponto vazio confirmado | Ponto zero confirmado; Monday 02h00 | Motivo “sem registro de ponto”; +02h00; percentual não calculável |
| CA-10 Folga prevista | Calendário dispensa o dia e não há registros | Não aplicável; fora da base de conciliação |
| CA-11 Aprovação | Gestor aceita justificativa para −01h30 | Tratamento encerrado; diferença e classificação numérica preservadas |
| CA-12 Correção na origem | Monday muda de 06h30 para 08h00; ponto 08h00 | Após atualização completa, resolvida por correção, com histórico |
| CA-13 Reimportação | Importar os mesmos registros duas vezes | Mesmos totais; nenhum caso/notificação duplicado |
| CA-14 Identidade ambígua | Duas pessoas têm o mesmo nome | Fila de correspondência; não misturar horas |
| CA-15 Período em andamento | Dia atual ainda não encerrado | Dados provisórios; fora do ranking conclusivo |
| CA-16 Amostra incompleta | 10 dias esperados, 8 completos, 6 conciliados | Taxa 75%; cobertura 80%; dois dias pendentes de dados |
| CA-17 Sem base válida | Nenhum dia elegível completo | Taxa “não calculável”; explicação da falta de base |
| CA-18 Exportação restrita | Funcionário tenta exportar equipe | Apenas escopo próprio disponível; sem dados de colegas |
| CA-19 Filtro combinado | Setembro + equipe A + crítica | Cards, tabela e relatório usam a mesma interseção |
| CA-20 Mudança de período | Seleção de pessoas seguida de novo filtro | Limpar seleção e informar; não manter destinatários ocultos |

## 10. Priorização e sequência sugerida

### MVP — ciclo completo de consulta e resolução

1. Acesso, perfis, equipes e correspondência de pessoas (RF-01, RF-02, RF-11).
2. Consulta/importação, qualidade, calendário básico e conciliação diária (RF-12, RN-01 a RN-12).
3. Painéis, ranking, detalhe e filtros (RF-03 a RF-06, RF-08).
4. Solicitações, justificativas, notificações individuais e histórico (RF-07, RF-09, RF-13).
5. Prévia e exportação de planilhas (RF-10).

O MVP está funcionalmente completo quando uma pessoa consegue conferir e responder a uma divergência, o gestor consegue analisá-la e exportá-la, e o administrador consegue explicar a origem e a regra dos números.

### Segunda etapa

PDF executivo, impressão formatada, favoritos, notificações em lote, prazos/lembretes automáticos, comparação entre equipes/períodos, indicadores de recorrência, tendências, anexos e jornadas avançadas.

### Evoluções futuras

Resumo semanal, sugestões de correção com justificativa verificável, alertas preventivos antes do fechamento, indicadores de tempo de resolução, painel executivo para diretoria, metas de qualidade do registro e comparativo de conciliação entre equipes. Regras de fechamento/reabertura de período e eventual escrita nas fontes exigem escopo próprio.

## 11. Decisões pendentes para o arquiteto e o responsável pelo produto

| ID | Decisão a validar | Por que afeta o produto |
|---|---|---|
| D-01 | Tolerâncias, precisão, arredondamento e vigência | Define classificação e comparabilidade histórica |
| D-02 | Fuso, jornada noturna e distribuição de durações entre dias | Evita comparar dias diferentes |
| D-03 | Origem do dado Monday e atribuição de tempo à pessoa | Define o que é uma hora elegível e evita duplicidade |
| D-04 | Dados efetivamente disponíveis no Ponto VR Mais e no Monday | Define se haverá batidas, totais, intervalos, ajustes, links e histórico; API/importação/permissões ainda não verificadas |
| D-05 | Entrada via desktop, múltiplos papéis, equipes e acesso histórico | Define quem pode consultar, decidir e exportar |
| D-06 | Frequência de atualização, prazo de lançamento e “dados antigos” | Define atualidade, provisório e momento de cobrança |
| D-07 | Mudanças após encerramento, fechamento mensal e reprocessamento | Define reabertura e preservação das decisões |
| D-08 | Reuniões, treinamentos, ausências, feriados e escalas | Define dias/horas elegíveis e evita falsos alertas |
| D-09 | Política de prazos, lembretes e delegação de gestor | Define responsável, vencimento e escalonamento |
| D-10 | Colunas, formato da planilha, PDF e comentários nas exportações | Define utilidade e conteúdo compartilhado |
| D-11 | Histórico, retenção, anexos e acesso administrativo | Define o ciclo de vida e a disponibilidade dos registros |
| D-12 | Download, impressão, abertura de links e sessão no WebView2 | Define como as ações aparecem dentro do desktop |
| D-13 | Metas de desempenho e disponibilidade | Validar tempo aceitável para abrir um mês de 50 pessoas, atualizar e exportar, com volume histórico real |

O arquiteto deve transformar essas definições em decisões de solução rastreáveis, mantendo explícitas as hipóteses ainda não confirmadas. Não pressupor que as fontes ofereçam uma API, um campo ou uma permissão específica sem verificar no contexto real da empresa.

## 12. Prompts para chats de programação

Use o prompt mestre junto desta especificação. Em seguida, use o prompt da funcionalidade desejada. Os prompts definem comportamento e interface; a escolha de implementação permanece com o arquiteto e a equipe. Os oito prompts temáticos consolidam o escopo da conversa e incorporam os esclarecimentos de regras deste documento.

### Prompt mestre — contexto do produto

```text
Atue na construção de uma aplicação de conciliação de horas para aproximadamente 50 funcionários. Use a especificação funcional anexa como referência de produto e preserve a identificação de seus requisitos.

A aplicação compara horas lançadas em atividades do Monday com horas de jornada registradas no Ponto VR Mais. A interface será React incorporada à aplicação desktop por WebView2, com experiência simples inspirada na organização visual do Monday e identidade própria em laranja e cinza.

Perfis: funcionário consulta apenas seus dados, acompanha diferenças, responde a pedidos e envia justificativas; gestor acompanha equipes autorizadas, analisa rankings e gráficos, solicita ajustes, decide justificativas e exporta; administrador gerencia pessoas, vínculos entre fontes, equipes, regras, calendário e qualidade dos dados.

A diferença é Monday menos ponto. Mostrar sinal e explicação textual. O ranking padrão deve somar as diferenças absolutas de cada dia, para que diferenças positivas e negativas não se anulem. Separar qualidade dos dados, resultado numérico e tratamento da pendência. Justificativa aceita preserva a diferença original. Fonte indisponível não equivale a zero. Dias provisórios e não aplicáveis ficam identificados e fora dos indicadores conclusivos.

Forneça navegação lateral, cards, tabelas compactas, etiquetas com texto, gráficos legíveis, filtros visíveis e detalhes de cada dia. Para 50 pessoas, usar ranking com acesso ao gráfico individual. Incluir teclado, foco visível, redimensionamento, preservação de filtros e mensagens de carregamento, vazio, erro, acesso restrito e dados antigos.

O MVP inclui acesso e perfis, correspondência das pessoas, calendário básico, qualidade dos dados, conciliação diária, painel do gestor e do funcionário, filtros, detalhes, justificativas, notificações internas individuais, exportação em planilha e histórico. PDF, favoritos, lembretes, notificações em lote, anexos e análises avançadas pertencem à segunda etapa.

Trate os valores de tolerância 10/30 minutos como exemplo pendente de validação. Não invente capacidades das fontes nem regras oficiais de folha, banco de horas ou jornada. Ajustes são feitos na origem e reavaliados após atualização. A proposta não está automaticamente implementada no projeto atual.

Para cada funcionalidade trabalhada, apresente comportamento do usuário, telas e ações, regras, permissões, estados alternativos e critérios de aceite. Identifique decisões pendentes que afetem o resultado. Inicialmente apresente a especificação da funcionalidade solicitada; escreva código apenas quando a solicitação do chat incluir implementação.
```

### Prompt 1 — Painel do gestor

```text
Especifique o painel do gestor da aplicação de conciliação Monday × Ponto VR Mais, usando RF-03, RF-04, RN-09 e RN-10 da especificação anexa.

O gestor deve descobrir rapidamente a cobertura dos dados, a taxa de dias conciliados, as pessoas com maiores diferenças, os casos críticos e as pendências que exigem ação. Todos os indicadores seguem período e equipe selecionados.

Descreva cabeçalho com atualização por fonte, cards, ranking por soma das diferenças absolutas diárias, gráfico comparativo ponto/Monday, distribuição por motivo e caminhos até o detalhe da pessoa/dia. No ranking, mostrar saldo, diferença absoluta, percentual quando calculável, dias afetados e cobertura. Evitar sobrepor curvas de 50 pessoas.

Inclua filtros, busca, ordenação, legenda, acessibilidade, estados de carregamento, vazio, incompletude e falha. Use o exemplo de −2h em um dia e +2h em outro: saldo zero e divergência absoluta 4h. Defina critérios de aceite observáveis e separe o MVP das análises avançadas. Não descreva arquitetura ou código.
```

### Prompt 2 — Conciliação diária

```text
Especifique a conciliação diária entre Monday e Ponto VR Mais conforme RN-01 a RN-12 e RF-05 da especificação anexa.

Comparar pessoa, jornada e período equivalentes. Apresentar jornada prevista como contexto, ponto líquido, total Monday elegível, diferença Monday menos ponto, magnitude e percentual quando ponto for maior que zero. Mostrar como cada total é formado.

Considerar intervalos sem desconto duplo, ajustes, atribuição de atividades compartilhadas, registros duplicados, cronômetros em andamento, feriados, férias, afastamentos, escalas, reuniões, jornadas noturnas e dados ausentes. Não inventar capacidades das fontes. Fuso e distribuição entre dias são decisões explícitas.

Separar qualidade/aplicabilidade, resultado numérico e tratamento. Distinguir zero confirmado, falta de registro e falha de consulta. Não considerar o dia em andamento uma divergência concluída. Preservar diferenças dentro da tolerância e após aprovação de justificativa.

Defina regras, prioridade visual, explicações ao usuário e critérios de aceite com limites de tolerância, denominador zero, fontes incompletas e reimportação idêntica. Não escreva código.
```

### Prompt 3 — Área do funcionário

```text
Especifique a área do funcionário usando RF-05, RF-06 e RF-07 da especificação anexa.

A pessoa consulta exclusivamente seus registros em Meu resumo, Meu calendário, Minhas pendências e Notificações. Deve entender totais, diferença, cobertura, dias conciliados e solicitações do gestor. Ao abrir o dia, vê ponto, intervalos, atividades, exclusões e explicação do resultado.

Descreva o caminho para corrigir na origem, atualizar a consulta, enviar uma justificativa, responder complementos e acompanhar a decisão. Justificativa aceita não apaga a diferença; notificação lida não é resposta. Mostrar responsável e próximo passo.

Detalhe calendário com legenda, lista priorizada, formulários e confirmação de envio, preservação de filtros, texto não punitivo e uso por teclado. Inclua cenários de dados antigos, dia provisório, nenhum resultado, erro de envio e resposta não salva. Forneça critérios de aceite; não descreva implementação.
```

### Prompt 4 — Tratamento de divergências

```text
Defina o fluxo de justificativas e correções conforme RF-07, RF-13 e a tabela de estados da especificação anexa.

Contemplar identificação, pedido do gestor, resposta espontânea do funcionário, aguardando funcionário, aguardando gestor, devolução para complemento, encerramento com justificativa, resolução por correção e reabertura. Vencimento é condição de prazo, não substitui o responsável atual.

Para cada transição, informar quem age, campos obrigatórios, validações, efeito para a outra pessoa, notificação e registro histórico. Gestor não aprova seu próprio caso. Justificativa não altera horas de origem. Atualização idêntica não duplica ocorrências; atualização que altera caso encerrado precisa de regra explícita.

Descreva comentários e motivos de decisão no MVP. Separe anexos, prazos configuráveis e lembretes como segunda etapa. Inclua exemplos de mensagens, cenários de concorrência entre resposta e atualização dos dados, e critérios de aceite. Não escreva código.
```

### Prompt 5 — Filtros e pesquisas

```text
Especifique os filtros compartilhados dos painéis, listas e relatórios conforme RF-08 da especificação anexa.

Incluir período predefinido/personalizado com limites inclusivos, pessoa, equipe, gestor autorizado, qualidade, motivo, gravidade, tratamento, direção e faixa da diferença, com/sem justificativa, responsável e prazo. Critérios diferentes combinam com E; várias opções no mesmo critério combinam com OU.

Mostrar filtros ativos em chips, total de resultados, limpar individual/todos e preservar contexto ao abrir e fechar detalhes. Mudança de filtro limpa seleção de destinatários. Cards, gráficos e exportação precisam refletir o mesmo conjunto. Favoritos entram na segunda etapa e não concedem novos acessos.

Descreva interface, validação de intervalo invertido, zero resultados, restrições por perfil e critérios de aceite. Não descreva bibliotecas ou código.
```

### Prompt 6 — Relatórios e exportação

```text
Especifique o módulo de relatórios conforme RF-10 e RN-09 da especificação anexa.

O usuário escolhe relatório, período, filtros, colunas, ordenação e agrupamento e vê a prévia antes de exportar. Funcionário exporta somente seus dados; gestor exporta pessoas autorizadas. Distinguir resultados filtrados de linhas selecionadas.

MVP: planilha com resumo, conciliação por pessoa/equipe, divergências, registros ausentes e pendências. Segunda etapa: PDF executivo, impressão, evolução mensal, justificativas e decisões. Horas extras somente quando houver classificação da origem, sem equiparar à diferença Monday/ponto.

Incluir totais, unidades, legenda do sinal, cobertura, atualização de cada fonte, regra aplicada, data de geração, autoria e filtros. Preservar diferenças absolutas diárias no ranking, durações superiores a 24h e valores não calculáveis. Explicar quando os dados mudarem desde a prévia.

Descreva nome do arquivo, sucesso/destino de download no desktop, sem resultados, falha e nova tentativa. Defina critérios de aceite e decisões pendentes; não indique bibliotecas ou arquitetura.
```

### Prompt 7 — Notificações internas

```text
Especifique a central de notificações internas conforme RF-09 da especificação anexa.

MVP: pedido individual do gestor, solicitação de correção/justificativa, resposta, decisão, lista de não lidas, histórico, filtro por tipo e ligação direta com o caso. Mensagem informa destinatário, motivo e próximo passo. Ler uma notificação não responde nem encerra a pendência.

Segunda etapa: envio por seleção/equipe, prazos, lembretes, proximidade do vencimento, pendência vencida e preferências. Mostrar destinatários antes do envio, resultados parciais e opção de repetir apenas falhas. Parar lembretes após resolução e impedir duplicidade por reimportação.

Falhas operacionais vão para responsáveis autorizados. Demais usuários recebem avisos sobre a qualidade de seus dados. Não gerar mensagem para todos a cada atualização sem mudança relevante. Se o acesso ao caso mudar, a notificação não revela conteúdo restrito.

Inclua exemplos respeitosos, ações rápidas, estados vazios/erro e critérios de aceite. Não escreva código nem proponha canais externos no MVP.
```

### Prompt 8 — Administração e qualidade dos dados

```text
Especifique a administração conforme RF-01, RF-02, RF-11, RF-12 e RF-13 da especificação anexa.

O administrador gerencia usuários, equipes, gestores, vigência de acessos, correspondências das identidades, tolerâncias, calendário e atividades elegíveis. Distinguir administrador técnico de gestor com acesso aos registros da equipe. Desativação preserva histórico e interrompe acesso.

Criar fila para pessoas não associadas, identidade ambígua, cadastro duplicado, registro inválido e fonte incompleta. Não associar apenas pelo nome. Exibir separadamente para cada fonte última tentativa, último sucesso, cobertura, processamento, ignorados e falhas.

Descreva carga inicial, validação de uma amostra, correção de correspondência, nova tentativa e reavaliação dos dias afetados sem duplicar horas. Alteração de regras precisa de vigência e alcance; conservar o antes/depois no histórico. Fontes, campos e permissões de integração ainda precisam ser verificados.

Inclua formulários, validações, alertas orientados à ação, permissões, estados das integrações e critérios de aceite. Separe calendário básico do MVP de escalas/regras avançadas. Não prescreva arquitetura ou código.
```

## 13. Como usar este material na arquitetura

1. Ler objetivo, limites e regras antes de escolher componentes.
2. Validar D-01 a D-08 com produto e responsáveis pelos dados para fechar o comportamento básico.
3. Mapear os requisitos RF para as capacidades da solução e confirmar as restrições reais das duas fontes.
4. Usar o manual HTML para revisar jornadas, rótulos e comportamento com funcionário, gestor e administrador.
5. Transformar os cenários CA em validação funcional de ponta a ponta, incluindo dados incompletos e controle de acesso.
6. Registrar decisões restantes, estimar fases e manter este documento e o manual coerentes quando o escopo mudar.

Não há dependência declarada entre este produto proposto e as funcionalidades atuais do repositório CEP-API. Os arquivos foram adicionados apenas como documentação de produto.
