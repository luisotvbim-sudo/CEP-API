# Contexto do chat para continuidade da conciliação de horas

## 1. Resumo para começar

Esta passagem preserva o contexto relevante até 14/09/2026. Não é uma transcrição literal: credenciais, dados pessoais e respostas brutas das APIs foram omitidos deliberadamente.

O responsável pelo produto pediu uma aplicação simples para relacionar as horas de ponto do VR Mais com as horas registradas no Monday. Funcionários conferem seus próprios dados; gestores acompanham equipes, identificam diferenças, filtram períodos, exportam relatórios e enviam notificações internas.

Decisões finais:

- Cadastro de funcionários vindo do VR Mais, trazendo todos os disponíveis no escopo autorizado.
- Fonte Monday: **EG03_MICRO PLANEJAMENTO**, chamado na conversa de “Micro Planejamento”.
- Dono de cada sessão: **quem deu start no relógio**, identificado por `started_user_id`.
- O responsável atual da atividade, a coluna PROFISSIONAL e quem encerrou o cronômetro não substituem esse usuário.
- Não procurar uma atividade específica para aferição; a atividade explica onde o tempo foi registrado.
- O nome ajuda a encontrar a pessoa entre as fontes. O plano recomenda confirmar a correspondência e preservá-la pelos identificadores estáveis.
- Interface React incorporada ao desktop por WebView2, simples, inspirada na organização do Monday, em laranja e cinza.
- Aproximadamente 50 funcionários esperados; abrangência real ainda precisa ser confirmada.
- **Neste chat foi pedido estudo e planejamento, sem codificar a aplicação.**

O próximo agente deve continuar desse ponto, sem reiniciar a descoberta ou tratar hipóteses antigas como decisões atuais.

## 2. Evolução das decisões

| Etapa | Pedido ou hipótese | Situação atual |
|---|---|---|
| Definição inicial | Atuar como gerente de produto, pensar nas funcionalidades e criar prompts para chats de programação | Priorizar comportamento, interface e critérios de aceite; não impor programação desnecessária |
| Documentação | Criar especificação e manual HTML para arquiteto de software | Entregues; referências na seção 3 |
| Design | Mudar as cores para laranja e cinza | Confirmado e aplicado aos documentos iniciais |
| Continuidade | Deixar claras as instruções para outro agente | Prompts na especificação, no plano e nesta passagem |
| Origem inicial | Usar “micro planejamento” | Quadro identificado como EG03_MICRO PLANEJAMENTO |
| Hipótese intermediária | Buscar em tudo do Monday onde os funcionários apontam horas | Houve exploração mais ampla, mas essa abrangência foi substituída pelo Micro Planejamento |
| Autoria intermediária | Investigar responsável da atividade, PROFISSIONAL e autor do lançamento | Encerrada pela decisão explícita: o identificador é quem deu start |
| Estudo aprofundado | Propor cenário ideal antes de desenvolver | Plano entregue; não houve implementação neste chat |
| Publicação | Subir o plano em outra branch no GitHub | Publicado em docs/plano-conciliacao-horas; este contexto acompanha a mesma branch |

Não voltar a perguntar qual quadro usar ou se a hora pertence ao responsável da atividade. Se uma sessão não tiver iniciador, tratar dado incompleto, sem mudar silenciosamente a regra. A recuperação de histórico movido para fora do Micro Planejamento exige decisão própria de escopo; não retomar a busca global apenas porque ela apareceu antes.

## 3. Arquivos e prioridade

Todos estão em `docs/conciliacao-horas/` no repositório CEP-API.

| Arquivo | Finalidade |
|---|---|
| [README.md](README.md) | Entrada e ordem de leitura |
| [contexto-para-proximo-agente.md](contexto-para-proximo-agente.md) | Decisões do chat, estado e passagem de trabalho |
| [plano-produto-estudo.md](plano-produto-estudo.md) | Proposta atual após estudo, alternativas, regras, experiência, fases, fontes e 28 cenários P-01 a P-28 |
| [especificacao-funcional.md](especificacao-funcional.md) | Visão inicial: 13 requisitos RF, 12 regras RN, 20 cenários CA, decisões D e prompts |
| [manual.html](manual.html) | Manual offline da experiência proposta, por perfil, com dados fictícios |

