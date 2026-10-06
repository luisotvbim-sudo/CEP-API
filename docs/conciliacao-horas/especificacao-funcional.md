# CEP Horas — especificação funcional

Revisão de contexto: 04/10/2026. Base de código conferida: CEP-API `b36c6e149b42253b44860d98c6ffe44f98c53dd6`. Esta revisão preserva os IDs da visão original e incorpora as regras aprovadas em 29/09 e os contratos de energia. Descreve implementação e requisitos futuros separadamente; não comprova versão implantada, homologação das fontes ou funcionamento em uma máquina Windows.

O [contrato de análises e notificações](contrato-analises-notificacoes.md) registra as regras aprovadas. O [contexto atual da API](../CONTEXTO-ATUAL.md) e os contratos especializados descrevem como foram implementadas. [Decisões e pendências](../DECISOES-PENDENCIAS.md) conserva o que ainda exige definição ou validação. Para alterar comportamento, consulte o código e o OpenAPI da base de entrega.

## 1. Objetivo e alcance

CEP Horas compara horas de atividades Monday com a jornada do Ponto VR Mais, por pessoa e período. O membro confere seus registros; o líder acompanha somente seu escopo; o coordenador administra pessoas, times e integrações de sua organização. A interface React atende navegador e host WPF/WebView2. O produto compara registros: diferença não comprova ausência, produtividade ou irregularidade, nem equivale automaticamente a débito ou hora extra oficial.

A visão original considera aproximadamente 50 membros como referência de planejamento, sem comprovar o volume ou a frota atual. Metas de desempenho para consultar um mês desse escopo devem ser definidas e medidas com dados reais autorizados em D-13.

Correções são realizadas nas fontes. CEP Horas lê, normaliza, calcula e apresenta resultados sem alterar batidas ou tarefas. Jornada prevista, banco de horas, folha, escrita nas fontes, aplicativo móvel e operação offline de conciliação não estão definidos como capacidades atuais. Grants offline dos plugins são outro contrato.

### Estado das capacidades

**Implementado** significa encontrado no código analisado; **parcial** indica uma base existente com partes futuras; **planejado** conserva requisitos úteis da visão do produto, sem representar aprovação de uma nova entrega.

| Capacidade | Estado |
|---|---|
| Autenticação web/nativa, convites, recuperação, revogação e isolamento por organização | Implementado |
| Organizações, usuários, produtos, times e vínculos temporais de membro/líder | Implementado |
| Diretórios Monday/VR, associação por IDs, convite e ativação da pessoa | Implementado |
| Sincronização normal de 17 dias e inicial/full administrativa de 90 dias | Implementado; cobertura real exige homologação |
| Histórico bruto e resumo diário importado | Implementado; não certifica cobertura ou atualidade |
| Motor diário/semanal/sprint, diferença, tolerância e ocorrências de integridade | Implementado |
| Configurações/agendas globais, relatórios persistidos, envios e caixa individual | Implementado; envio automático nasce desativado |
| Verificação de energia e PIN administrativo temporário | Implementado na API; execução Windows pertence ao Front |
| Calendário completo, férias, feriados, escalas e classificação leve/crítica | Planejado |
| Casos, pedidos, justificativas, decisões, prazos e reabertura | Planejado |
| Ranking de gestão, gráficos/indicadores avançados e exportação planilha/PDF | Planejado ou parcial como apresentação; não há contrato completo dessas capacidades na API |

## 2. Perfis e acesso

| Perfil | Representação | Acesso implementado |
|---|---|---|
| Coordenador | `OrganizationAdmin` | Administração e consulta da própria organização |
| Líder | `User` com vínculo `Manager` | União dos times ativos que lidera hoje, pessoas vinculadas hoje e próprios dados |
| Membro | `User` com vínculo `Member` | Próprios dados com vínculo vigente |
| Administração técnica | `SystemAdmin` | Administração global; seleciona `organizationId` explicitamente nas operações organizacionais |

