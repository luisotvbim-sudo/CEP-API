# Plano de produto para conciliação de horas

## 1. Recomendação executiva

A aplicação deve responder a uma pergunta: **as horas de trabalho registradas no ponto de cada funcionário estão representadas nos seus apontamentos do Micro Planejamento?**

O cenário recomendado usa o VR Mais como cadastro principal de funcionários e referência de ponto, e o quadro **EG03_MICRO PLANEJAMENTO** como fonte de apontamentos do Monday. A comparação nasce no dia de trabalho de cada pessoa; semana, mês e intervalo personalizado são somas dessas comparações diárias. A atividade explica onde o tempo foi registrado, mas não é a unidade principal da aferição.

**Decisão confirmada de produto: cada sessão do Monday pertence ao usuário que deu start no relógio.** O responsável atual pela atividade, a coluna PROFISSIONAL e quem encerrou o relógio não substituem esse usuário. Essa decisão simplifica a atribuição e deve ser aplicada de maneira uniforme, inclusive quando outra pessoa encerra ou edita o registro.

O produto será uma central de conferência, não um novo sistema de ponto ou de gestão de tarefas. Funcionários corrigem os registros nos sistemas de origem e acompanham a atualização na central. Gestores identificam diferenças, consultam sua composição, enviam solicitações e exportam resultados. Uma justificativa aceita encerra o atendimento, mas não transforma horas diferentes em horas iguais.

A interface permanece em React dentro do desktop por WebView2, com organização inspirada no Monday e identidade em laranja e cinza. O plano não escolhe bibliotecas, banco de dados, linguagem de integração ou infraestrutura, e não autoriza implementação.

### 1.1 Decisões confirmadas e recomendações

| Tema | Situação | Direção |
|---|---|---|
| Objetivo | Confirmado | Comparar quantidade de horas do ponto e do Monday |
| Cadastro inicial | Confirmado | Importar funcionários do VR Mais |
| Fonte Monday | Confirmado | Quadro EG03_MICRO PLANEJAMENTO |
| Dono da hora | Confirmado | Usuário que iniciou a sessão do relógio |
| Busca de pessoa | Confirmado como preferência | Nome facilita encontrar a correspondência; vínculo confirmado usa identificadores estáveis |
| Atividades | Confirmado | Não selecionar uma atividade específica para aferir; considerar os apontamentos do escopo |
| Perfis e experiência | Confirmado | Funcionário e gestor; interface simples, desktop, laranja e cinza |
| Forma de comparação | Recomendada | Pessoa × dia/jornada; agregação por período |
| Correção | Recomendada | Somente na origem; central consulta, explica e acompanha |
| Tolerância e prazo | Propostos, não aprovados | Uma tolerância simples e um prazo de lançamento; valores na seção 12 |
| Histórico e total de ponto | Exigem homologação | Demonstrar cobertura e confirmar significado dos valores antes de concluir diferenças |

### 1.2 O que a comparação comprova

Se ponto e Monday tiverem 8 horas válidas no mesmo dia, os **totais estão conciliados**. Isso não comprova que cada minuto do cronômetro ocorreu dentro de um intervalo de presença, nem avalia produtividade, qualidade de entrega ou regularidade trabalhista.

Por exemplo, 8 horas no ponto e 8 horas no Monday podem ter intervalos diferentes. Para o objetivo deste produto, a quantidade diária é o resultado principal. Sobreposições e intervalos incompatíveis são sinais para conferência; uma auditoria minuto a minuto seria outro escopo. Não usar expressões como “horas comprovadamente trabalhadas em tarefas” para um resultado baseado apenas em totais.

## 2. Evidências e limites da viabilidade

As consultas autorizadas às contas demonstraram leitura de funcionários, sessões de tempo e relatórios de jornada. A investigação é de viabilidade, por amostragem; **não houve conciliação completa de uma competência nem validação de todos os funcionários**. Os números abaixo retratam as leituras do estudo, não métricas de uso do produto. A referência temporal e as fontes privadas estão no inventário de fontes.[^1]

| Evidência observada | Implicação para o plano | Limitação |
|---|---|---|
| 32 funcionários ativos retornados pelo VR Mais | O cadastro pode partir da fonte de ponto | A estimativa de aproximadamente 50 pessoas ainda precisa ser confrontada com a população e as permissões |
| 43 usuários habilitados retornados pelo Monday | Há identidades disponíveis para associação | Usuário Monday não equivale automaticamente a funcionário elegível |
| 23 correspondências únicas por nome completo normalizado entre os 32 funcionários e os usuários consultados | O nome é um bom auxílio à preparação inicial | São sugestões, não identidades confirmadas; nove não tiveram esse mesmo resultado |
| Micro Planejamento com 3.777 itens informados nos metadados e estrutura de subitens com 85 itens | A leitura precisa cobrir o quadro e sua hierarquia | Contagem de itens não informa quantas sessões existem nem garante histórico completo |
| Amostra de 20 itens principais com 37 sessões | Há detalhe de apontamento utilizável | Amostra não aleatória e insuficiente para estimar taxas da equipe |
| As 37 sessões tinham identificação de quem iniciou; 36 tinham início e fim | A regra escolhida tem suporte nos registros examinados | Isso não garante preenchimento em todos os registros históricos ou futuros |
| 17 sessões tinham alguma informação de início/fim manual | Entrada manual precisa ser aceita quando os dados necessários existirem | Manual não significa inválido ou suspeito |
| Três sessões tinham usuários diferentes no início e no fim; duas atividades com sessões não tinham PROFISSIONAL preenchido | A atribuição por start evita depender desses outros campos | Não há motivo para reabrir a decisão de autoria por causa desses casos |
| Relatório de jornada retornou totais e batidas em consultas curtas | Há uma referência candidata para o ponto diário | É preciso homologar dias com ajuste, abono e jornadas especiais |
| Consulta curta de jornada retornou sucesso; variante com paginação retornou erro 500 | A carga do VR precisa de teste de cobertura antes de ser liberada | Não demonstra indisponibilidade geral nem estabelece a causa do erro |

A documentação do Monday descreve sessões com identificação própria, início, fim e usuário iniciador. O total da coluna corresponde ao conjunto de sessões e não substitui o detalhe por pessoa e data.[^2] A documentação oficial da VR publica consultas de colaboradores e extração de relatórios de jornada e de registros de ponto.[^3]

Permanecem não demonstrados: cobertura histórica de itens arquivados ou movidos, volume integral de sessões, desempenho para uma competência, semântica do total VR em todas as exceções, acesso a toda a população e comportamento de alterações antigas. A viabilidade é favorável, mas a liberação deve depender dos testes da seção 13.