As instruções explícitas mais recentes do responsável prevalecem. Entre os documentos, decisões confirmadas neste contexto e no plano prevalecem sobre hipóteses antigas da especificação/manual. As demais recomendações continuam propostas, não políticas aprovadas por silêncio.

O plano não reescreveu integralmente os documentos iniciais. Sua seção 14.2 indica os alinhamentos necessários: origem Monday, atribuição, situação das integrações, tolerância e prioridade da primeira versão. A especificação contém o prompt mestre e oito prompts temáticos na seção 12; o plano tem instrução para arquitetura na seção 14.3.

## 4. O que foi feito — e o que não foi

Foram criados documentos de produto, manual HTML proposto e estudo de viabilidade. Foram feitas leituras autorizadas das APIs com credenciais fornecidas pelo responsável, sem criar ou alterar funcionários, atividades, batidas ou apontamentos nas fontes.

As consultas demonstraram leitura de cadastro, metadados de quadro, sessões e relatório de jornada. Não houve conciliação completa de um mês ou de toda a equipe, teste integral de desempenho, homologação de todas as exceções ou liberação do produto para funcionários.

Não foi criada a aplicação de conciliação neste chat. O repositório já contém uma API e há trabalho de backend em outra branch; não foi auditado aqui quanto desse trabalho atende ao plano. Presença de código no repositório não comprova implementação e validação deste produto.

As respostas privadas ficaram em memória durante o estudo. Um novo agente não deve pressupor acesso às variáveis ou sessões de ferramentas anteriores. Os achados estão preservados nos documentos; uma nova homologação pode exigir novas leituras autorizadas.

## 5. Evidências das contas, sem dados pessoais

Observações pontuais em 14/09/2026, por amostragem, não indicadores de produção. A amostra de itens não foi aleatória.

| Observação | Resultado | Limite |
|---|---|---|
| Funcionários ativos VR retornados | 32 | Conferir população e permissões antes de afirmar cobertura das aproximadamente 50 pessoas |
| Consulta de inativos | Nenhum retorno | Não prova inexistência de inativos ou histórico fora da abrangência acessível |
| Usuários Monday habilitados retornados | 43 | Não são automaticamente funcionários elegíveis |
| Nome completo normalizado entre fontes | 23 sugestões únicas entre 32 funcionários VR | Nenhum vínculo persistente criado; nove sem essa correspondência exata |
| Quadro principal | 3.777 itens informados nos metadados | Contagem não representa número de sessões nem histórico integral |
| Estrutura de subitens | 85 itens | Coleta e cobertura ainda precisam de validação |
| Amostra principal | 20 itens e 37 sessões | Não extrapolar percentuais para a equipe |
| Iniciador presente | 37 das 37 sessões | Suporte à regra na amostra, não garantia universal |
| Início e fim presentes | 36 das 37 sessões | Uma sessão sem encerramento precisa de tratamento de parcialidade |
| Algum dado de início/fim manual | 17 sessões | Manual não significa inválido |
| Iniciador e encerrador diferentes | 3 sessões | A duração permanece com o iniciador |
| Atividades com sessões e PROFISSIONAL vazio | 2 | Não bloqueiam autoria quando o iniciador é conhecido |
| Mais de um iniciador na mesma atividade | Uma atividade na amostra | Somar sessões por pessoa, sem repartir ou replicar o acumulado do item |
| Relatório VR curto | Retorno bem-sucedido com total e batidas | Não valida todas as exceções |
| Variante paginada do relatório | HTTP 500 | Não comprova funcionamento da carga integral nem causa do erro |

Na hipótese anterior de busca ampla, foram lidos metadados e pequenas amostras de outros quadros, identificando controles de tempo fora do Micro Planejamento. Esses resultados não compõem a base atual nem autorizam expansão; servem como alerta para possíveis lacunas históricas por movimentação.

## 6. Referências práticas das integrações

São pistas para evitar repetir a descoberta básica, não código pronto nem garantia de contrato futuro. Fontes e ressalvas completas estão na seção 15 do plano.

### 6.1 Monday

