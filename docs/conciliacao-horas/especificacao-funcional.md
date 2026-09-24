# Conciliação de horas — especificação funcional do produto

Versão 1.1 · 23/09/2026 · Fonte de verdade funcional para produto e engenharia

**Status: produto em construção.** Este documento consolida a visão do CEP Horas, registra o que já existe no backend e diferencia explicitamente capacidades entregues, parciais e planejadas. O [manual de uso proposto](manual.html) descreve a experiência esperada com dados fictícios; ele não comprova que uma funcionalidade esteja implementada.

Legenda utilizada neste documento:

- **Entregue:** existe no backend atual e possui contrato persistido ou endpoint disponível.
- **Parcial:** existe uma base técnica, mas o comportamento funcional ou o permissionamento ainda não está completo.
- **Planejado:** faz parte da visão aprovada do produto, porém ainda não foi implementado.

## 1. Objetivo e contexto

Criar uma central de conciliação que compare as horas registradas em atividades do Monday com a jornada registrada no Ponto VR Mais. A aplicação atenderá aproximadamente 50 membros, seus líderes e coordenadores. O resultado deve permitir identificar divergências, entender sua origem, solicitar esclarecimentos, acompanhar ajustes e emitir relatórios.

O membro precisa responder: “Minhas horas estão coerentes? Qual dia precisa de atenção? O que devo fazer?”. O líder precisa responder: “Quais pessoas dos meus times e quais períodos precisam de análise? Qual é a dimensão das diferenças? Quem está com a próxima ação?”. O coordenador precisa garantir que pessoas, times, fontes e regras estejam corretamente configurados em toda a organização.

A interface será uma página React incorporada à aplicação desktop por WebView2. Esta é uma restrição informada pelo solicitante. A arquitetura, os mecanismos de integração, a persistência, a autenticação e os componentes de implementação serão definidos pelo arquiteto.

O produto compara registros; uma diferença não comprova ausência, baixa produtividade ou irregularidade. Reuniões, treinamento, atividades internas, atrasos de lançamento e falhas de importação podem explicar diferenças. O contexto deve acompanhar os números.

### 1.1 Resultado esperado

- O líder identifica os casos prioritários dos times sob sua responsabilidade em uma única visão.
- O membro entende cada diferença sem precisar reconstruir manualmente dois relatórios.
- O coordenador administra todos os times, pessoas e integrações da organização.
- Cada pendência informa motivo, responsável, prazo, próximo passo e histórico.
- Todos os números podem ser rastreados até os registros que os compõem.
- Dados incompletos são destacados antes de qualquer conclusão.

### 1.2 Limites do escopo

- A proposta inicial consulta e concilia dados das fontes. Correções de batidas e lançamentos são realizadas na origem; após atualização, a aplicação reavalia o resultado.
- Registrar uma justificativa ou uma decisão não altera as horas originais.
- Jornada prevista é uma referência adicional, separada da comparação Monday × ponto.
- Banco de horas e horas extras oficiais, quando disponíveis, são apresentados como contexto. Sua apuração formal e o fechamento de folha não estão definidos neste produto.
- Leitura offline, gravação nas fontes, notificações externas e aplicativo móvel próprio não são compromissos do MVP.
- `SystemAdmin` é administração técnica da plataforma e não é um perfil operacional do CEP Horas.

### 1.3 Estado atual da implementação

O backend atual já entrega a fundação administrativa e de integração do CEP Horas:

| Capacidade | Estado atual |
|---|---|
| Autenticação desktop e web, refresh rotativo, revogação de sessões e recuperação de senha | **Entregue** |
| Isolamento de dados por organização | **Entregue** |
| Administração de organizações, usuários, convites, status e produtos | **Entregue** |
| Criação e edição de times | **Entregue** para `OrganizationAdmin` |
| Vínculos temporais de pessoas aos times como `Member` ou `Manager` | **Entregue** como estrutura e administração |
| Consulta dos diretórios do Monday e VR Mais | **Entregue** |
| Associação manual das duas identidades externas a uma pessoa | **Entregue** |
| Convite da pessoa associada e vínculo da conta após o aceite | **Entregue** |
| Atualização manual de 7 dias por Membro (próprio), Líder (próprios dados e time) e Coordenador (organização); carga inicial/reprocessamento administrativo de 90 dias | **Entregue** no backend |
| Consultas externas reduzidas ao escopo | **Parcial**: VR Mais recebe IDs e período; Monday filtra itens/subitens por responsável na origem, mas filtra a data das sessões localmente |
| Persistência idempotente do histórico bruto e retenção de 90 dias | **Entregue** |
| Histórico bruto por pessoa, fonte e período | **Entregue** com escopo de Coordenador, Líder e Membro |
| Coordenador com acesso total ao CEP Horas | **Entregue** por `OrganizationAdmin` nas capacidades existentes |
| Líder restrito aos próprios times | **Entregue** nas consultas atuais de times, pessoas e histórico |
| Membro com acesso aos próprios registros | **Entregue** nas consultas atuais de pessoa e histórico |
| Conciliação diária, tolerâncias, calendário e divergências | **Planejado** |
| Justificativas, decisões, alertas e notificações internas | **Planejado** |
| Painéis, relatórios e exportações | **Planejado** |

Os contratos técnicos já entregues estão detalhados em [`../workforce-admin-integration.md`](../workforce-admin-integration.md). O escopo de Líder e Membro já protege as consultas existentes; capacidades ainda inexistentes, como alertas, justificativas, divergências e exportações, permanecem planejadas e deverão reutilizar o mesmo cálculo de acesso.