Os vínculos são recalculados no servidor a cada requisição, com datas inclusivas e dia de negócio em São Paulo. Usuário sem vínculo vigente pode receber pessoas, times ou histórico vazios. O líder atual pode consultar registros anteriores à transferência; o líder anterior perde esse acesso. Essa é a autorização implementada, não uma atribuição histórica das horas ao time da data trabalhada. A política futura está em D-05.

Configurações e agendas são globais, mas pessoas, relatórios, envios e destinatários permanecem isolados por organização. `OrganizationAdmin` não pode escolher outra organização; `SystemAdmin` precisa selecionar uma para operações organizacionais. As rotas pessoais de energia e caixa individual não aceitam escolher pessoa ou organização.

## 3. Regras de negócio

### RN-01 — Unidade, data e duração

Calcular em segundos e apresentar duração sem confundir `07h30` com 7,30 horas. Totais podem ultrapassar 24 horas. O dia civil usa `America/Sao_Paulo`; instantes persistidos são UTC. Intervalos usam início inclusivo e fim exclusivo. Cada execução captura um único corte para ambas as fontes. Semana começa na segunda-feira; sprint começa no dia 1 ou 15 e termina no corte atual. Não existem jornadas após meia-noite no contrato aprovado.

### RN-02 — Total de ponto

Em dia fechado, o motor usa o total oficial VR importado, incluindo ajustes, e valida a integridade das batidas disponíveis. Não desconta intervalos novamente. Batidas ímpares/inconsistentes, ausência ou múltiplos registros diários impedem conclusão. No dia atual, conta intervalos fechados válidos e eventual entrada aberta até o corte; entrada aberta durante o expediente não é automaticamente erro.

### RN-03 — Total Monday

As sessões pertencem ao profissional único do item/subitem, não ao iniciador do timer nem à coluna R.T. O subitem usa profissional próprio quando preenchido e herda do pai quando ausente. Vários profissionais impedem atribuição presumida; não multiplicar ou dividir horas sem regra aprovada. O conector prioriza a coluna `PROFISSIONAL`, depois uma única coluna de responsável ou a única coluna Pessoa; ambiguidade provoca falha explícita.

O motor deduplica por fonte, identidade e chave externa usando a versão mais recente. Sessões de chaves distintas são somadas, mesmo se simultâneas: a implementação não reúne seus intervalos numa duração única nem emite ocorrência específica de sobreposição. A regra adicional de sobreposição e categorias/boards permanece em D-03. Timer em andamento é contado até o corte atual; atravessar fechamento diário gera ocorrência e impede diferença conclusiva naquele dia.

### RN-04 — Convenção da diferença

`Diferença = Monday − VR`. Negativa significa menos horas no Monday; positiva, mais horas no Monday. Mostrar sinal e descrição. Zero significa totais conhecidos iguais. Diferenças aceitas por eventual justificativa futura deverão conservar os valores originais.

### RN-05 — Tolerância

Tolerância global inicial de **30 minutos**, simétrica. Comparar segundos sem arredondar antes do limite: ocorrência `above_tolerance` somente quando `abs(diferença) > tolerância × 60`. O limite exato é permitido e a diferença permanece visível. A configuração tem versão; relatórios emitidos conservam a versão aplicada. Faixas leve/crítica, tolerância por time e reprocessamento retroativo não estão implementados.

### RN-06 — Ausência, zero e qualidade

Dado desconhecido permanece `null`. Falha de fonte não é zero. Em leitura ao vivo completa, Monday sem sessões pode ser zero; VR ausente não é zero presumido. No histórico importado, ausência de registros Monday não comprova cobertura e mantém total nulo. Separar incompleto, provisório e diferença confirmada; nenhuma mensagem de coerência pode derivar de fontes incompletas.

### RN-07 — Calendário e exceções