| Referência | Informação observada |
|---|---|
| API | `https://api.monday.com/v2`, consultas GraphQL de leitura |
| Autenticação | Cabeçalho `Authorization`; valor não incluído |
| Versão das respostas no estudo | `2026-07`; verificar a versão adequada ao retomar |
| Quadro | `EG03_MICRO PLANEJAMENTO`, ID `9920862624` |
| Estrutura de subitens | `Subelementos de EG03_MICRO PLANEJAMENTO`, ID `9920862951` |
| Coluna principal | `H.GASTA`, ID `duration_mksv685w`, tipo `time_tracking` |
| Dono da sessão | `history.started_user_id` |
| Campos úteis da sessão | `id`, `started_at`, `ended_at`, `started_user_id`, `ended_user_id`, `created_at`, `updated_at` e indicadores de entrada manual |
| Contexto da coluna | `duration` é o total acumulado; `running` informa o estado do relógio |

Também foram identificados H.PREV, H.GASTA (H), CRONOGRAMA, R.T. e PROFISSIONAL. Não são fontes adicionais para somar horas. H.GASTA (H) é fórmula cuja expressão não foi homologada. Cronograma não determina a data efetiva do trabalho; PROFISSIONAL não determina autoria.

Cuidados já identificados:

- Usar sessões para separar pessoa e dia, não somente o acumulado.
- Não restringir a coleta pelo responsável atual ou pela data de criação da tarefa; uma atividade antiga pode ter sessão recente.
- Na amostra, `status: active` apareceu também em sessões encerradas. Não usar esse texto isoladamente como indicação de cronômetro aberto; conferir datas e estado do relógio.
- Entrada manual precisa de informação suficiente, mas não deve ser rejeitada apenas por ser manual.
- A leitura de subitens foi parcial; ausência de sessões em uma amostra não prova ausência em toda a hierarquia.
- Não somar novamente no pai o resumo do tempo já contabilizado nos subitens.
- A documentação consultada limita a paginação usual aos itens ativos. Arquivados/excluídos exigem tratamento específico e permissões; completar a listagem ativa não comprova recuperação histórica.