## 2. Estrutura organizacional, perfis e permissões

O CEP Horas organiza o acesso da seguinte forma:

```text
Organização
├── Coordenadores — administram todo o CEP Horas da organização
├── Time A
│   ├── Líderes — cuidam somente dos times que lideram
│   └── Membros — consultam somente seus próprios dados
├── Time B
│   ├── Líderes
│   └── Membros
└── Time C
    ├── Líderes
    └── Membros
```

Uma pessoa pode ser membro de um time e líder de outro. Nesse caso, alterna entre “Minha jornada” e “Gestão do time”. O perfil ativo, a organização e o escopo de dados devem ficar visíveis. A troca de perfil revalida filtros, resultados e seleção de pessoas.

### 2.1 Correspondência com o modelo técnico

| Perfil do CEP Horas | Representação técnica atual | Escopo esperado |
|---|---|---|
| Coordenador | `UserRole.OrganizationAdmin` | Toda a organização |
| Líder | `TeamAssignmentRole.Manager` | Somente os times com vínculo vigente de líder |
| Membro | `TeamAssignmentRole.Member` e `UserRole.User` | Somente os próprios dados |
| Administrador técnico | `UserRole.SystemAdmin` | Administração da plataforma, fora da operação do CEP Horas |

Não será criado inicialmente um novo `UserRole.Coordinator`: `OrganizationAdmin` representa o coordenador do produto. Líder e membro são vínculos temporais de time e devem ser validados no banco a cada operação protegida; não devem depender somente de claims persistidos no token.

### 2.2 Matriz funcional desejada

| Capacidade | Membro | Líder | Coordenador |
|---|---|---|---|
| Consultar jornada e atividades | Somente próprias | Próprias e dos membros dos times liderados | Toda a organização |
| Solicitar atualização das fontes | Incremental para a organização | Incremental para a organização | Incremental ou reprocessamento completo de até 90 dias |
| Consultar ranking de divergências | Não | Somente times liderados | Toda a organização |
| Enviar justificativa e responder | Próprios casos | Próprios casos como membro | Próprios casos, quando aplicável |
| Solicitar ajuste ou justificativa | Não | Membros dos times liderados | Qualquer membro da organização |
| Aprovar, devolver e reabrir caso | Não | Times liderados; nunca o próprio caso | Toda a organização; nunca o próprio caso |
| Exportar relatório | Próprios dados | Times liderados | Toda a organização |
| Consultar alertas e notificações | Próprios | Próprios e dos times liderados | Toda a organização e falhas administrativas |
| Criar e editar times | Não | Não | Sim |
| Definir membros e líderes | Não | Não | Sim |
| Promover ou remover coordenadores | Não | Não | Sim, preservando ao menos um coordenador ativo |
| Configurar regras e integrações | Não | Consultar disponibilidade dos próprios times | Sim |
| Consultar auditoria | Próprios casos | Casos dos times liderados | Auditoria da organização |

### 2.3 Estado do permissionamento

O permissionamento atual usa `OrganizationAdmin` como Coordenador e consulta os vínculos `Manager` e `Member` vigentes diretamente no banco a cada requisição protegida. O Líder lista somente os times que lidera e consulta sua própria pessoa e os usuários com vínculo vigente nesses times. O Membro consulta somente a própria pessoa e os próprios registros. Vínculos futuros, encerrados ou pertencentes a time inativo não concedem acesso.

As garantias implementadas são:

- coordenador com acesso a toda a organização;
- líder limitado aos times em que possui vínculo `Manager` vigente;
- membro limitado à própria pessoa e aos próprios registros;
- nenhuma consulta entre organizações;
- vínculo futuro, encerrado ou inativo sem conceder acesso fora de sua vigência;
- usuário em múltiplos times recebendo a união estrita dos escopos vigentes;
- alterações de papel ou vínculo produzindo efeito sem depender da expiração do JWT;
- consultas atuais aplicando o mesmo escopo; novas exportações, notificações e casos deverão obrigatoriamente reutilizá-lo.

O `SystemAdmin` permanece um administrador técnico, não um perfil cotidiano do CEP Horas. Para suporte administrativo, ele pode operar uma organização selecionada explicitamente pelo parâmetro `organizationId`. Para os demais papéis, o parâmetro é opcional e nunca permite sair da organização registrada no token.

A atualização normal pode ser solicitada por qualquer perfil autenticado e cobre hoje mais os seis dias anteriores. O Membro atualiza somente as próprias identidades externas; o Líder, as suas e as dos membros de seus times vigentes; o Coordenador, as pessoas associadas na organização. A carga inicial administrativa e `full=true` cobrem 90 dias, incluindo diretórios; uma instalação antiga com apenas 60 dias precisa de `full=true` para completar o histórico. Atualizações de 7 dias usam os diretórios persistidos. No Monday, a atribuição é feita pelo único responsável da atividade/subitem, não pelo usuário que iniciou ou encerrou o cronômetro. A API filtra os itens por responsável, mas o histórico do cronômetro não aceita filtro de data na origem; as sessões são limitadas aos sete dias após a leitura dos itens selecionados. O saldo e todas as demais métricas de pessoa, time e organização devem usar exclusivamente o mesmo intervalo inclusivo selecionado no filtro; os indicadores consolidados ainda não foram implementados.