## 3. Comparação das alternativas

| Alternativa | Vantagem | Problema para este objetivo | Decisão recomendada |
|---|---|---|---|
| Comparar total mensal do ponto com total mensal Monday | Visão rápida | Falta em um dia e excesso em outro se anulam | Usar somente como resumo acompanhado do detalhe diário |
| Somar o total da atividade e atribuir ao responsável atual | Poucos campos | Mistura datas e contribuições; depende de uma atribuição que pode mudar | Não usar |
| Extrair sessões e agrupar por usuário que iniciou e dia | Respeita a regra definida e permite explicar os números | Exige cobertura, identificação e datas válidas | Base recomendada |
| Exigir correspondência entre cada intervalo de ponto e cada sessão | Investiga simultaneidade | Amplia o objetivo e pode penalizar apontamentos retrospectivos válidos | Fora da primeira versão |
| Usar planilhas exportadas como rotina permanente | Facilita uma conferência manual inicial | Depende de coleta repetida e dificulta atualização compartilhada | Usar como referência de homologação, não como fluxo principal |
| Cada desktop consultar as fontes de forma independente | Dispensa visão compartilhada inicial | Pode produzir versões diferentes dos mesmos números e multiplicar consultas | Preferir resultados e atualizações compartilhados |

A escolha funcional é **conferência diária por sessão, com leitura periódica das fontes e uma visão comum para funcionário e gestor**. Não há necessidade de varrer todos os quadros do Monday, criar novas tarefas ou alterar o processo de planejamento para começar.

## 4. Cenário ideal de operação

### 4.1 Preparação inicial

O administrador importa o cadastro do VR, confere a população com o responsável da empresa e identifica quem deve participar da comparação. Importar todos os funcionários disponíveis não significa cobrar apontamento de todos: quem não registra ponto ou não está sujeito ao processo deve aparecer como não elegível, com motivo e vigência.

Em seguida, revisa as sugestões de correspondência com o Monday. O sistema mostra nome e identificação disponíveis nas duas fontes. Nomes iguais, abreviações e contas duplicadas exigem confirmação. A decisão é registrada uma vez e reutilizada; renomear um usuário não muda a pessoa proprietária do histórico.

Por fim, são definidos o início da cobertura, o calendário básico, os acessos, a tolerância e o prazo de lançamento. A central só apresenta o período como completo depois da conferência das duas fontes. Funcionários sem vínculo não desaparecem da lista: ficam na preparação pendente.

### 4.2 Rotina do funcionário

O funcionário registra o ponto normalmente no VR e inicia/encerra suas sessões nas atividades do Micro Planejamento. Ao abrir a central, encontra o mês atual, as últimas atualizações e os dias que precisam de atenção. Não precisa selecionar quadros nem repetir manualmente as horas.

Ao abrir um dia, vê ponto, sessões e diferença. Se faltou lançar uma atividade ou encerrar um relógio, corrige no Monday. Se houver problema de ponto, usa o processo já existente no VR. Pode solicitar atualização ou responder ao gestor na própria central, sem registrar novamente a jornada ali.

O dia de hoje aparece como parcial. Um aviso de fonte desatualizada explica que ainda não é possível concluir a comparação. O funcionário tem oportunidade de conferir e corrigir antes de receber uma cobrança automática.

### 4.3 Rotina do gestor

O gestor escolhe período e equipe e vê primeiro se os dados estão completos. Em seguida, encontra as pessoas com maior diferença acumulada diária, abre os dias que formam esse número e consulta o detalhe. A atividade funciona como rastreabilidade, não como filtro obrigatório da comparação.

Quando necessário, envia uma solicitação individual e acompanha a resposta. Se a origem for corrigida, o resultado é recalculado. Se a diferença for explicada e aceita, ela permanece no relatório numérico com o atendimento encerrado por justificativa. A exportação utiliza o mesmo recorte visto na tela.

## 5. Regras centrais de negócio

### 5.1 Identidade e propriedade do tempo

A sequência de identificação é: funcionário VR confirmado → usuário Monday associado → sessões iniciadas por esse usuário. A conta utilizada para entrar na central também deve estar associada a essa pessoa; conhecer um nome ou possuir acesso ao desktop não concede acesso aos registros de terceiros.

O campo de referência para o dono da sessão é `started_user_id`, documentado como o identificador do usuário que a iniciou.[^2] Esta é a tradução da decisão de produto, não uma inferência baseada no responsável da tarefa.

| Situação | Regra |
|---|---|
| Pessoa A inicia e A encerra | A recebe a duração da sessão |
| Pessoa A inicia e B encerra | A continua recebendo a duração |
| Atividade está atribuída a B, mas A inicia | A recebe a duração |
| PROFISSIONAL está vazio, mas A iniciou | A recebe a duração; o vazio não bloqueia a conciliação |
| A e B iniciam sessões próprias na mesma atividade | Cada um recebe suas próprias sessões |
| Entrada manual com iniciador e datas válidos | Aplica-se a mesma regra; mostrar a origem manual como contexto |
| Sessão sem usuário iniciador | Pendência de identificação; não substituir pelo responsável da atividade |
| Iniciador ainda não associado ao VR | Preservar a sessão e encaminhar à fila de correspondência |
| Nome foi alterado | Manter o vínculo pelos identificadores confirmados |

Não dividir a duração igualmente entre responsáveis, não replicá-la para todos os participantes e não transferi-la para quem encerrou o relógio. Se alguém iniciar em nome de outra pessoa, a regra definida ainda atribui a hora a quem iniciou; a central não tenta adivinhar a intenção.

### 5.2 Escopo do Monday

Incluir as sessões das atividades e subitens pertencentes ao Micro Planejamento no escopo temporal validado. Não limitar a coleta pela coluna PROFISSIONAL, por R.T., por nome da atividade, por status “em andamento”, pelo cronograma ou pela data de criação do item. Uma tarefa antiga pode conter uma sessão recente.

No quadro examinado, H.GASTA é a coluna de controle de tempo. H.PREV representa outro campo, e H.GASTA (H) é uma fórmula: não devem ser somados como fontes adicionais de horas. O cronograma serve ao planejamento, não define o dia efetivo do apontamento. A expressão da fórmula não foi validada e não é necessária para a regra proposta.[^1]

Sessões próprias do item e dos subitens podem participar. Resumos e espelhamentos não entram novamente. O Monday permite apresentar no item pai o total dos subitens, portanto somar ambos os totais visuais pode duplicar tempo.[^4] A exigência de produto é que cada sessão de origem contribua uma única vez, mesmo após reimportação ou movimentação.