Referências: [Time Tracking](https://developer.monday.com/api-reference/reference/time-tracking), [Items page](https://developer.monday.com/api-reference/reference/items-page) e [Authentication](https://developer.monday.com/api-reference/docs/authentication). Verificar os contratos vigentes antes de novas validações.

### 6.2 VR Mais / Pontomais

| Referência | Informação observada |
|---|---|
| Base consultada | `https://api.pontomais.com.br/external_api/v1` |
| Autenticação | Cabeçalho `access-token`; valor não incluído |
| Cadastro | `GET /employees`, com seleção de atributos e filtros |
| Relatório de jornada | `POST /reports/work_days`, extração de relatório, não alteração de ponto |
| Relatório de batidas | `POST /reports/time_cards`, documentado; não tratar todas as possibilidades como testadas |
| Filtros úteis | Período, colaborador, agrupamento por funcionário e colunas |
| Campos candidatos | `date`, `total_time`, `shift_time`, `time_cards`, `time_breaks`, `shift_appointments`, `time_balance` |

O relatório de jornada foi consultado com objeto `report`, `employee_id`, `group_by: employee` e formato JSON. Houve recortes de um dia e de um pequeno intervalo entre 08/09/2026 e 11/09/2026, não uma competência inteira de todos os funcionários.

A variante com `page` e `per_page` dentro de `report` retornou 500; a consulta curta sem esses parâmetros funcionou. A causa não foi diagnosticada. Não assumir que retirar paginação resolve qualquer volume: verificar cobertura e truncamento e, se necessário, esclarecer o comportamento com o fornecedor por um fluxo autorizado.

O retorno observado tinha `heading`, `data` e `meta`, com grupos, cabeçalhos, linhas diárias, rodapés e totais. Algumas células eram textos de apresentação e outras listas/objetos. Não pressupor horários uniformes prontos para cálculo nem registrar a resposta inteira em logs.

Distinções importantes:

- `total_time`: candidato ao total diário, sujeito à homologação como duração efetiva de trabalho.
- `shift_time` e `shift_appointments`: referências previstas, não trabalho efetivamente registrado.
- `time_balance`: saldo, não total diário comparável.
- `time_breaks` vazio não prova ausência de almoço; os intervalos podem estar na sequência de batidas.
- Uma amostra com seis batidas foi compatível com nove horas de trabalho. Não comprova a semântica em abonos, ajustes ou jornadas especiais.
- Não descontar intervalos duas vezes quando o total já for líquido.

A documentação foi localizada pelo link da [Central de Ajuda VR](https://materiais.vr.com.br/central-de-ajuda/extensao-api/) para a [coleção no Postman](https://documenter.getpostman.com/view/4785048/RWMCvVxN?version=latest#intro). O visualizador web não leu a página do Postman naquele momento; a coleção pública publicada foi acessada para consultar colaboradores e relatórios. Isso não foi uma falha da API da conta.

### 6.3 Credenciais e dados privados

O responsável forneceu tokens no chat original. **Não foram incluídos nos documentos publicados por este trabalho.** Não copiar o chat bruto para o repositório, repetir credenciais em prompts ou procurar tokens no histórico do Git.

Novas consultas precisam de acesso seguro e autorizado, com o menor escopo necessário. Recomenda-se substituir as credenciais usadas no estudo antes da produção, considerando outras integrações que possam utilizá-las. Essa substituição não foi executada nem confirmada neste chat.

IDs de quadro e coluna acima não autenticam a integração. Nomes e e-mails de funcionários, identificadores pessoais, batidas individuais e respostas brutas foram excluídos intencionalmente desta passagem.

## 7. Propostas não equivalem a aprovação

O plano recomenda comparar pessoa × dia/jornada e agregar por período. Diferença é Monday menos ponto. O ranking usa soma das diferenças absolutas diárias: um dia com −2h e outro com +2h resultam em saldo zero, mas diferença absoluta de 4h.

Diretrizes propostas:

- Corrigir nas fontes, sem editar horas pela central.
- Separar qualidade, resultado numérico e atendimento.
- Não converter dados desconhecidos ou falhas em zero.
- Manter dia atual e relógios abertos como provisórios.
- Aceitar justificativa sem apagar a diferença.
- Não chamar diferença de produtividade, falta comprovada ou hora extra.
- Não exigir apontamento de créditos de ausência sem trabalho.
- Mostrar cobertura e a mesma base comparável nas telas e relatórios.
- Preservar a origem e impedir duplicação de sessões.
- Comparar quantidade de horas, sem declarar prova de coincidência minuto a minuto.

Valores propostos, ainda não aprovados: tolerância de 10 minutos por dia; atualização recente a cada 30 minutos e revisão diária do período corrente; prazo até o próximo dia útil em horário a definir; piloto de cinco a dez pessoas por duas semanas úteis. Não foram agendados nem medidos.

Os antigos exemplos de faixas 10/30 minutos também não são política aprovada. As metas de desempenho do plano são objetivos de validação, não resultados observados.

## 8. Pendências reais

| Prioridade | Pendência | Próxima ação |
|---|---|---|
| 1 | Aproximadamente 50 pessoas esperadas versus 32 ativos retornados | Confirmar população, abrangência e elegibilidade |
| 1 | Total VR correto para comparação | Homologar dias comuns e exceções contra a referência da empresa |
| 1 | Início de cobertura e arquivamento/movimentação | Demonstrar histórico recuperável sem expandir silenciosamente o escopo |
| 2 | Correspondências por nome ainda não confirmadas | Revisar sugestões e ambiguidades |
| 2 | Jornadas noturnas ou multidia | Verificar existência e forma de consolidação no VR |
| 2 | Tolerância e prazo | Submeter propostas simples à validação |
| 2 | Carga integral, paginação e atualização retroativa | Planejar teste controlado de volume, falhas e cobertura |
| 2 | Acesso desktop e equipes | Confrontar o plano com o que já existe, sem presumir compatibilidade |
| 3 | Retenção, acesso histórico e operação | Definir política e responsáveis antes da liberação |

Primeiro aproveitar documentos e verificações disponíveis. Perguntar apenas o que exige escolha ou informação ausente, sem reabrir quadro de origem e autoria por start.

## 9. Próxima sequência de trabalho

1. Ler contexto e plano, depois especificação e manual.
2. Verificar branch e alterações atuais, separando documentação de backend paralelo.
3. Preparar decisões de arquitetura e critérios de verificação, sem escrever código nesta etapa.
4. Fechar população, referência de ponto e cobertura histórica antes de prometer resultados conclusivos.
5. Planejar homologação de cinco a dez pessoas e dez dias úteis, incluindo exceções disponíveis, sem alterar dados reais para fabricar testes.
6. Usar P-01 a P-28 do plano, abrangendo identidade, start por A/stop por B, subitens, fontes incompletas, mudanças e permissões.
7. Preparar alinhamento da especificação/manual se solicitado; não implementar regras antigas isoladamente.
8. Aguardar autorização explícita antes de implementar, configurar produção, alterar fontes ou ampliar coleta.

O responsável prefere funcionalidades claras e interface simples. Explicar as escolhas pelo impacto para funcionário e gestor, sem transformar escolhas técnicas complexas em requisitos supostamente pedidos.

## 10. Git e trabalho paralelo

Repositório: [luisotvbim-sudo/CEP-API](https://github.com/luisotvbim-sudo/CEP-API).

Branch desta documentação: [docs/plano-conciliacao-horas](https://github.com/luisotvbim-sudo/CEP-API/tree/docs/plano-conciliacao-horas).

Estado verificado antes desta atualização:

- `3a1259e`: especificação e manual publicados.
- `c95c285`: plano publicado na branch de documentação.
- A branch de documentação partiu da versão publicada da main, sem incorporar dois commits locais de outros trabalhos.
- O checkout principal estava em `feature/controle-de-ponto-backend`, commit `73ef27d`, com rastreamento remoto dessa feature.
- `5b0094c` e `73ef27d` pertencem a esse trabalho separado e não foram auditados no estudo.
- Havia uma cópia local não rastreada de `docs/conciliacao-horas/plano-produto-estudo.md` no checkout principal. O documento já estava versionado na branch de documentação; não incluí-lo automaticamente em outra branch.

Esse estado pode mudar: verificar novamente antes de agir. Não houve criação de pull request ou merge do plano na main neste chat. Publicar a branch não autoriza merge de backend, rebase, sobrescrita de alterações ou publicação em produção.

A publicação documental usou áreas temporárias separadas para preservar o checkout principal. Manter esse cuidado; não usar reset destrutivo, troca forçada de branch ou push forçado para contornar divergências.

## 11. Mensagem pronta para outro agente

> Continue a preparação da aplicação de conciliação de horas do CEP-API. Comece por docs/conciliacao-horas/contexto-para-proximo-agente.md e plano-produto-estudo.md, na branch docs/plano-conciliacao-horas. Consulte depois especificação e manual, respeitando as decisões mais recentes.
>
> O objetivo é relacionar horas de ponto do VR Mais com horas do EG03_MICRO PLANEJAMENTO. O cadastro parte do VR. Cada sessão pertence exclusivamente a quem deu start, identificado por started_user_id, independentemente do responsável da atividade ou de quem encerrou. Essa regra e o quadro já foram decididos: não volte a perguntar por eles.
>
> Estamos em estudo e planejamento. Não escreva código nem altere fontes sem nova autorização explícita. O repositório pode conter backend de outra frente; examine o estado sem misturar branches ou presumir que o código já atende ao plano.
>
> Prepare arquitetura e sequência de validação para comparação diária, acesso pessoal/gerencial, filtros, detalhe, gráficos, exportação e solicitações internas. Preserve React em WebView2 e laranja/cinza. Não amplie a busca para todos os quadros nem transforme auditoria minuto a minuto em requisito.
>
> Separe decisões confirmadas, evidências apenas amostrais e propostas. Priorize população, significado do total VR e cobertura histórica. Use os 28 cenários e separe incompletude de divergência. Não exponha tokens em arquivos ou prompts; novas consultas dependem de acesso seguro e autorizado.
>
> Entregue recomendação clara, decisões, dependências, riscos e critérios de aceite. Implemente somente depois de solicitação expressa.

## 12. Critério de continuidade

O próximo agente deve conseguir explicar, sem o chat original: o problema, o quadro, a autoria da sessão, o que foi entregue, o que sustenta a viabilidade, as propostas não aprovadas, os próximos passos e por que ainda não deve codificar.

Pedidos operacionais pontuais do chat, como desligar o computador ao terminar esta publicação, não são requisitos da aplicação nem instruções recorrentes para o próximo agente.

Não há segredo necessário para ler os documentos. Se novas leituras privadas forem indispensáveis, obter acesso em uma etapa própria, não publicar credenciais junto ao contexto.