Calendário de feriados, férias, afastamentos, folgas, escalas e dias dispensados é planejado. Não interpretar ausência VR como folga nem excluir automaticamente dias do cálculo. A regra atual exclui sábado/domingo apenas dos avisos automáticos; relatórios diários continuam sendo processados e envios manuais não têm essa restrição. Reuniões, treinamento e atividades internas precisam de política própria antes de mudar elegibilidade.

### RN-08 — Resultado, qualidade e tratamento

A implementação retorna totais, `partial`, qualidade das fontes e ocorrências como `incomplete`, `odd_punches`, `running_timer` e `above_tolerance`. Uma ocorrência não é um caso com workflow. A visão futura separa qualidade/aplicabilidade, resultado numérico e tratamento (identificada, aguardando membro/líder, resolvida por correção, encerrada com justificativa, reaberta). Não apresentar esses estados futuros como disponíveis.

### RN-09 — Métricas do período

Total VR só existe se todos os dias tiverem VR calculável; o mesmo vale individualmente para Monday. Saldo e divergência absoluta do período só existem quando todos os dias têm diferença calculável. Não apresentar subtotal dos dias válidos como saldo completo. Divergência absoluta soma magnitudes diárias: −02h00 e +02h00 geram saldo zero e divergência absoluta 04h00.

Taxa de dias conciliados, cobertura percentual, percentual de divergência, pendências de tratamento e totais comparáveis parciais são métricas planejadas, sem DTO completo atual. Quando implementadas, devem informar base e denominador; sem denominador válido, serão não calculáveis. Justificativa aceita não deverá alterar taxa numérica de conciliação.

Fórmulas preservadas para essas métricas futuras:

| Métrica planejada | Fórmula/base |
|---|---|
| Taxa de conciliação | Dias conciliados ÷ dias elegíveis, completos e encerrados × 100 |
| Cobertura dos dados | Dias elegíveis, completos e encerrados ÷ dias elegíveis encerrados esperados × 100 |
| Percentual diário da diferença | Diferença diária ÷ VR daquele dia × 100, somente se VR > 0 |
| Percentual de divergência do período | Soma das diferenças absolutas ÷ soma VR dos mesmos dias calculáveis × 100, somente se denominador > 0 |
| Pendências de tratamento | Casos abertos, separados por responsável e vencimento |

Informar taxa de dias conciliados, sem chamá-la percentual de horas conciliadas. A base comparável futura deve ser separada dos totais recebidos quando houver lacunas; não subtrair conjuntos de datas diferentes como saldo certificado. Essas fórmulas não alteram a regra atual de agregação que exige todos os dias.

### RN-10 — Ranking e curvas

Ranking futuro deve priorizar divergência absoluta, com cobertura e quantidade de dias analisados, evitando cancelamento entre sinais. Curvas da pessoa devem apresentar Monday e VR separadamente, lacunas sem preenchimento zero e detalhes acessíveis por teclado. Evitar dezenas de curvas sobrepostas. Essas visualizações não medem produtividade e não autorizam cálculo no React.

### RN-11 — Atualização e preservação

Sincronização normal reconsulta hoje e seis dias anteriores, limitada ao escopo; inicial/full administrativa cobre 90 dias e diretórios. Não é atualização apenas por cursor. Falha de uma fonte não apaga a última importação válida da outra. Retenção física de registros importados ocorre após sucesso da respectiva fonte. Tentativa recente com erro não prova atualização.

Relatórios são snapshots imutáveis: GET análises não consulta fontes. Novo processamento gera nova análise. Workflow de antes/depois, preservação de justificativas e reabertura por alteração material é planejado. Idempotência da importação, dos pedidos de envio e da entrega não equivale a esse workflow.

### RN-12 — Dia atual, fechamento e prazos