Filtros por atividade podem existir no detalhe para investigação. Se ocultarem parcelas, não devem recalcular silenciosamente a comparação geral e apresentá-la como se ainda representasse todo o trabalho da pessoa.

### 5.3 Qual valor do ponto usar

A referência desejada é o **tempo líquido de trabalho registrado no VR**, considerando os intervalos uma única vez. Jornada contratada, horas previstas, saldo do banco e adicionais não são substitutos desse tempo.

No relatório de jornada, `total_time` é descrito como horas totais, `shift_time` como horas previstas, `time_cards` como pontos e `time_balance` como saldo.[^3] Uma amostra de dia com seis batidas apresentou total de 9 horas compatível com a soma de seus intervalos de trabalho.[^1] Isso apoia o uso do relatório como candidato, mas não comprova seu significado em todos os tipos de jornada.

Recomendação: homologar o total diário do VR contra o relatório utilizado pela empresa, incluindo dia comum, correção, ausência parcial abonada e, se houver, jornada noturna. Confirmar que a parcela comparada representa duração de trabalho, sem transformar crédito de ausência, adicional ou fator de cálculo em obrigação de apontamento no Monday. A VR possui processos distintos de ajuste e abono; o produto deve preservar essa distinção.[^5]

Se o total não puder ser isolado com confiança, o dia fica pendente de validação. Não começar reconstruindo toda a apuração de ponto por conta própria nem fazer desconto manual silencioso. Se a empresa optar por aferir exclusivamente marcações originais, desconsiderando ajustes aprovados, isso muda a referência e exige decisão explícita antes da implementação.

### 5.4 Data e janela de comparação

Usar a data efetiva da sessão, não a data em que foi criada ou alterada. Proposta inicial: fuso `America/Sao_Paulo`, a validar com a operação. Um apontamento feito depois, mas referente ao dia anterior, corrige o dia anterior.

Para jornadas diurnas, a unidade recomendada é o dia civil local. Para trabalho que atravessa meia-noite, é preciso manter a mesma unidade de jornada usada no total do VR. Não dividir o Monday por meia-noite e comparar automaticamente com um ponto consolidado inteiro no dia de início.

No piloto, homologar primeiro jornadas diurnas e manter casos noturnos ou multidia como não conclusivos até validar sua distribuição. Isso é uma limitação de cobertura visível, não autorização para ignorar funcionários ou horas.

### 5.5 Cálculo e significado da diferença

Para cada pessoa e dia comparável:

- P = duração válida do ponto.
- M = soma das sessões válidas atribuídas à pessoa no Monday.
- Diferença = M − P.
- A menos no Monday = máximo entre P − M e zero.
- A mais no Monday = máximo entre M − P e zero.

Exibir “01h30 a menos no Monday” ou “00h45 a mais no Monday”. “A menos” não é automaticamente hora não trabalhada; “a mais” não é automaticamente hora extra. A central compara registros, não calcula remuneração.

Preservar a precisão disponível; somar antes de arredondar a apresentação. Duração de 07h30 não equivale a 7,30 horas. Totais mensais devem suportar valores acima de 24 horas. Quando segundos mudarem uma classificação ou explicarem uma diferença aparentemente igual na tela, mostrá-los no detalhe e na exportação precisa.

### 5.6 Tolerância simples

Recomenda-se uma única tolerância diária configurável, sem uma matriz de gravidade por equipe na primeira versão. A proposta inicial é 10 minutos em valor absoluto, inclusive no limite, sujeita à aprovação da empresa. Não é uma tolerância legal nem significa autorizar omissão de lançamentos.

Até a tolerância: “Conciliado dentro da tolerância”, mantendo a diferença visível. Acima dela: “A menos no Monday” ou “A mais no Monday”. A ordenação pela magnitude já permite priorizar os casos maiores; faixas leve/crítica não são necessárias para começar.

A comparação usa a precisão preservada, não o texto arredondado. Com tolerância de 10 minutos, diferença de 10min01s fica fora dela. Uma alteração de regra registra vigência e não reclassifica períodos passados sem informar seu alcance.

### 5.7 Relógios abertos, duplicidade e sobreposição

Sessão ainda aberta aparece separada das horas encerradas. O dia fica provisório ou pendente de encerramento, sem cobrança conclusiva baseada nesse total. Não encerrar relógios automaticamente nem supor que a sessão terminou junto com a última batida.

Uma sessão repetida pela importação entra uma única vez. Duas sessões diferentes do mesmo usuário com intervalos sobrepostos precisam de sinalização de qualidade: mostrar a soma recebida e o conflito, sem descartar uma delas arbitrariamente nem subtrair a sobreposição silenciosamente. Até a revisão, o dia não recebe selo conclusivo de conciliação.

Entrada manual não é, sozinha, motivo para rejeição. Datas ausentes, duração negativa ou impossibilidade de determinar a jornada são motivos para impedir conclusão. A situação deve indicar qual registro precisa ser conferido.

### 5.8 Ausência de registros e aplicabilidade

| Situação | Resultado esperado |
|---|---|
| Ponto válido de 8h e busca Monday completa sem sessões | 8h a menos no Monday, se o dia estiver encerrado e elegível |
| Monday com 2h e ponto efetivamente confirmado como zero | 2h a mais no Monday, sem percentual baseado em ponto zero |
| Uma das fontes falhou ou só parte foi consultada | Dados incompletos; não substituir por zero |
| Ambas consultadas sem registros em dia dispensado | Não aplicável |
| Ambas sem registros em dia esperado de trabalho | “Sem registros nas duas fontes — verificar”; não declarar conciliado apenas porque 0 = 0 |
| Férias, folga ou ausência integral confirmada sem trabalho | Não aplicável à comparação de horas trabalhadas |
| Ausência parcial com trabalho no restante do dia | Comparar a parcela efetivamente trabalhada, se identificável |
| Cadastro sem vínculo Monday ou cobertura histórica incerta | Resultado não conclusivo; pendência de dados |

No cenário ideal, reuniões, treinamento e atividades internas realizadas durante o trabalho também têm apontamentos elegíveis no Micro Planejamento. Não é necessário que pertençam a uma tarefa de projeto específica. Se o processo da empresa não registra esses períodos no quadro, parte das diferenças será esperada: o sistema deve explicá-la, não criar lançamentos fictícios ou subtrair uma estimativa automática.

## 6. Histórico, atualização e confiança

### 6.1 O limite mais importante do histórico Monday

A documentação atual informa que a navegação por `items_page` retorna itens ativos. Itens arquivados ou excluídos exigem consulta por identificadores conhecidos; essa listagem não percorre todos os arquivados do quadro.[^6] Portanto, concluir a paginação dos itens ativos não prova que todas as horas históricas foram recuperadas.