A desativação encerra novos acessos e preserva o histórico conforme a política definida pela organização. Mudanças de time e de líder mantêm datas de vigência. Como transferências de time são raras, a implementação atual usa os vínculos vigentes **hoje** para autorizar a consulta de todo o intervalo solicitado: o líder atual pode ver os registros anteriores à transferência e o líder anterior perde essa consulta. A atribuição histórica de horas e o acesso a períodos anteriores à transferência continuam como decisão pendente em D-05; não apresentar a regra atual como uma divisão histórica dos resultados por time.

## 3. Conceitos e informações do negócio

| Conceito | Significado e informações necessárias |
|---|---|
| Membro | Identidade interna, nome, situação, times, líderes e vigência de cada vínculo |
| Líder | Membro com vínculo `Manager` vigente em um ou mais times; o vínculo define seu escopo de gestão |
| Coordenador | Usuário com administração integral do CEP Horas dentro da organização |
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

No board Monday integrado, o único profissional na coluna Pessoa `PROFISSIONAL` recebe as sessões de tempo do item, mesmo se outra pessoa iniciar ou parar o relógio. No subitem, um profissional próprio preenchido prevalece; sem ele, o profissional do item pai é herdado. A coluna `R.T.` não atribui horas. Sessões de atividade com mais de um profissional são ignoradas, inclusive nos subitens que herdam essa ambiguidade; não se multiplicam horas silenciosamente.

### RN-04 — Convenção da diferença

`Diferença diária = horas Monday − horas de ponto`.

- Negativa: menos horas no Monday do que no ponto.
- Positiva: mais horas no Monday do que no ponto.
- Zero: totais iguais, desde que os dados estejam completos e a jornada seja elegível.

Sempre mostrar uma descrição junto ao sinal: “01h30 a menos no Monday”. Não usar “débito” ou “hora extra” como sinônimos automáticos dessa diferença.

### RN-05 — Tolerâncias

Exemplo inicial a validar: até 10 minutos de diferença absoluta = conciliado dentro da tolerância; de 11 a 30 = divergência leve; acima de 30 = divergência crítica. A tolerância usa a magnitude, independentemente do sinal.

Diferença dentro da tolerância continua visível e não vira zero. Limites, precisão, arredondamento e eventual variação por time/jornada precisam ser aprovados em D-01. Aplicar a regra vigente na data da análise e registrar a versão; reprocessar períodos anteriores exige indicar o alcance e conservar o histórico.

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
| Tratamento | Sem pendência, identificada, aguardando membro, aguardando líder, resolvida por correção, encerrada com justificativa, reaberta |

“Sem lançamento no Monday”, “sem registro de ponto” e “sem registros nas duas fontes” são motivos visíveis. “Aguardando justificativa” corresponde a aguardando membro; “aguardando análise” corresponde a aguardando líder. “Resolvido” é um agrupamento do tratamento, nunca uma alteração dos dados originais.

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

- Líder: Visão geral, Meus times, Divergências, Relatórios e Notificações. Situação dos dados e ação de atualização incremental ficam acessíveis no cabeçalho; a sincronização completa permanece restrita ao coordenador.
- Membro: Meu resumo, Meu calendário, Minhas pendências e Notificações.
- Coordenador: Usuários e times, Correspondências, Regras e calendário, Sincronização, Auditoria e visão integral da organização.
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

**Aceite:** um membro vê apenas seus dados; um líder só vê pessoas dos times que lidera; um coordenador vê toda a organização; alternar para a visão pessoal não mantém uma seleção do time; sessão expirada oferece nova entrada preservando o contexto quando possível; ninguém aprova a própria justificativa.

### RF-02 — Correspondência de pessoas · MVP

O coordenador associa identidades das fontes à pessoa interna. Mostrar “associado”, “sem correspondência”, “duplicado”, “ambíguo” e “inativo”. Pesquisar e revisar associações, visualizar origem e histórico, impedir correspondências conflitantes sem resolução explícita.

**Aceite:** pessoas com nomes iguais não são unidas automaticamente apenas pelo nome; registros sem associação ficam em fila de qualidade; após associação correta, os dias afetados podem ser reavaliados; troca de vínculo conserva autoria e histórico.

### RF-03 — Visão geral do líder e do coordenador · MVP

Cards: membros no escopo, totais comparáveis de ponto e Monday, saldo, divergência acumulada absoluta, taxa de dias conciliados, cobertura, dias/casos críticos, pessoas sem lançamento e pendências aguardando líder. Identificar a unidade de cada contagem.

Gráficos: comparação diária, barras das maiores divergências, distribuição por motivo e evolução da conciliação. Ranking mostra pessoa, ponto, Monday, saldo, divergência absoluta, percentual, dias afetados, cobertura e pendências. O líder vê apenas os times que lidera; o coordenador pode comparar todos os times. Tendências avançadas entram na segunda etapa.

**Aceite:** todos os cards obedecem ao mesmo período e escopo; clicar no ranking abre a pessoa com o período preservado; saldo zero não oculta dias opostos; registros incompletos aparecem em aviso separado; gráfico e tabela oferecem os mesmos valores.

### RF-04 — Meus times · MVP

Tabela com membro, time, ponto, Monday, saldo, divergência absoluta, dias divergentes, pendências e situação. Busca por nome, ordenação e abertura do detalhe. Seleção múltipla para exportação; cobrança em lote fica na segunda etapa.

**Aceite:** maior divergência absoluta aparece primeiro por padrão; informar “X pessoas encontradas”; identificar seleção da página versus todos os resultados; não incluir pessoas fora do escopo em uma exportação; não perder filtros ao voltar do detalhe.