Dia atual é parcial. A API usa corte real também para decisões de energia; `partial=true` sozinho não impede liberação quando há resultado calculável dentro da tolerância. Relatório diário verifica o dia civil anterior. Prazos de lançamento, fechamento mensal, bloqueio de competência e reabertura dependem de D-06/D-07; não simulá-los.

## 4. Requisitos funcionais

| ID | Requisito e aceite | Estado |
|---|---|---|
| RF-01 | Entrada autenticada, organização e escopo explícitos. Membro não acessa colegas; líder não acessa outros times; seleção anterior deve ser descartada ao trocar contexto. | Implementado; aprovação de justificativa própria é restrição do workflow futuro |
| RF-02 | Associar identidade Monday e VR ativa da mesma organização por IDs únicos; convites de 48h, reenvio administrativo e ativação sem sessão. Nomes iguais não podem misturar dados. | Implementado |
| RF-03 | Visão de gestão com indicadores, ranking, cobertura e acesso ao detalhe individual no escopo. | Parcial: relatórios autorizados existem; ranking/indicadores avançados planejados |
| RF-04 | Meus times: tabela operacional de pessoas no escopo, busca, ordenação, totais/diferenças, pendências e seleção para exportação, sem revelar terceiros. Cadastro de times, vínculos/vigências e união dos times liderados são sua fundação atual. | Cadastro/consulta de times e pessoas implementados; tabela comparativa/seleção para exportação planejadas |
| RF-05 | Detalhe da pessoa/dia com registros seguros, origem, totais conhecidos, corte, qualidade e motivos. Dado importado e análise ao vivo devem ser identificados. | Implementado nos contratos de histórico/relatórios |
| RF-06 | Área pessoal com histórico, notificações e relatórios permitidos; atualizar próprios dados sem ampliar escopo. | Implementado; resposta a casos planejada |
| RF-07 | Central de casos: solicitar ajuste/justificativa, responder, aprovar/devolver, resolver por correção e reabrir, com autoria, motivo e histórico. Ninguém aprova o próprio caso. | Planejado |
| RF-08 | Filtros de pessoa/período/fonte/ocorrência e paginação; mudanças de contexto descartam seleções ocultas. Filtros avançados e favoritos mantêm a mesma interseção em telas e exportações. | Implementado nos parâmetros publicados; extensões planejadas |
| RF-09 | Caixa individual, avisos automáticos e manuais com análise individual; enfileirada, recebida e lida são estados diferentes. Recuperar todas as páginas pendentes após reconexão; leitura não resolve ocorrência. | Implementado; prazos/escalonamento de casos planejados |
| RF-10 | Relatórios no escopo e exportação planilha/PDF com período, regra, procedência e ressalvas; membro exporta apenas próprios dados. | Relatórios persistidos implementados; exportações planejadas |
| RF-11 | Administração de organizações, pessoas, times, integrações, tolerância/agendas globais e auditoria; conflitos de versão não sobrescrevem alteração concorrente. | Implementado; calendário e exceções planejados |
| RF-12 | Atualização 7/90, estado por fonte e lote, concorrência controlada e reimportação idempotente. Fonte indisponível não certifica resultado. Não há endpoint para cancelar um job de sincronização. | Implementado; cobertura/semântica real requer homologação |
| RF-13 | Histórico importado, relatórios com versão e auditoria administrativa protegidos pelo escopo atual. Decisões futuras não apagam resposta anterior. | Parcial: dados/auditoria existentes; trilha de casos planejada |

## 5. Análises, agenda e avisos

Configurações e agendas são globais e editáveis por administradores autorizados, com versão e auditoria. O envio automático nasce desativado. Mensagem não escolhe regra por interpretação de texto.