A recomendação é estabelecer uma data de início de cobertura homologada. O histórico anterior só será rotulado como completo quando houver evidência suficiente. Uma exportação de referência pode ajudar na conferência; seu conteúdo e alcance precisam ser verificados, sem presumir que inclua automaticamente tudo que foi arquivado.

Para períodos acompanhados, preservar a referência das sessões e dos itens já vistos, suas revisões e a situação de acesso. Se um item sumir da listagem, não transformar suas horas anteriores em zero. Distinguir, quando verificável, mudança de acesso, arquivamento, movimentação e exclusão. Se a causa ou o valor atual não puderem ser verificados, manter o último conhecido com ressalva, fora da base conclusiva afetada.

Não ampliar silenciosamente o estudo para todos os quadros. A recuperação de horas anteriores movidas para fora do Micro Planejamento depende de autorização de escopo. Preservação do histórico já recebido não equivale a iniciar coleta generalizada de outros quadros. Tampouco captura eventos anteriores ao primeiro acesso ou garante detectar um item criado e removido entre duas leituras: a cobertura precisa considerar o processo de arquivamento da empresa.

### 6.2 Atualização recomendada

Funcionário e gestor devem consultar o mesmo conjunto de resultados, com data de referência visível. Abrir cada desktop não deve disparar uma carga integral independente. A preferência por processamento compartilhado é uma recomendação de confiabilidade e operação; a solução concreta fica com o arquiteto.

Proposta de partida, a validar por volume e limites: atualizar os dias recentes a cada 30 minutos durante a operação e revisar o período corrente diariamente. Correções em períodos anteriores exigem atualização sob demanda e uma rotina de revisitação com alcance explícito. Não prometer detecção de toda alteração histórica com uma janela curta.

O Monday documenta limites de chamadas, complexidade e concorrência; sua paginação usa cursores com validade limitada.[^7][^8] Por isso, a frequência só vira compromisso depois de uma medição com o quadro real. Eventos automáticos podem ser estudados depois para antecipar atualizações, sem substituir a verificação periódica de completude.

### 6.3 Comportamento em falhas

Mostrar, por fonte, última tentativa, último sucesso, intervalo coberto e pendências. Monday atualizado e VR com falha não equivalem a conciliação atualizada. O botão “Atualizar” informa que a solicitação foi recebida e o resultado quando concluída, sem criar cópias de horas ou casos.

No VR, a variante paginada do relatório curto falhou, enquanto a consulta sem essa paginação funcionou.[^1] Recomenda-se validar recortes controlados por pessoa/período e demonstrar que nenhum registro ficou truncado; não assumir que retirar paginação resolve a carga integral. O contrato publicado descreve paginação, mas o comportamento observado precisa de homologação ou esclarecimento do fornecedor.[^3]

Preservar o último resultado conhecido em falhas, com a data correspondente e ressalva. Uma atualização não deve misturar parcelas novas e antigas e apresentar o conjunto como definitivamente completo. Alterações relevantes preservam antes/depois e reavaliam os dias afetados.

## 7. Indicadores que atendem ao objetivo

A visão principal deve ter poucos indicadores e uma base comparável explícita. Pessoas/dias sem identidade, cobertura ou dados válidos aparecem em uma fila de preparação separada; não são colocados no fim do ranking como se tivessem desempenho melhor.

| Indicador | Definição | Uso |
|---|---|---|
| Ponto comparável | Soma de P nos dias elegíveis, completos e encerrados | Referência do período |
| Monday comparável | Soma de M nos mesmos dias | Volume de apontamentos correspondente |
| A menos no Monday | Soma de máximo(P − M, 0), dia a dia | Prioridade alinhada ao objetivo de encontrar horas faltantes |
| A mais no Monday | Soma de máximo(M − P, 0), dia a dia | Conferir possíveis excessos, registros de ponto ou apontamentos |
| Diferença acumulada absoluta | Soma de valor absoluto(M − P), dia a dia | Ordenação padrão das pessoas com registros mais distantes |
| Saldo do período | Soma de M − P | Resumo complementar, não substitui a divergência diária |
| Dias conciliados | Dias dentro da tolerância ÷ dias comparáveis | Taxa numérica de conciliação, com denominador visível |
| Cobertura | Dias comparáveis ÷ dias encerrados elegíveis esperados | Indicar quanto da população/período foi efetivamente avaliado |

As somas preservam diferenças pequenas, inclusive dentro da tolerância. A classificação e a contagem de dias fora dela aparecem separadamente. Se o calendário ou a elegibilidade não permitirem conhecer os dias esperados, a cobertura percentual fica “Não calculável”, com a contagem conhecida e a limitação.

Exemplo: segunda-feira com ponto 8h e Monday 6h; terça-feira com ponto 8h e Monday 10h. O saldo é zero, mas existem 2h a menos, 2h a mais e diferença absoluta de 4h. O período não pode parecer totalmente conciliado.

Se forem mostradas taxas relativas de horas, “Monday ÷ ponto” deve ser chamada de proporção de apontamento, pode ultrapassar 100% e não equivale a comprovação de cobertura dos mesmos intervalos. Não se recomenda colocá-la como indicador principal na primeira versão. Divisão por zero ou falta de base resulta em “Não calculável”.

Totais recebidos de dias ainda não comparáveis podem aparecer no detalhe, separados dos totais comparáveis. A exportação e os gráficos devem respeitar a mesma distinção.

## 8. Interface simples em laranja e cinza

### 8.1 Organização geral

Uma navegação curta, com “Minha conferência”, “Equipe”, “Pendências” e “Relatórios”, conforme a permissão. Correspondências, integrações e regras ficam em “Administração”, fora da rotina do funcionário. Notificações são acessíveis pelo cabeçalho, não exigem um módulo de comunicação separado.

O período fica sempre visível. Padrão recomendado: mês atual, distinguindo hoje dos dias encerrados. Atalhos para semana, mês anterior e intervalo personalizado. Começo e fim do filtro são inclusivos. Ao trocar o perfil, revalidar pessoas, equipes e permissões.

Usar cinza no texto, navegação e superfícies; laranja nas ações e destaques. Gráfico do ponto em cinza contínuo e do Monday em laranja tracejado. Rótulos, sinais e estilos de linha devem transmitir o significado também sem cor. Manter contraste, foco de teclado, redimensionamento e alternativas textuais aos gráficos.

### 8.2 Tela principal do gestor

No topo: período, equipe, busca por pessoa, última atualização de cada fonte e ações “Atualizar” e “Exportar”. Exibir um aviso compacto quando a base estiver incompleta, com acesso à lista de pendências de dados.