### RF-05 — Detalhe da pessoa e do dia · MVP

Resumo da pessoa com jornada prevista, totais, cobertura, pendências, gráfico diário e histórico. Tabela por dia: data, previsto, ponto, Monday, diferença, qualidade, resultado e tratamento.

Abrir um dia mostra batidas/intervalos disponíveis, ajustes, total líquido, atividades e duração por atividade, projeto/board/grupo quando disponíveis, itens excluídos do cálculo e motivo, regra aplicada, justificativas, decisões e próximos passos. Oferecer abrir o registro de origem se houver referência utilizável.

**Aceite:** a soma das parcelas elegíveis explica o total; exclusões ficam visíveis; campos não fornecidos pela origem aparecem como indisponíveis; sinal e descrição da diferença concordam; o usuário sabe qual regra classificou o dia.

### RF-06 — Área do membro · MVP

Meu resumo apresenta totais pessoais, diferença, dias conciliados, cobertura e pedidos do líder. Meu calendário mostra situação de cada dia, legenda e seleção do período. Minhas pendências prioriza solicitações com prazo e casos que exigem resposta.

**Aceite:** clicar no calendário abre a mesma conciliação da lista; dias sem jornada e dias sem dados têm símbolos/textos distintos; o membro envia uma resposta e acompanha a decisão; o resultado numérico permanece após justificativa aceita; nenhum ranking de colegas fica disponível.

### RF-07 — Central de divergências e justificativas · MVP

Fila com motivo, gravidade, pessoa, data, diferença, responsável atual, prazo e tratamento. Líder ou coordenador solicita justificativa ou correção; membro responde; líder ou coordenador aceita, devolve para complemento ou encerra conforme regra e escopo. Comentários ficam no contexto do caso. Reabertura exige motivo. Anexos entram na segunda etapa.

**Aceite:** toda mudança de tratamento tem autor e data; pedido informa o que precisa ser esclarecido; devolução inclui motivo e próximo passo; resposta enviada aparece para ambos; aceitar justificativa não edita fontes; duplicar clique não cria respostas repetidas.

### RF-08 — Filtros e pesquisa · MVP / segunda etapa

MVP: período predefinido e personalizado, membro, time, líder quando aplicável, qualidade, motivo, gravidade, tratamento, direção/faixa da diferença, presença de justificativa, responsável e prazo. Oferecer hoje, semana, mês atual, mês anterior e intervalo livre. Explicar que hoje pode estar provisório.

Combinar critérios com “E”; múltiplas escolhas dentro de um mesmo filtro com “OU”. “Limpar filtros” volta ao padrão informado. Chips mostram critérios ativos. Favoritos e compartilhamento de visualizações ficam na segunda etapa, respeitando permissões de quem abre.

**Aceite:** datas inicial e final são inclusivas e validadas; intervalo invertido tem orientação clara; nome não encontrado oferece limpar pesquisa; chips, gráfico, tabela, cards e exportação usam o mesmo contexto; filtro preservado não restaura acesso a time não autorizado.

### RF-09 — Notificações internas · MVP / segunda etapa

MVP: mensagens individuais do líder ou coordenador, pedido de justificativa/ajuste, resposta enviada, decisão e acesso direto ao caso; central com não lidas, histórico, filtro por tipo, marcar como lida e marcar todas como lidas. Erros de integração são enviados aos coordenadores; membros e líderes veem avisos apenas dentro de seu escopo.

Segunda etapa: envio em lote por seleção/time, prazos configuráveis, lembretes automáticos, proximidade do vencimento, atraso, preferências e aviso de relatório pronto quando a geração for demorada. Mudança material de dados pode gerar aviso; cada sincronização bem-sucedida não precisa notificar todos.

**Aceite:** mostrar destinatário e contexto antes do envio; marcar como lida não equivale a responder; a mesma ocorrência não gera notificações duplicadas; lembretes cessam quando a pendência encerra; o vínculo só abre conteúdo ainda autorizado; envio em lote informa sucessos e falhas sem reenviar aos destinatários já atendidos.

Exemplo: “Olá, Ana. Em 08/09 há 01h30 a menos no Monday em relação ao ponto. Confira as atividades e envie uma justificativa ou ajuste o lançamento até o prazo indicado.”

### RF-10 — Relatórios e exportação · MVP / segunda etapa

MVP: resumo do período, conciliação por pessoa/time, divergências detalhadas, registros ausentes e pendências sem resposta, exportados em planilha. Membro exporta seus próprios dados. Líder exporta os times que lidera; coordenador exporta toda a organização.

Segunda etapa: PDF executivo, impressão formatada, justificativas/decisões, evolução mensal, comparação entre períodos e visão de horas extras quando a origem fornecer essa classificação. Não rotular diferença Monday × ponto como hora extra.

Prévia informa tipo, período, filtros, pessoas/linhas, colunas, ordenação, agrupamento e totais. Arquivo inclui geração, autoria, fontes, atualização/cobertura, regras, unidades e descrição dos sinais. Nome sugerido: `conciliacao-time-2026-09-01-a-2026-09-30.xlsx`.

**Aceite:** exportar mantém filtros, permissões e ordenação escolhidos; avisar se dados mudaram desde a prévia; campos indisponíveis não viram zero; preservar acentos e durações acima de 24h; sem resultados, explicar e permitir revisar filtros; falha oferece tentar novamente; informar sucesso e destino/ação de abertura conforme comportamento definido para o desktop.