| Momento local | Comportamento |
|---|---|
| Processamento diário | Relatório do dia anterior, inclusive fim de semana, sem popup; batidas ímpares, timer atravessando fechamento e diferença acima da tolerância quando calculável |
| 10h, dias úteis | Reavalia apenas o dia civil anterior e avisa problemas confirmados; segunda considera domingo, sem pendências antigas |
| 11h50, dias úteis | Compara o dia até o corte; orienta ajuste ou informa coerência parcial e pausa próxima ao almoço |
| 17h, dias úteis | Mesma comparação parcial, com mensagem de fim de expediente |
| Envio manual | Administrador seleciona pessoa ou todos do escopo e diário/semanal/sprint; prévia não envia e quantidade é revalidada no processamento |

O worker usa exclusão mútua PostgreSQL e fila persistida. Pedido manual tem `requestId`; mesma chave/conteúdo reutiliza o pedido, conteúdo diferente gera conflito. Cada pessoa recebe no máximo uma notificação consolidada por execução, com resultado e aviso persistidos atomicamente. Intervalo mínimo manual de um minuto por organização e rate limiting continuam no servidor.

O agendador recupera slots devidos do mesmo dia preservando corte; não reconstrói dias passados nunca enfileirados. Habilitação/alteração não recria horários anteriores à mudança. Notificações já geradas são recuperadas integralmente pelo cliente, com horário original e indicação de atraso. Recebimento deve ser confirmado após registro local persistente; ler é operação distinta.

## 6. Energia e integração nativa

A API decide `allowed`, `blocked` ou `indeterminate` para `shutdown`, `restart` e `hibernate`, somente para o usuário autenticado. Override administrativo válido retorna `allowed` com `analysis: null` antes de consultar fontes. Sem override ativo, o fluxo normal usa análise ao vivo no dia atual; pré-requisitos inválidos, como pessoa não associada ou identidade inativa, podem impedir a consulta e resultar em `indeterminate`. Histórico importado e relatório anterior não autorizam ação. WPF revalida a decisão e o serviço Windows executa. HTTP de erro, resposta incompleta e regra negada não significam transporte offline.

PIN global dedicado de seis dígitos é provisionado no servidor sem eco, guardado como hash forte e limitado por conta/organização/global. Sucesso abre janela individual de cinco minutos para as três ações; rotação, mudança de segurança/organização e expiração invalidam a janela. O PIN nunca é enviado ao serviço Windows nem armazenado pelo React. Consulte [energia](../power-action-check.md) e [desbloqueio](../power-admin-unlock.md).

## 7. Experiência e qualidade

Navegação lateral, tabelas compactas, filtros visíveis e detalhes por pessoa/dia; identidade laranja/cinza, texto junto à cor e foco acessível. Carregamento, vazio, restrição de acesso, fonte parcial e falha devem ter estados distintos. Preservar filtros seguros quando possível, sem reter seleções entre organizações. Fixtures devem ser identificadas e isoladas; não simulam operação real.

Jornadas atuais: coordenador prepara organização, importa diretórios, associa pessoas e convida; membro ativa conta, entra e confere histórico/avisos; líder consulta pessoas e relatórios no escopo atual. A jornada futura de tratamento adiciona solicitação, resposta, decisão e reabertura. Estados previstos: identificada → aguardando membro → aguardando líder → encerrada com justificativa/resolvida por correção; devolução retorna à resposta e mudança material pode exigir reabertura. Esse fluxo não foi implementado.

### Aceites detalhados preservados para evoluções