Até quatro cartões: horas a menos no Monday, horas a mais no Monday, dias conciliados e cobertura. A tabela é a peça principal, com funcionário, ponto, Monday, a menos, a mais, dias divergentes e cobertura. Ordenação padrão pela diferença absoluta diária acumulada; opção rápida “Mais horas faltantes”.

O gráfico geral usa barras horizontais para as maiores diferenças, limitado inicialmente aos primeiros dez resultados com acesso aos demais. Ao selecionar uma pessoa, abrir suas duas curvas diárias. Não sobrepor cinquenta pessoas no mesmo gráfico. Valores ausentes aparecem como lacunas, nunca como zero ou uma linha que sugira medição contínua.

Filtros iniciais: período, pessoa, equipe, direção da diferença, somente dias divergentes, qualidade dos dados e situação da solicitação. Filtros avançados podem expandir a área, sem ocupar permanentemente a tela. Tabela, cartões, gráfico e exportação usam o mesmo recorte declarado.

### 8.3 Tela principal do funcionário

Mostrar somente os próprios dados. O resumo informa ponto, Monday, diferença e dias a conferir. Abaixo, uma lista diária simples; calendário pode ser uma visualização alternativa, não uma segunda obrigação de navegação.

Cada linha informa data, ponto, Monday, diferença e situação. O funcionário abre o dia em uma ação e encontra o que precisa conferir. Pedidos do gestor aparecem com contexto e próxima ação. Não mostrar rankings de colegas nem chamar diferenças de baixa produtividade.

### 8.4 Detalhe do dia

Um painel ou página apresenta três blocos: resumo da diferença; ponto com total utilizado, batidas e informação de ajuste disponível; sessões Monday com atividade, início, fim, duração e pessoa identificada pelo start. Entrada manual, relógio aberto e qualquer parcela não utilizada ficam identificados.

A soma das linhas consideradas deve explicar exatamente o total. Permitir abrir a atividade no Monday quando houver link e permissão. O painel de pendência contém histórico e mensagem do gestor, resposta do funcionário e situação do atendimento. Fechar o detalhe preserva período, filtros e posição da lista.

Para o WebView2, combinar com o desktop onde links externos serão abertos, como o arquivo exportado será salvo e como a sessão será encerrada. A navegação incorporada e a comunicação com o desktop precisam restringir conteúdo e origens autorizadas, conforme as recomendações de segurança da Microsoft.[^9]

### 8.5 Situações sem resultado

Tratar separadamente: carregando, nenhum resultado para os filtros, nenhuma sessão encontrada após consulta completa, pessoa sem correspondência, fonte indisponível, período parcialmente coberto, acesso restrito e dia em andamento. Cada mensagem informa o próximo passo. O usuário não precisa interpretar códigos de erro de integração.

## 9. Solicitações, notificações e relatórios

### 9.1 Atendimento de uma diferença

Manter separadas três informações: **qualidade dos dados**, **resultado numérico** e **situação da conversa**. Na interface simples, a qualidade pode ser um aviso, o resultado uma etiqueta e o atendimento uma indicação de responsável.

Fluxo mínimo: gestor solicita conferência → funcionário responde ou corrige na origem → central atualiza → gestor acompanha o resultado ou aceita a explicação. Uma resposta espontânea do funcionário também pode abrir a análise. Pedir complemento mantém o histórico e devolve a próxima ação ao funcionário.

Correção efetiva que elimine a diferença encerra por correção. Aceite de explicação encerra por justificativa sem alterar P, M ou a diferença. Mudança posterior que volte a produzir divergência relevante sinaliza revisão e preserva a decisão anterior. Atualização idêntica não recria o caso.

### 9.2 Notificações internas

Na primeira versão, incluir envio individual pelo gestor, resposta do funcionário e comunicação da decisão. A mensagem informa pessoa, dia ou período, diferença, motivo do pedido e link para o contexto. Confirmar o destinatário antes do envio. Marcar como lida não equivale a responder nem resolver.

Exemplo fictício: “Há 01h30 a menos no Monday em 10/09. Confira os apontamentos desse dia e responda aqui se precisar explicar a diferença.”

Não disparar cobranças enquanto houver fonte incompleta ou prazo de lançamento em aberto. Problemas de integração vão aos administradores; funcionários recebem aviso sobre a disponibilidade dos próprios dados. Envios em lote, lembretes automáticos e escalonamento ficam para depois do piloto.

### 9.3 Exportações

Primeira versão: planilha de conciliação com resumo por funcionário e linhas por dia. Relatório detalhado pode incluir as sessões que formam os totais; comentários e justificativas só entram por escolha explícita e com acesso autorizado. Funcionário exporta somente os próprios dados.

Colunas essenciais: pessoa, data/jornada, ponto, Monday, diferença assinada, a menos, a mais, qualidade, resultado, situação do atendimento e referências das fontes. Incluir período, filtros, atualização de cada fonte, data de geração, cobertura, tolerância e unidade de duração.

A prévia e o arquivo devem usar o mesmo resultado de referência; avisar se houve atualização antes da geração. Preservar acentos, duração acima de 24h e campos desconhecidos sem convertê-los em zero. Validar conteúdo de texto para que nomes de atividades ou comentários não sejam executados como fórmulas na planilha. PDF executivo e impressão formatada podem entrar depois.

## 10. Responsabilidades, acesso e operação

| Papel | Responsabilidade | Limite |
|---|---|---|
| Funcionário | Conferir seus dias, corrigir na origem e responder | Não acessa ou exporta colegas |
| Gestor | Acompanhar equipe autorizada, solicitar conferência e avaliar respostas | Não aprova o próprio caso nem altera horas pela central |
| Administrador | Pessoas, correspondências, escopo, regras e falhas | Administração técnica não concede automaticamente visão de todas as jornadas |
| Responsável pelo ponto/RH | Homologar total de referência, calendário e exceções | A central não substitui o fechamento oficial |
| Responsável pelo Monday | Confirmar processo de apontamento, acesso e movimentação de itens | Não precisa reorganizar o quadro como condição inicial |
| Arquiteto | Traduzir garantias funcionais em uma solução verificável | Não presumir capacidades não testadas das fontes |

A consulta precisa respeitar equipe, vigência do acesso e histórico autorizado. Funcionário inativado deixa de ter novo acesso, mas o histórico permitido é preservado conforme política. Importar uma pessoa do VR não cria automaticamente uma conta de acesso nem envia convite.

Os tokens pessoais do Monday refletem as permissões da conta de origem.[^10] A VR também documenta token por usuário com acesso sujeito aos filtros de segurança, além de modalidade de abrangência maior.[^11] Portanto, uma resposta bem-sucedida não prova acesso a toda a empresa. Homologar a população e a cobertura com o responsável pelos dados.