### RF-11 — Administração · MVP / segunda etapa

MVP: usuários ativos/inativos, times, membros, líderes, coordenadores, correspondência entre fontes, escopo de acesso, tolerância inicial, fontes elegíveis, calendário básico e exceções. Registrar vigência das regras e alterações.

Segunda etapa: jornadas avançadas e múltiplas escalas, prazos por contexto, regras por time, delegações temporárias, anexos e políticas avançadas de fechamento. Cadastro básico de jornada e exceções é necessário desde o MVP para evitar falsos alertas.

**Aceite:** validar limites de tolerância e datas de exceções; avisar o alcance de mudança de regra; impedir período inválido; preservar histórico de pessoa desativada; somente coordenador cria times, define líderes e promove outro coordenador; `SystemAdmin` não recebe acesso operacional automático ao CEP Horas.

### RF-12 — Sincronização e qualidade dos dados · MVP

Por fonte: última tentativa, último sucesso, cobertura, andamento, registros processados, duplicados ignorados, dados rejeitados e falhas. Membro vê quando seus dados foram atualizados; líder vê a disponibilidade de seus times; coordenador consulta detalhes e tenta novamente quando permitido.

**Aceite:** sucesso no Monday e falha no ponto produzem estados separados; atualização parcial não sinaliza período completo; tentativa repetida não soma horas novamente; coordenador consegue localizar registros não associados; aviso informa o que fazer e mantém visível a data do último dado válido.

### RF-13 — Histórico e auditoria · MVP

Registrar criação/revisão de correspondência, alteração de regras, comentários, pedidos, respostas, decisões, reaberturas, atualizações relevantes e exportações. Exibir autoria, data, motivo e valores anteriores/novos quando aplicável.

**Aceite:** membro consulta os próprios casos; líder consulta os casos dos times que lidera; coordenador consulta a organização; decisão não apaga resposta anterior; reprocessamento identifica regra e dados utilizados; restrições de acesso continuam valendo no histórico e nos relatórios.

## 7. Fluxo de tratamento

| Estado atual | Ação e responsável | Próximo estado | Efeito esperado |
|---|---|---|---|
| Identificada | Líder ou coordenador solicita justificativa/correção | Aguardando membro | Define pedido, responsável e prazo quando habilitado |
| Identificada | Membro envia justificativa espontânea | Aguardando líder | Líder ou coordenador recebe resposta e contexto |
| Aguardando membro | Membro responde | Aguardando líder | Registra resposta e notifica o responsável no escopo |
| Aguardando líder | Líder ou coordenador pede complemento ou rejeita resposta | Aguardando membro | Exige motivo; informa novo próximo passo |
| Aguardando líder | Líder ou coordenador aceita justificativa | Encerrada com justificativa | Mantém diferença numérica e decisão |
| Qualquer caso aberto | Dados completos atualizados ficam dentro da tolerância | Resolvida por correção | Registra comparação antes/depois e encerra pedido |
| Caso aberto | Líder ou coordenador valida exceção de calendário elegível | Encerrada com justificativa | Registra motivo/regra; recalcula aplicabilidade quando cabível |
| Caso encerrado | Líder ou coordenador reabre com motivo | Reaberta | Indica responsável e preserva histórico |
| Caso encerrado | Atualização altera materialmente os registros | Reaberta ou revisão sinalizada | Comportamento a aprovar em D-07; não apagar decisão anterior |

Vencida é uma condição do prazo, não um estado que substitui “aguardando membro” ou “aguardando líder”. Uma notificação lida não resolve um caso. O membro não encerra unilateralmente a análise. Líder e coordenador podem registrar revisão dentro do próprio escopo, mas não zerar uma diferença manualmente.

## 8. Jornadas de uso esperadas

### 8.1 Membro — conferência e resposta

1. Entrar na visão “Minha jornada” e escolher o período.
2. Conferir a atualização das duas fontes e a cobertura.
3. Abrir o dia destacado no calendário ou em Minhas pendências.
4. Comparar batidas, intervalos e atividades; ler a explicação da diferença.
5. Se o registro estiver errado, corrigi-lo na origem conforme o processo da empresa e atualizar a consulta.
6. Se precisar explicar o caso, enviar justificativa objetiva no próprio dia/solicitação.
7. Acompanhar “aguardando líder”, responder complementos e consultar a decisão.

### 8.2 Líder — triagem e acompanhamento

1. Abrir Visão geral e selecionar período/time dentre os que lidera.
2. Conferir cobertura antes de interpretar a taxa de conciliação.
3. Consultar ranking por divergência absoluta, dias afetados ou vencimento.
4. Abrir a pessoa e localizar os dias que explicam o ranking.
5. Analisar registros e contexto; solicitar justificativa ou ajuste com mensagem clara.
6. Na fila “aguardando líder”, avaliar a resposta e aceitar ou pedir complemento.
7. Exportar o relatório com filtros, atualização e situação do tratamento.

### 8.3 Coordenador — preparação e manutenção