- **RF-03/RF-04/RF-05:** painel futuro usa mesma interseção de período/escopo em cards, gráfico e tabela; cada contagem informa sua unidade. Ranking abre detalhe com período preservado. Seleção distingue página e todos os resultados, sem incluir pessoas invisíveis. Detalhe explica parcelas elegíveis/exclusões e procedência; campo ausente fica indisponível e referência à origem só aparece quando utilizável. Jornada prevista é referência adicional planejada, não fonte atual de horas calculadas.
- **RF-06/RF-07/RF-13:** calendário pessoal futuro distingue dia dispensado de dia sem dados. Toda mudança de caso tem autor/data; pedido explica motivo e próximo passo, devolução exige motivo, resposta fica visível às partes autorizadas e decisão preserva respostas anteriores. Aceitar justificativa não edita fontes nem diferença; ninguém aprova o próprio caso. Reabertura exige motivo e critério definido. Clique repetido não duplica resposta. Comentários ficam no caso; anexos e delegação são evoluções dependentes de decisão.
- **RF-08:** critérios diferentes combinam com E; escolhas dentro de um mesmo filtro combinam com OU. Chips explicitam filtros, limpar retorna ao padrão informado e datas inclusivas são validadas. Critérios futuros incluem qualidade, motivo, gravidade, tratamento, direção/faixa, presença de justificativa, responsável e prazo. Favoritos/compartilhamento revalidam o acesso de quem abre, sem restaurar time perdido. Redimensionamento, teclado, foco e retorno do detalhe conservam contexto seguro.
- **RF-09:** notificações futuras de casos mostram destinatário/contexto antes do envio, oferecem acesso ao conteúdo ainda autorizado e não confundem leitura com resposta. Lembretes cessam após encerramento; alteração material pode avisar sem gerar mensagem para cada importação bem-sucedida. Preferências, marcar todas como lidas, prazos/escalonamento e aviso de exportação pronta dependem de contratos próprios.
- **RF-10:** exportação futura tem prévia de tipo, período, filtros, pessoas/linhas, colunas, ordenação, agrupamento e totais. Arquivo informa geração, autoria, fontes, atualização/cobertura, versão das regras, unidades e sinais. Preserva filtros/permissões/ordenação, acentos e durações acima de 24h; indisponível não vira zero. Avisar mudança dos dados desde a prévia, explicar ausência de resultados, oferecer retentativa em falha e indicar destino/abertura no desktop. PDF/impressão, decisões/comentários e comparação de períodos seguem sem contrato completo; diferença não recebe rótulo de hora extra.
- **RF-11/RF-12:** calendário/exceções futuros validam motivo, vigência, alcance e datas; regras por time/jornada exigem nova definição compatível com configuração global vigente. Estado por fonte distingue tentativa/sucesso/cobertura; rejeitados/duplicados e registros não associados precisam de representação segura, sem inventar contadores que o DTO não publique.

## 8. Cenários de aceite preservados

Exemplos usam tolerância inicial de 30 minutos e fontes completas, salvo ressalva. A coluna estado distingue verificações implementadas de aceite futuro; não registra execução de testes nesta revisão documental.