Recomenda-se manter credenciais de integração fora do conteúdo React, dos arquivos distribuídos ao funcionário, de relatórios, mensagens e logs. O acesso do funcionário à central deve ser individual e independente dessas credenciais. Usar apenas leitura nas fontes, manter trilha de acesso e decisões e coletar somente dados necessários; CPF, endereço, salário e documentos médicos não são necessários para esta comparação.

Antes de produção, substituir as credenciais utilizadas no estudo por credenciais controladas e apropriadas ao escopo, sem interromper outras integrações inadvertidamente. Definir responsável pela renovação, falhas e retirada de acesso. A política de retenção, descarte e compartilhamento deve ser aprovada pela organização; este plano não estabelece prazo legal.

## 11. Primeira versão e sequência de entrega

### 11.1 O que entra na primeira versão

O MVP deve completar o ciclo de conferência, não apenas exibir um gráfico. Inclui cadastro importado, revisão de correspondências, acesso pessoal/gerencial, leitura do Micro Planejamento e do ponto, comparação diária, qualidade dos dados, tabela da equipe, gráfico individual, filtros por período, detalhe rastreável, exportação em planilha e solicitação interna individual com resposta.

Calendário básico, inativos no histórico, tratamento de duplicidade, cobertura e proteção de acesso não são opcionais de acabamento: evitam conclusões erradas. Já o calendário visual, muitas faixas de gravidade, relatórios variados e grandes painéis executivos não precisam bloquear a primeira entrega.

### 11.2 O que fica fora inicialmente

Não incluir criação automática de atividades, start/stop pela central, edição de ponto, preenchimento automático de horas faltantes, cálculo de folha, banco de horas próprio, pontuação de produtividade, decisões disciplinares, notificações externas, anexos sensíveis, inteligência artificial para atribuir horas ou varredura de todos os quadros.

Após o piloto: avaliar notificações em lote, lembretes, PDF, filtros salvos, tendências, jornadas especiais e histórico adicional. Evolução não implica aprovação automática de cada item.

### 11.3 Fases com critérios de passagem

| Fase | Entrega esperada | Critério para seguir |
|---|---|---|
| 0 — Fechar a referência | População, vínculos, campo de ponto, período inicial, regras propostas e processo de arquivamento | Responsáveis concordam com o significado de cada número e com as limitações |
| 1 — Provar a conferência | Amostra de pessoa/dia com total rastreado em ambas as fontes | Todos os casos da amostra têm cálculo reproduzível ou pendência explicitamente correta |
| 2 — Consulta funcional | Visões pessoal/equipe, detalhe, filtros, atualização e exportação | Números consistentes entre telas e arquivo; acesso segregado verificado |
| 3 — Ciclo de atendimento | Solicitação, resposta, decisão, notificações e histórico | Correção e justificativa têm efeitos diferentes e não duplicam casos |
| 4 — Piloto e liberação | Uso acompanhado, medição e ampliação gradual | Critérios de qualidade, cobertura e usabilidade da seção 13 atendidos |

As fases são um plano de trabalho futuro. Não há estimativa de prazo de desenvolvimento sem decisão de arquitetura, disponibilidade da equipe e homologação das integrações. O piloto sugerido é de cinco a dez funcionários e um gestor durante duas semanas úteis; duração e grupo são propostas, não uma agenda criada.

## 12. Configuração inicial recomendada e decisões restantes

As escolhas abaixo permitem planejar sem inventar acordos já feitos. A autoria por start e o quadro de origem estão encerrados como decisões de produto e não precisam ser investigados novamente como dúvidas de negócio.

| Decisão | Proposta inicial | Quem valida | Efeito de não validar |
|---|---|---|---|
| População | Todos os funcionários visíveis no VR no cadastro; conciliar somente elegíveis | Gestor e responsável VR | Não afirmar que os aproximadamente 50 estão cobertos |
| Total do ponto | Total líquido homologado no relatório de jornada VR | RH/responsável pelo ponto | Dias com referência incerta permanecem não conclusivos |
| Unidade diária | Dia local para jornadas diurnas; fuso São Paulo | Operação e RH | Jornadas incompatíveis ficam pendentes |
| Tolerância | 10 minutos absolutos por dia, limite inclusivo | Gestor/produto | Pode-se mostrar diferença bruta; não declarar regra aprovada |
| Prazo de lançamento | Até o próximo dia útil da equipe, em horário a definir | Gestor | Exibir provisório e não automatizar cobranças |
| Frequência | 30 minutos para dados recentes e revisão diária do período corrente, após medição | Arquiteto e operação | Não prometer dados em tempo real |
| Início do histórico | Primeiro período com cobertura demonstrável | Gestor e responsável Monday | Não classificar lacunas antigas como horas faltantes |
| Trabalho fora de tarefas de projeto | Todas as horas trabalhadas deveriam ter apontamento elegível; explicações preservadas quando não houver | Gestor | Diferenças esperadas devem permanecer explicadas, não escondidas |
| Acesso histórico e retenção | Escopo e vigência explícitos por papel | Organização | Sem liberação ampla ou retenção indefinida presumida |

Não é preciso responder a uma longa lista para entender o plano. Antes do piloto, as prioridades são confirmar a população, homologar o total de ponto e definir a cobertura histórica. As demais propostas podem ser aprovadas como configuração inicial e revistas com evidência de uso.

## 13. Homologação e critérios de sucesso

### 13.1 Como validar antes de liberar

Escolher cinco a dez funcionários com vínculos confirmados e dez dias úteis encerrados. Incluir pessoas com muitas atividades, sessões manuais e ao menos uma situação de ajuste de ponto, se disponível. Não tomar somente os primeiros itens retornados pela API como amostra representativa.

Conferir o ponto com o relatório usado pela empresa e o Monday com o histórico de sessões ou uma exportação que tenha o mesmo escopo. Para cada pessoa/dia, registrar os totais de referência, o resultado esperado e a explicação de eventual diferença. Dados pessoais usados na homologação ficam em acesso restrito, não no repositório.

Cobrir os cenários críticos abaixo com registros reais disponíveis e exemplos controlados fora da produção quando necessário. Não editar apontamentos reais para fabricar testes. O propósito é provar cálculo, cobertura e comportamento, não forçar uma taxa alta de conciliação.

### 13.2 Cenários de aceite

Nos exemplos numéricos, as fontes estão completas, o dia está encerrado e a tolerância proposta é de 10 minutos, salvo indicação contrária.