1. Definir usuários, times, membros, líderes, coordenadores e respectivos acessos.
2. Associar cada pessoa às identidades nas duas fontes.
3. Validar regras, calendário, jornada, elegibilidade das atividades e tolerância.
4. Acompanhar uma carga inicial e resolver dados rejeitados/sem correspondência.
5. Validar amostra de jornadas completas e liberar a consulta aos usuários.
6. Acompanhar falhas, alterações de time e vigência de regras.

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
| CA-11 Aprovação | Líder aceita justificativa para −01h30 de membro do seu time | Tratamento encerrado; diferença e classificação numérica preservadas |
| CA-12 Correção na origem | Monday muda de 06h30 para 08h00; ponto 08h00 | Após atualização completa, resolvida por correção, com histórico |
| CA-13 Reimportação | Importar os mesmos registros duas vezes | Mesmos totais; nenhum caso/notificação duplicado |
| CA-14 Identidade ambígua | Duas pessoas têm o mesmo nome | Fila de correspondência; não misturar horas |
| CA-15 Período em andamento | Dia atual ainda não encerrado | Dados provisórios; fora do ranking conclusivo |
| CA-16 Amostra incompleta | 10 dias esperados, 8 completos, 6 conciliados | Taxa 75%; cobertura 80%; dois dias pendentes de dados |
| CA-17 Sem base válida | Nenhum dia elegível completo | Taxa “não calculável”; explicação da falta de base |
| CA-18 Exportação restrita | Membro tenta exportar um time | Apenas escopo próprio disponível; sem dados de colegas |
| CA-19 Filtro combinado | Setembro + time A + crítica | Cards, tabela e relatório usam a mesma interseção |
| CA-20 Mudança de período | Seleção de pessoas seguida de novo filtro | Limpar seleção e informar; não manter destinatários ocultos |
| CA-21 Isolamento do líder | Líder do time A consulta pessoa exclusiva do time B | Acesso negado sem revelar dados do time B |
| CA-22 Múltiplos times | Pessoa lidera A e B, mas não C | Consulta retorna a união de A e B; nenhum dado de C |
| CA-23 Vínculo encerrado | Vínculo de líder terminou antes do período consultado | Aplicar a política histórica definida em D-05; nunca ampliar acesso silenciosamente |
| CA-24 Coordenador | Coordenador consulta qualquer time da própria organização | Acesso permitido; outra organização continua inacessível |

## 10. Priorização e sequência sugerida

### MVP — ciclo completo de consulta e resolução

1. Acesso, perfis, times e correspondência de pessoas (RF-01, RF-02, RF-11).
2. Consulta/importação, qualidade, calendário básico e conciliação diária (RF-12, RN-01 a RN-12).
3. Painéis, ranking, detalhe e filtros (RF-03 a RF-06, RF-08).
4. Solicitações, justificativas, notificações individuais e histórico (RF-07, RF-09, RF-13).
5. Prévia e exportação de planilhas (RF-10).

O MVP está funcionalmente completo quando um membro consegue conferir e responder a uma divergência, um líder consegue analisar e exportar apenas os próprios times, e um coordenador consegue administrar a organização e explicar a origem e a regra dos números.

### Segunda etapa

PDF executivo, impressão formatada, favoritos, notificações em lote, prazos/lembretes automáticos, comparação entre times/períodos, indicadores de recorrência, tendências, anexos e jornadas avançadas.

### Evoluções futuras

Resumo semanal, sugestões de correção com justificativa verificável, alertas preventivos antes do fechamento, indicadores de tempo de resolução, painel executivo para diretoria, metas de qualidade do registro e comparativo de conciliação entre times. Regras de fechamento/reabertura de período e eventual escrita nas fontes exigem escopo próprio.

## 11. Decisões pendentes para o arquiteto e o responsável pelo produto

| ID | Decisão a validar | Por que afeta o produto |
|---|---|---|
| D-01 | Tolerâncias, precisão, arredondamento e vigência | Define classificação e comparabilidade histórica |
| D-02 | Fuso, jornada noturna e distribuição de durações entre dias | Evita comparar dias diferentes |
| D-03 | Origem do dado Monday e atribuição de tempo à pessoa | Define o que é uma hora elegível e evita duplicidade |
| D-04 | Campos adicionais disponíveis no Ponto VR Mais e no Monday | As integrações e a persistência bruta já existem; falta homologar todos os significados, campos e regras de uso na conciliação |
| D-05 | Entrada via desktop, múltiplos vínculos de time e acesso histórico após transferência | Define como a vigência do vínculo limita consulta, decisão e exportação de períodos passados |
| D-06 | Frequência de atualização, prazo de lançamento e “dados antigos” | Define atualidade, provisório e momento de cobrança |
| D-07 | Mudanças após encerramento, fechamento mensal e reprocessamento | Define reabertura e preservação das decisões |
| D-08 | Reuniões, treinamentos, ausências, feriados e escalas | Define dias/horas elegíveis e evita falsos alertas |
| D-09 | Política de prazos, lembretes e delegação de líder | Define responsável, vencimento e escalonamento |
| D-10 | Colunas, formato da planilha, PDF e comentários nas exportações | Define utilidade e conteúdo compartilhado |
| D-11 | Histórico, retenção, anexos e acesso administrativo | Define o ciclo de vida e a disponibilidade dos registros |
| D-12 | Download, impressão, abertura de links e sessão no WebView2 | Define como as ações aparecem dentro do desktop |
| D-13 | Metas de desempenho e disponibilidade | Validar tempo aceitável para abrir um mês de 50 pessoas, atualizar e exportar, com volume histórico real |

O arquiteto deve transformar essas definições em decisões de solução rastreáveis, mantendo explícitas as hipóteses ainda não confirmadas. Não pressupor que as fontes ofereçam uma API, um campo ou uma permissão específica sem verificar no contexto real da empresa.

## 12. Prompts para chats de programação