| ID | Entrada/ação | Resultado esperado | Estado |
|---|---|---|---|
| CA-01 | VR 08h00, Monday 08h00 | Diferença zero conhecida | Implementado |
| CA-02 | VR 08h00, Monday 07h30 | −00h30 dentro do limite exato, diferença preservada | Implementado |
| CA-03 | VR 08h00, Monday 07h40 | −00h20 dentro da tolerância; não existe faixa leve atual | Implementado |
| CA-04 | VR 08h00, Monday 06h30 | −01h30 acima da tolerância, a menos no Monday | Implementado |
| CA-05 | VR 08h00, Monday 08h45 | +00h45 acima da tolerância; não chamar de hora extra | Implementado |
| CA-06 | Dois dias completos −02h00/+02h00 | Saldo zero e divergência absoluta 04h00 | Implementado |
| CA-07 | Fonte VR falhou, Monday 08h00 | Diferença indisponível, sem presumir VR zero | Implementado |
| CA-08 | Consulta Monday ao vivo completa e vazia, VR 08h00 | Monday zero e −08h00; histórico vazio não comprova zero | Implementado |
| CA-09 | VR explicitamente válido zero, Monday 02h00 | +02h00; percentual futuro exige denominador válido | Parcial |
| CA-10 | Calendário futuro dispensa dia sem registros | Não aplicável, excluído da base por regra explícita | Planejado |
| CA-11 | Líder aceita justificativa de membro | Encerrar tratamento preservando diferença original | Planejado |
| CA-12 | Correção Monday de 06h30 para 08h00 | Nova análise muda diferença; resolver caso com antes/depois é futuro | Parcial |
| CA-13 | Reimportação/pedido/entrega repetidos | Não duplicar registros nem a mesma operação; casos futuros precisam regra própria | Implementado nos mecanismos atuais |
| CA-14 | Pessoas com mesmo nome | Associação por IDs, sem misturar horas | Implementado |
| CA-15 | Dia atual em andamento | Marcar parcial e usar corte; ranking conclusivo futuro exclui provisórios | Parcial |
| CA-16 | Dez dias, oito calculáveis | Agregado que exige todos os dias fica nulo; taxas 75%/80% são cenário futuro, não DTO atual | Parcial |
| CA-17 | Nenhum dia comparável | Saldo/diferença indisponíveis; taxa futura não calculável | Parcial |
| CA-18 | Membro tenta exportar time | Exportação futura mantém somente escopo próprio | Planejado |
| CA-19 | Período + time + ocorrência | Usar mesma interseção autorizada; relatório não recalcula fontes | Parcial |
| CA-20 | Seleção seguida de novo contexto/período | Descartar destinatários ocultos e confirmar escopo antes de envio | Aceite de interface |
| CA-21 | Líder A consulta pessoa exclusiva de B | Não revelar dados de B | Implementado |
| CA-22 | Pessoa lidera A e B, não C | União A/B e próprios dados, sem C | Implementado |
| CA-23 | Vínculo de líder encerrado | Acesso segue vínculos vigentes hoje; atribuição histórica aguarda D-05 | Implementado com limite explícito |
| CA-24 | Coordenador consulta outra organização | Própria organização permitida; outra negada | Implementado |

## 9. Decisões D-01 a D-13

| ID | Definição atual | Parte pendente |
|---|---|---|
| D-01 | 30 minutos simétricos, segundos e versão global | Classificações adicionais, vigência/reprocessamento retroativo |
| D-02 | São Paulo, dias civis, sem jornada após meia-noite | Homologar travessias reais sem habilitar jornada noturna por suposição |
| D-03 | Profissional Monday, herança e deduplicação por chave | Sobreposição entre sessões, categorias/boards e atividade compartilhada |
| D-04 | Adaptadores/importação existentes | Homologar significados, campos e cobertura com fontes reais |
| D-05 | Sessão web/nativa, múltiplos vínculos, autorização vigente hoje | Política após transferência e atribuição histórica por time |
| D-06 | Janela 7/90, corte único e agendas | Prazo de lançamento, atualidade máxima e execução perdida em dias anteriores |
| D-07 | Relatórios snapshots, nova análise em novo processamento | Casos, reabertura, competência e preservação/revisão de decisões |
| D-08 | Sem calendário completo; automático exclui sábado/domingo | Feriados, ausências, escalas e elegibilidade de reuniões/treinamento |
| D-09 | Agendas iniciais, caixa pessoal e recuperação de pendentes | Prazos, lembretes de casos, delegação/escalonamento e mensagem livre sem análise |
| D-10 | Relatórios persistidos | Colunas/formatos de planilha, PDF, comentários e impressão |
| D-11 | Retenção móvel de importados por 90 dias | Retenção de relatórios/avisos/auditoria/casos/anexos e backup externo |
| D-12 | Sessão nativa protegida, bridge explícito e notificações | Download/impressão de relatórios e homologação Windows por versão |
| D-13 | Limites de paginação/concurrency e CI | Metas medidas de tempo, disponibilidade e carga real, usando um mês de aproximadamente 50 pessoas como referência inicial de planejamento |

As pendências não autorizam ampliar produto por inferência. Planejar entrega com objetivo, contrato e aceite definidos, registrar decisões no repositório e conferir implementação no código. Nenhum contexto necessário deve permanecer somente em chat.