| ID | Cenário | Resultado esperado |
|---|---|---|
| P-01 | Ponto 8h e Monday 8h | Conciliado, diferença zero |
| P-02 | Ponto 8h e Monday 6h30 | 1h30 a menos no Monday |
| P-03 | Ponto 8h e Monday 9h | 1h a mais no Monday, sem classificar como hora extra |
| P-04 | −2h em um dia e +2h em outro | Saldo zero; diferença absoluta 4h; dois dias divergentes |
| P-05 | A inicia, B encerra e a tarefa pertence a C | Toda a sessão pertence a A |
| P-06 | PROFISSIONAL vazio, iniciador válido | Apontamento incluído para quem iniciou |
| P-07 | Dois iniciadores com sessões próprias na mesma tarefa | Duração de cada sessão atribuída ao respectivo iniciador |
| P-08 | Entrada manual com datas e iniciador válidos | Incluída sem penalização por ser manual |
| P-09 | Iniciador ausente ou vínculo ambíguo | Pendência de identificação, sem atribuição por suposição |
| P-10 | Mesmo nome em duas pessoas | Não unir automaticamente |
| P-11 | Sessão importada duas vezes | Total inalterado, sem duplicidade de caso ou mensagem |
| P-12 | Tempo de subitem também resumido no pai | Contabilizar a sessão uma única vez |
| P-13 | Consulta Monday completa e vazia; ponto 8h | 8h a menos, e não “falha de integração” |
| P-14 | Fonte falha ou consulta parcial | Comparação não conclusiva; não converter falha em zero |
| P-15 | Ambas sem registros em dia esperado | Verificar ausência de registros; não apresentar conciliação automática |
| P-16 | Relógio sem encerramento | Parcial ou pendente de encerramento, fora do ranking conclusivo |
| P-17 | Duas sessões sobrepostas do mesmo usuário | Alerta de qualidade e soma rastreável; sem correção silenciosa |
| P-18 | Sessão cruza meia-noite; ponto usa jornada de início | Mesma unidade homologada ou pendência; nunca comparação em dias incompatíveis |
| P-19 | Ajuste retroativo muda o Monday | Atualizar o dia efetivo, preservar antes/depois e reavaliar o caso |
| P-20 | Gestor aceita uma justificativa | Encerrar atendimento, preservar diferença numérica |
| P-21 | Item some da listagem ativa | Não zerar horas conhecidas; verificar estado e cobertura |
| P-22 | Diferença de 10min e de 10min01s | Primeiro dentro da tolerância; segundo fora, com precisão visível |
| P-23 | Ponto zero e Monday 2h | Exibir 2h a mais; percentual sobre ponto não calculável |
| P-24 | Dez dias elegíveis esperados, oito comparáveis e seis conciliados | Cobertura 80%; dias conciliados 75% |
| P-25 | Funcionário tenta abrir ou exportar colega | Acesso negado em todas as rotas e relatórios |
| P-26 | Filtros alterados e depois exportação | Arquivo e prévia refletem o recorte; seleção antiga de destinatários é limpa |
| P-27 | Dados mudam depois de caso encerrado | Preservar decisão anterior e sinalizar revisão quando aplicável |
| P-28 | Abono parcial ou jornada especial | Não exigir apontamento de crédito sem trabalho; usar apenas parcela homologada ou sinalizar pendência |

### 13.3 Condições para liberar

Todos os totais da amostra devem ser explicáveis pelas suas parcelas, sem divergência de cálculo não esclarecida. Todas as pessoas do piloto devem ter vínculo confirmado ou exclusão explícita, e todos os dias devem ter situação compreensível. Zero mistura de pessoas, zero duplicação de sessões e zero vazamento de acesso são critérios de bloqueio, não metas médias.

Reexecutar uma carga idêntica não pode mudar os números. Simular falha e recuperação de cada fonte não pode produzir horas artificiais. Testar atualização antiga, exportação filtrada e item arquivado/movido em ambiente apropriado antes de prometer histórico completo.

Metas propostas de experiência: funcionário localizar um dia e entender a diferença em até um minuto; gestor identificar os três maiores casos do período em até dois minutos. Meta inicial de desempenho: consulta mensal de resultados já processados para cerca de 50 pessoas em até três segundos na maior parte das interações, com ambiente e critério de medição aprovados. Essas metas ainda não foram medidas e não se aplicam ao tempo de carga inicial das APIs.

Sucesso do produto é reduzir o tempo de conferência, aumentar a rastreabilidade e permitir corrigir inconsistências. Não é obter artificialmente 100% de conciliação, ocultar diferenças justificadas ou elevar a quantidade de cobranças.

## 14. Orientação para o arquiteto e para o próximo agente

### 14.1 Garantias a preservar na arquitetura

A solução deve manter uma identidade de pessoa confirmada, a origem de cada sessão, o total de ponto utilizado, a unidade de jornada, as regras vigentes, a cobertura de cada fonte e a história das decisões. É necessário recalcular resultados quando a origem muda sem duplicar registros e sem apagar o atendimento anterior.

As consultas dos usuários não devem depender de recuperar todo o quadro a cada abertura. Separar funcionalmente a obtenção dos dados da navegação permite compartilhar resultados, controlar atualização e conservar a última leitura válida. A arquitetura deve justificar como atende a isso e ao controle de acesso, sem assumir que a interface esconde credenciais de forma suficiente.

O contrato das integrações deve ser acompanhado ao longo do tempo. O estudo observou a versão Monday 2026-07, que a documentação consultada identifica como versão corrente; a recomendação do fornecedor é declarar a versão utilizada e planejar sua atualização.[^12] Isso é um cuidado de manutenção, não uma escolha definitiva de versão para uma implementação futura.

### 14.2 Relação com a documentação existente

Para continuar em outro chat, começar pelo [contexto para o próximo agente](contexto-para-proximo-agente.md), que consolida a evolução das decisões, a situação do Git e as referências seguras das integrações. O [índice da documentação](README.md) apresenta a ordem de leitura.

Este plano complementa a [especificação funcional](especificacao-funcional.md) e o [manual de uso proposto](manual.html). Ambos permanecem referências da visão mais ampla; suas telas e regras não representam funcionalidades implementadas. Este novo estudo não reescreve automaticamente os documentos anteriores.

Para preparar a próxima versão da especificação, reconciliar as seguintes mudanças:

- RN-03, D-03 e prompts de conciliação: autoria definida por quem iniciou, sem dependência de PROFISSIONAL; fonte restrita ao Micro Planejamento e seus apontamentos no escopo homologado.
- D-04: leitura básica das duas fontes foi verificada por amostragem; cobertura, exceções e carga integral ainda não foram homologadas.
- RN-05 e interface: uma tolerância simples é a proposta para começar; os antigos exemplos de faixas 10/30 minutos não são uma política aprovada.
- RN-06 a RN-11: conservar separação de qualidade, números e atendimento, e tornar explícitas as limitações de arquivamento, movimentação e dia sem registros.
- RF-03 a RF-10: priorizar tabela, detalhe diário, gráfico individual, exportação e comunicação individual; outras visualizações e automações não precisam entrar primeiro.