Use o prompt mestre junto desta especificação. Em seguida, use o prompt da funcionalidade desejada. Os prompts definem comportamento e interface; a escolha de implementação permanece com o arquiteto e a equipe. Os oito prompts temáticos consolidam o escopo da conversa e incorporam os esclarecimentos de regras deste documento.

### Prompt mestre — contexto do produto

```text
Atue na construção do CEP Horas, uma aplicação de conciliação de horas para aproximadamente 50 membros organizados em times. Use a especificação funcional anexa como fonte de verdade do produto e preserve a identificação de seus requisitos.

A aplicação compara horas lançadas em atividades do Monday com horas de jornada registradas no Ponto VR Mais. A interface será React incorporada à aplicação desktop por WebView2, com experiência simples inspirada na organização visual do Monday e identidade própria em laranja e cinza.

Perfis: membro consulta apenas seus dados, acompanha diferenças, recebe alertas, responde a pedidos e envia justificativas; líder acompanha somente os times em que possui vínculo vigente de líder, analisa rankings e gráficos, solicita ajustes, decide justificativas e exporta; coordenador administra toda a organização, cria times, define membros e líderes, promove outros coordenadores e gerencia pessoas, integrações, regras, calendário e qualidade dos dados. `SystemAdmin` é administração técnica da plataforma e não recebe acesso operacional automático ao CEP Horas.

A diferença é Monday menos ponto. Mostrar sinal e explicação textual. O ranking padrão deve somar as diferenças absolutas de cada dia, para que diferenças positivas e negativas não se anulem. Separar qualidade dos dados, resultado numérico e tratamento da pendência. Justificativa aceita preserva a diferença original. Fonte indisponível não equivale a zero. Dias provisórios e não aplicáveis ficam identificados e fora dos indicadores conclusivos.

Forneça navegação lateral, cards, tabelas compactas, etiquetas com texto, gráficos legíveis, filtros visíveis e detalhes de cada dia. Para 50 pessoas, usar ranking com acesso ao gráfico individual. Incluir teclado, foco visível, redimensionamento, preservação de filtros e mensagens de carregamento, vazio, erro, acesso restrito e dados antigos.

O MVP inclui acesso e perfis, correspondência das pessoas, calendário básico, qualidade dos dados, conciliação diária, painel do líder e do membro, filtros, detalhes, justificativas, notificações internas individuais, exportação em planilha e histórico. PDF, favoritos, lembretes, notificações em lote, anexos e análises avançadas pertencem à segunda etapa.

Trate os valores de tolerância 10/30 minutos como exemplo pendente de validação. Não invente capacidades das fontes nem regras oficiais de folha, banco de horas ou jornada. Ajustes são feitos na origem e reavaliados após atualização. Respeite a tabela de estado atual: não apresente capacidades planejadas como implementadas.

Para cada funcionalidade trabalhada, apresente comportamento do usuário, telas e ações, regras, permissões, estados alternativos e critérios de aceite. Identifique decisões pendentes que afetem o resultado. Inicialmente apresente a especificação da funcionalidade solicitada; escreva código apenas quando a solicitação do chat incluir implementação.
```

### Prompt 1 — Painel do líder

```text
Especifique o painel do líder da aplicação de conciliação Monday × Ponto VR Mais, usando RF-03, RF-04, RN-09 e RN-10 da especificação anexa.

O líder deve descobrir rapidamente a cobertura dos dados, a taxa de dias conciliados, as pessoas com maiores diferenças, os casos críticos e as pendências que exigem ação. Todos os indicadores seguem o período e um dos times que o usuário lidera. O coordenador pode usar a mesma visão para qualquer time ou para toda a organização.

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

### Prompt 3 — Área do membro

```text
Especifique a área do membro usando RF-05, RF-06 e RF-07 da especificação anexa.

A pessoa consulta exclusivamente seus registros em Meu resumo, Meu calendário, Minhas pendências e Notificações. Deve entender totais, diferença, cobertura, dias conciliados e solicitações do líder ou coordenador. Ao abrir o dia, vê ponto, intervalos, atividades, exclusões e explicação do resultado.

Descreva o caminho para corrigir na origem, atualizar a consulta, enviar uma justificativa, responder complementos e acompanhar a decisão. Justificativa aceita não apaga a diferença; notificação lida não é resposta. Mostrar responsável e próximo passo.

Detalhe calendário com legenda, lista priorizada, formulários e confirmação de envio, preservação de filtros, texto não punitivo e uso por teclado. Inclua cenários de dados antigos, dia provisório, nenhum resultado, erro de envio e resposta não salva. Forneça critérios de aceite; não descreva implementação.
```

### Prompt 4 — Tratamento de divergências

```text
Defina o fluxo de justificativas e correções conforme RF-07, RF-13 e a tabela de estados da especificação anexa.

Contemplar identificação, pedido do líder ou coordenador, resposta espontânea do membro, aguardando membro, aguardando líder, devolução para complemento, encerramento com justificativa, resolução por correção e reabertura. Vencimento é condição de prazo, não substitui o responsável atual.

Para cada transição, informar quem age, campos obrigatórios, validações, efeito para a outra pessoa, notificação e registro histórico. Líder e coordenador não aprovam o próprio caso. Justificativa não altera horas de origem. Atualização idêntica não duplica ocorrências; atualização que altera caso encerrado precisa de regra explícita.