Em caso de conflito sobre autoria ou quadro de origem, prevalecem as decisões confirmadas neste plano. As outras mudanças são recomendações a aprovar. Não implementar a documentação antiga isoladamente como se não existisse esta definição posterior.

### 14.3 Instrução pronta para outro agente

> Atue como arquiteto de software na preparação da aplicação de conciliação de horas. Leia este plano e a especificação funcional vinculada. Não escreva código, não configure integrações de produção e não altere dados das fontes nesta etapa.
>
> O objetivo é comparar, por funcionário e dia/jornada, as horas líquidas de ponto do VR Mais com as sessões do quadro EG03_MICRO PLANEJAMENTO. O cadastro de funcionários parte do VR. Cada sessão Monday pertence exclusivamente ao usuário que iniciou o relógio; não use o responsável da atividade, PROFISSIONAL ou quem encerrou como substitutos. Nomes ajudam a encontrar pessoas, mas os vínculos confirmados usam identificadores estáveis.
>
> Proponha uma solução que explique cada total, preserve a cobertura e o histórico, impeça duplicidades e separe dados incompletos de diferenças reais. Não trate a soma de horas como prova de coincidência minuto a minuto. Não amplie a consulta para todos os quadros nem prometa histórico completo sem validá-lo. Ajustes de horas continuam nos sistemas de origem.
>
> Preserve a interface React em WebView2, laranja e cinza, com acesso pessoal e gerencial, tabela de conferência, filtros de período, detalhe diário, gráfico individual, exportação e mensagens internas. Credenciais das fontes não podem ser distribuídas na interface. Funcionário vê somente seus dados; gestor vê o escopo autorizado.
>
> Entregue decisões de arquitetura com justificativas, riscos, dependências, etapas e plano de validação dos cenários P-01 a P-28. Diferencie decisões de produto confirmadas, configurações propostas e capacidades das APIs ainda não homologadas. Comece pelos critérios de passagem das fases 0 e 1. Aguarde autorização específica antes de implementar.

## 15. Fontes e notas de evidência

Referência temporal do estudo: 14 de setembro de 2026. Documentação de fornecedores é mutável; revisões futuras devem verificar o contrato vigente. Exemplos de funcionamento são fictícios e não representam avaliação individual de funcionários. O documento não contém credenciais nem registros pessoais identificáveis.

[^1]: Evidência privada: consultas autorizadas às APIs das contas VR Mais e Monday e reanálise da amostra do quadro EG03_MICRO PLANEJAMENTO, em 14/09/2026. Escopo: cadastro disponível, metadados do quadro, 20 itens principais/37 sessões e consultas curtas de relatório de jornada. Acesso restrito; respostas brutas não foram anexadas ao repositório. Contagens e ocorrências são observações da amostra, não uma auditoria integral. Referências técnicas do quadro examinado: ID `9920862624`; coluna H.GASTA `duration_mksv685w`. Esses identificadores são contexto de integração, não credenciais.

[^2]: monday.com, [Time Tracking](https://developer.monday.com/api-reference/reference/time-tracking), documentação da API, consultada em 14/09/2026. Fundamenta os campos de sessão, o usuário iniciador e o total acumulado da coluna; não define por si só a política de atribuição da empresa.

[^3]: VR/Pontomais, [Documentação da API no Postman](https://documenter.getpostman.com/view/4785048/RWMCvVxN?version=latest#intro), seções “Colaboradores / Listar”, “Relatórios / Jornada” e “Relatórios / Registros de ponto”; coleção pública publicada e indicada pela [Central de Ajuda oficial](https://materiais.vr.com.br/central-de-ajuda/extensao-api/), consultada em 14/09/2026. Descreve campos, filtros e extração de relatórios. Não substitui a homologação da semântica de horas totais e da paginação na conta específica.

[^4]: monday.com, [The Time Tracking Column](https://support.monday.com/hc/en-us/articles/360001143809-The-Time-Tracking-Column), seções sobre histórico, subitens, resumo no item pai e exportação, consultada em 14/09/2026. Fundamenta o cuidado com totais resumidos e a possibilidade de conferência pelo histórico da origem.

[^5]: VR/Pontomais, [Gestão de solicitações: o que são e como gerenciar ajustes e abonos?](https://materiais.vr.com.br/central-de-ajuda/solicitacoes-de-ajuste/), Central de Ajuda, consultada em 14/09/2026. Fundamenta a distinção operacional entre ajustes e abonos; não fundamenta uma fórmula universal para o total utilizado na comparação.

[^6]: monday.com, [Items page](https://developer.monday.com/api-reference/reference/items-page), seção “Archived and deleted items”, consultada em 14/09/2026. Fundamenta a limitação da listagem ativa e a necessidade de identificadores para a consulta específica de itens arquivados/excluídos.

[^7]: monday.com, [Rate limits](https://developer.monday.com/api-reference/docs/rate-limits), consultada em 14/09/2026. Fundamenta a necessidade de medir carga e respeitar limites; a frequência de 30 minutos é proposta deste plano, não recomendação universal do fornecedor.

[^8]: monday.com, [Next items page](https://developer.monday.com/api-reference/reference/next-items-page), consultada em 14/09/2026. Documenta paginação e validade dos cursores; não comprova que a paginação do quadro inteiro foi executada no estudo.

[^9]: Microsoft, [Develop secure WebView2 apps](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security), consultada em 14/09/2026. Fundamenta restrição de navegação, validação de origem e cuidado com a interação entre conteúdo web e aplicação nativa.

[^10]: monday.com, [Authentication](https://developer.monday.com/api-reference/docs/authentication), consultada em 14/09/2026. Fundamenta que tokens pessoais refletem as permissões do usuário na plataforma; não comprova abrangência total da credencial examinada.

[^11]: VR/Pontomais, [Extensão API/Webhooks: requisitos e documentação do desenvolvedor](https://materiais.vr.com.br/central-de-ajuda/extensao-api/), Central de Ajuda, consultada em 14/09/2026. Fundamenta a disponibilidade de integração e a diferença de abrangência entre formas de credencial. Valores contratuais e contratação não foram objeto do estudo.

[^12]: monday.com, [Versioning](https://developer.monday.com/api-reference/docs/api-versioning), consultada em 14/09/2026. Identifica 2026-07 como versão corrente na data e recomenda declarar a versão das chamadas. O plano não presume que ela continuará corrente na data da futura implementação.