Descreva comentários e motivos de decisão no MVP. Separe anexos, prazos configuráveis e lembretes como segunda etapa. Inclua exemplos de mensagens, cenários de concorrência entre resposta e atualização dos dados, e critérios de aceite. Não escreva código.
```

### Prompt 5 — Filtros e pesquisas

```text
Especifique os filtros compartilhados dos painéis, listas e relatórios conforme RF-08 da especificação anexa.

Incluir período predefinido/personalizado com limites inclusivos, pessoa, time, líder autorizado, qualidade, motivo, gravidade, tratamento, direção e faixa da diferença, com/sem justificativa, responsável e prazo. Critérios diferentes combinam com E; várias opções no mesmo critério combinam com OU.

Mostrar filtros ativos em chips, total de resultados, limpar individual/todos e preservar contexto ao abrir e fechar detalhes. Mudança de filtro limpa seleção de destinatários. Cards, gráficos e exportação precisam refletir o mesmo conjunto. Favoritos entram na segunda etapa e não concedem novos acessos.

Descreva interface, validação de intervalo invertido, zero resultados, restrições por perfil e critérios de aceite. Não descreva bibliotecas ou código.
```

### Prompt 6 — Relatórios e exportação

```text
Especifique o módulo de relatórios conforme RF-10 e RN-09 da especificação anexa.

O usuário escolhe relatório, período, filtros, colunas, ordenação e agrupamento e vê a prévia antes de exportar. Membro exporta somente seus dados; líder exporta somente os times que lidera; coordenador exporta toda a organização. Distinguir resultados filtrados de linhas selecionadas.

MVP: planilha com resumo, conciliação por pessoa/time, divergências, registros ausentes e pendências. Segunda etapa: PDF executivo, impressão, evolução mensal, justificativas e decisões. Horas extras somente quando houver classificação da origem, sem equiparar à diferença Monday/ponto.

Incluir totais, unidades, legenda do sinal, cobertura, atualização de cada fonte, regra aplicada, data de geração, autoria e filtros. Preservar diferenças absolutas diárias no ranking, durações superiores a 24h e valores não calculáveis. Explicar quando os dados mudarem desde a prévia.

Descreva nome do arquivo, sucesso/destino de download no desktop, sem resultados, falha e nova tentativa. Defina critérios de aceite e decisões pendentes; não indique bibliotecas ou arquitetura.
```

### Prompt 7 — Notificações internas

```text
Especifique a central de notificações internas conforme RF-09 da especificação anexa.

MVP: pedido individual do líder ou coordenador, solicitação de correção/justificativa, resposta, decisão, lista de não lidas, histórico, filtro por tipo e ligação direta com o caso. Mensagem informa destinatário, motivo e próximo passo. Ler uma notificação não responde nem encerra a pendência.

Segunda etapa: envio por seleção/time, prazos, lembretes, proximidade do vencimento, pendência vencida e preferências. Mostrar destinatários antes do envio, resultados parciais e opção de repetir apenas falhas. Parar lembretes após resolução e impedir duplicidade por reimportação.

Falhas operacionais vão para responsáveis autorizados. Demais usuários recebem avisos sobre a qualidade de seus dados. Não gerar mensagem para todos a cada atualização sem mudança relevante. Se o acesso ao caso mudar, a notificação não revela conteúdo restrito.

Inclua exemplos respeitosos, ações rápidas, estados vazios/erro e critérios de aceite. Não escreva código nem proponha canais externos no MVP.
```

### Prompt 8 — Administração e qualidade dos dados

```text
Especifique a administração conforme RF-01, RF-02, RF-11, RF-12 e RF-13 da especificação anexa.

O coordenador gerencia usuários, times, membros, líderes, outros coordenadores, vigência de acessos, correspondências das identidades, tolerâncias, calendário e atividades elegíveis. Distinguir coordenador do CEP Horas de `SystemAdmin`, que administra tecnicamente a plataforma. Desativação preserva histórico e interrompe acesso.

Criar fila para pessoas não associadas, identidade ambígua, cadastro duplicado, registro inválido e fonte incompleta. Não associar apenas pelo nome. Exibir separadamente para cada fonte última tentativa, último sucesso, cobertura, processamento, ignorados e falhas.

Descreva carga inicial, validação de uma amostra, correção de correspondência, nova tentativa e reavaliação dos dias afetados sem duplicar horas. Alteração de regras precisa de vigência e alcance; conservar o antes/depois no histórico. Fontes, campos e permissões de integração ainda precisam ser verificados.

Inclua formulários, validações, alertas orientados à ação, permissões, estados das integrações e critérios de aceite. Separe calendário básico do MVP de escalas/regras avançadas. Não prescreva arquitetura ou código.
```

## 13. Como usar este material na arquitetura

1. Ler objetivo, limites e regras antes de escolher componentes.
2. Validar D-01 a D-08 com produto e responsáveis pelos dados para fechar o comportamento básico.
3. Mapear os requisitos RF para as capacidades da solução e confirmar as restrições reais das duas fontes.
4. Usar o manual HTML para revisar jornadas, rótulos e comportamento com membro, líder e coordenador; atualizar o manual quando a nomenclatura antiga aparecer.
5. Transformar os cenários CA em validação funcional de ponta a ponta, incluindo dados incompletos e controle de acesso.
6. Registrar decisões restantes, estimar fases e manter este documento e o manual coerentes quando o escopo mudar.

O CEP-API já contém a fundação administrativa, de autenticação, de times e de integração descrita na seção 1.3. Todo agente deve conferir essa tabela e os contratos técnicos antes de propor código, preservando compatibilidade com endpoints e dados já entregues.
