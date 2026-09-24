# CEP Horas — guia de implementação de todas as telas do front

Atualizado em 23/09/2026. Destinatário: agentes e desenvolvedores do repositório `CEP-FRONT`.

Este é um **handoff de implementação**, não uma afirmação de que todas as telas ou regras de conciliação já funcionem. O objetivo é permitir construir a experiência completa de Membro, Líder e Coordenador sem confundir visão do produto com contrato disponível. Para a regra de negócio, ler também [a especificação funcional](conciliacao-horas/especificacao-funcional.md); para os contratos administrativos detalhados, ler [a integração Monday/VR Mais](workforce-admin-integration.md). O OpenAPI gerado da **branch de backend que será consumida** e o código da API prevalecem quando houver divergência técnica.

## 1. O produto que as telas precisam representar

O CEP Horas traz apontamentos de atividades do Monday e registros de ponto do VR Mais, liga cada pessoa às duas identidades externas e permite analisar, para o **mesmo período escolhido**, as horas de cada fonte e sua diferença. A estrutura é `Organização → Times → Pessoas`. O Membro vê seus próprios dados; o Líder vê os seus e os das pessoas dos times que lidera; o Coordenador administra e consulta toda a organização.

O lançamento do Monday pertence ao **único `PROFISSIONAL` do item ou subitem**; se o subitem não tiver profissional próprio, herda o do pai. A coluna `R.T.` e a pessoa que iniciou ou parou o cronômetro não definem a atribuição das horas.

Convenção de produto para quando o backend entregar conciliação: `diferença = Monday − VR Mais`. Saldo do período é a soma das diferenças diárias **calculáveis** dentro do filtro; divergência acumulada é a soma dos valores absolutos diários, para que `−2h` e `+2h` não desapareçam em um saldo zero. Horas exibidas como diferença **não** são automaticamente horas extras, débito trabalhista, produtividade ou falta. Valor desconhecido é `null`/“Indisponível”, nunca zero implícito.

As métricas consolidadas, a comparação oficial, o calendário, as tolerâncias, os alertas, as justificativas e os relatórios **ainda não têm endpoint na API atual**. A implementação visual dessas partes pode ser planejada e desenhada, mas não deve usar agregação local de registros brutos como se fosse um saldo homologado.

## 2. Estado real em 23/09/2026

| Capacidade | Backend `CEP-API` | Front `CEP-FRONT` | Conduta nesta entrega |
|---|---|---|---|
| Login, renovação de sessão, logout, recuperação de senha | Disponível | Disponível em navegador e desktop | Preservar arquitetura existente. |
| Aceite de convite | `POST /api/v1/auth/invitations/accept` | Tela pública ainda ausente | Criar tela; verificar transporte web/desktop. |
| Organização, usuários, convites, times e vínculos | Disponível | Telas administrativas de times, pessoas e convites existem | Corrigir textos/fluxos e completar gestão de usuários/coordenadores. |
| Identidades Monday/VR, associação manual e convite | Disponível | Telas administrativas existem | Reusar, sem correspondência automática por nome. |
| Sincronização manual por Membro/Líder/Coordenador | 7 dias normais; 90 dias em carga inicial/admin `full=true` | Só tela administrativa, ainda com texto de 60 dias | Corrigir e disponibilizar ação conforme perfil. |
| Histórico bruto por pessoa/fonte/período | Disponível, até 90 dias por requisição | Só tela administrativa, ainda limitada a 60 dias no cliente | Corrigir e expor versão pessoal/gestão com escopo da API. |
| Resumo comparado, saldo, diferença por pessoa/time | Não disponível | Não disponível | Especificar telas; bloquear valores reais até contrato de conciliação. |
| Divergências, alertas, notificações, justificativas, aprovação | Não disponível | Não disponível | Planejar telas; não simular envio ou decisão. |
| Exportação e relatórios conciliados | Não disponível | Não disponível | Planejar; não exportar “conciliação” calculada no cliente. |

O front usa React 19, TypeScript, Vite, `src/App.tsx`, `src/admin/*`, `src/auth/*`, CSS próprio e um host WPF/WebView2. `App.tsx` hoje encaminha `organizationAdmin` para `AdminShell`, `systemAdmin` para seleção de organização e mostra ao usuário comum apenas “área de membro e líder ainda não disponível”. `AdminShell` já contém Pessoas, Associar e convidar, Sincronização, Times e Histórico. Antes de codar no `CEP-FRONT`, cumprir o `AGENTS.md` daquele repositório e conferir seus snapshots OpenAPI. Em 23/09, a API local em `127.0.0.1:8080` não estava respondendo; por isso este documento foi conferido contra os controllers/contratos da branch, **não** contra uma sessão local ativa ou dados reais das fontes.

### Correções concretas no front existente

1. Trocar “60 dias” por **90 dias** em `src/admin/HistoryPage.tsx`, `src/admin/format.ts` (validação inclusiva), testes correspondentes, `src/admin/SyncPage.tsx`, `README.md` e documentação de produto/compatibilidade do front.
2. Distinguir a atualização normal de **7 dias** do `full=true` de **90 dias**. O texto “coleta incremental” sozinho é impreciso: a persistência faz upsert, mas a consulta normal reavalia uma janela móvel de sete dias; não há delta puro por alteração desde o último cursor.
3. Remover de `src/admin/TeamsPage.tsx` a frase que diz que vínculo de Líder ainda não concede acesso operacional. O backend já limita consultas de times, pessoas e histórico pelo vínculo `manager` vigente; faltam as telas operacionais e a conciliação.
4. `SyncPage` não deve tratar `completeSnapshot=true` como “diretório inteiro atualizado” em tentativa normal, nem `receivedCount=0` como diretório vazio: nessas tentativas o diretório não é consultado. O escopo foi restrito às pessoas selecionadas.
5. Atualizar mensagens de erro para `sync_scope_empty`, `full_sync_forbidden` e `monday_responsible_column_unavailable`; rever o texto de `history_period_too_large` para 90 dias. Sessões Monday com múltiplos profissionais são ignoradas pela API, não geram esse erro.
6. Sincronizar `docs/openapi-backend-current.json` com a API executando, comparar com `docs/openapi.json`, regenerar `src/auth/api-schema.d.ts` se necessário e adaptar o cliente. A simples existência de Swagger não prova que PostgreSQL ou fontes externas estejam disponíveis.

## 3. Permissões e escopo: base de toda a navegação

O JWT traz `role=systemAdmin|organizationAdmin|user`. No CEP Horas, `organizationAdmin` é o **Coordenador**. Líder e Membro têm `role=user`; a condição de Líder vem de um vínculo `TeamAssignmentRole.Manager` vigente no banco, **não** de outro `UserRole` ou de um campo fixo do JWT. O mesmo usuário pode ser Líder de um time e Membro de outro. A interface pode oferecer “Minha jornada” e “Gestão dos times” quando fizer sentido, sempre descartando seleção/filtros incompatíveis ao alternar.

| Recurso atual | Membro (`user`) | Líder (`user` + `manager` vigente) | Coordenador (`organizationAdmin`) | `systemAdmin` |
|---|---|---|---|---|
| `GET /organization/time-control/people` e `/history` | Pessoas/registros próprios, quando há vínculo ativo que concede visibilidade | Próprios + usuários atualmente vinculados aos times que lidera | Organização inteira | Organização explicitamente selecionada |
| `GET /organization/time-control/teams` | Lista vazia: endpoint lista times **geridos**, não times dos quais é apenas membro | Times geridos vigentes | Todos da organização | Organização selecionada |
| `GET /teams/{id}/assignments` | Só se puder ler o time; em geral, não | Times geridos | Todos | Organização selecionada |
| `POST /synchronizations` | Identidades ativas associadas da própria pessoa | Próprias + pessoas visíveis dos times geridos | Pessoas associadas da organização | Organização selecionada |
| `GET /synchronizations/latest` e `/{batchId}` | Somente lotes solicitados pelo próprio usuário | Idem | Todos da organização | Organização selecionada |
| `POST /synchronizations?full=true` | Proibido | Proibido | Permitido | Organização selecionada |
| Criar/editar times, vínculos, identidades/convites, usuários | Não | Não | Permitido | Organização selecionada |

O backend calcula o escopo de Líder pelos **vínculos vigentes hoje** em time ativo. Uma troca de time é rara, mas a regra atual permite ao líder atual consultar o histórico anterior à transferência e retira esse acesso do líder antigo. **Não** apresentar totais históricos por time como se cada registro tivesse um time na data trabalhada: a atribuição temporal ainda não existe no contrato de histórico. `GET /teams` não serve para mostrar ao Membro “meu time”. Se essa tela exigir o nome do time para Membro, solicitar um endpoint específico ao backend.

Há um caso de borda a validar com backend: `TimeControlAccessService` retorna visibilidade vazia para `user` sem qualquer vínculo ativo. A sincronização inclui explicitamente o próprio usuário, mas `GET people/history` pode não devolver sua pessoa até que receba um vínculo. Não mascarar isso como “sem horas”; tratar como conta sem associação/vínculo e registrar teste de aceite.

O cliente **nunca** decide a autorização. Ele esconde ações indevidas por UX, mas depende do servidor para barrar ID de outro usuário/time/organização. Para `systemAdmin`, exigir seleção de organização e acrescentar `organizationId` nas rotas organizacionais; para os demais, usar a organização do token e nunca permitir troca arbitrária. `SystemAdmin` é suporte técnico, não uma persona cotidiana do painel operacional.

## 4. Contratos disponíveis para as telas

Prefixo HTTP `/api/v1`; no `AuthClient` atual, os caminhos passados a `request` começam depois desse prefixo. JSON/enums são `camelCase`; datas civis são `YYYY-MM-DD`, instantes são ISO 8601. Erros são `application/problem+json`, com `code` e `correlationId`.

| Necessidade | Endpoint e parâmetros principais | Observações |
|---|---|---|
| Sessão | `POST /auth/web/login`, `/auth/web/refresh`, `/auth/web/logout`; desktop usa `/auth/login`, `/refresh`, `/logout`; `GET /me` | Reusar `AuthClient`/bridge existente; não guardar refresh token no React. |
| Aceite de convite | `POST /auth/invitations/accept` | Corpo: `email`, `code`, `displayName`, `password` (mín. 12 caracteres), `client`; retorna tokens de sessão. Não há tela pronta. |
| Recuperação | `POST /auth/password/forgot`, `POST /auth/password/reset` | Pedido de código responde `202` mesmo para conta inexistente. |
| Pessoas associadas | `GET /organization/time-control/people?search=&page=1&pageSize=50` | `PagedResponse<WorkforcePersonResponse>`; servidor aplica escopo. |
| Histórico bruto | `GET /organization/time-control/history?from=YYYY-MM-DD&to=YYYY-MM-DD&workforcePersonId=&source=&search=` | `from/to` obrigatórios, período inclusivo ≤ 90 dias, até 200 pessoas, sem paginação de histórico. `source=monday|vrMais`. |
| Times geridos | `GET /organization/time-control/teams?includeInactive=false&asOf=YYYY-MM-DD` | Para Líder retorna apenas times que lidera **hoje**; `asOf` muda contagens, não autorização. |
| Vínculos do time | `GET /organization/time-control/teams/{teamId}/assignments?includeHistory=false&asOf=YYYY-MM-DD` | `role=member|manager`; datas de vigência inclusivas. Sem `includeHistory`, retorna vínculos ativos em `asOf`. |
| Atualização normal | `POST /organization/time-control/synchronizations` | Sem corpo; hoje + seis dias anteriores no fuso `America/Sao_Paulo`; POST espera o resultado final, podendo demorar. |
| Atualização completa | `POST /organization/time-control/synchronizations?full=true` | Administração apenas; diretórios + últimos 90 dias. Primeira solicitação administrativa sem identidades persistidas também faz carga inicial de 90 dias. |
| Estado da atualização | `GET /organization/time-control/synchronizations/latest` e `/{batchId}` | `404 sync_not_found` é estado vazio. Lote de usuário comum é só o que ele solicitou. |
| Identidades externas | `GET /organization/time-control/external-identities?source=&activeOnly=true&mapped=false&search=&page=&pageSize=` | Administração apenas. Usar `id` interno, nunca `externalId`, para associação. |
| Associação/convite | `POST /organization/time-control/people/invitations` | Corpo: `email`, `displayName`, `mondayIdentityId`, `vrMaisIdentityId`. E-mail é enfileirado; não afirmar entrega. |
| Convites/usuários | `GET /organization/invitations`, `POST /organization/invitations/{id}/resend`, `GET /organization/users`, `PATCH /organization/users/{id}` | Coordenador administra papéis. `PATCH` pede `role` e `status`, além de `displayName`/`products` opcionais: preservar valores existentes ao editar. |
| Administração de times | `POST /organization/time-control/teams`, `PATCH /teams/{teamId}`, `POST /teams/{teamId}/assignments`, `PATCH /teams/{teamId}/assignments/{assignmentId}/end` | `CreateTeamAssignmentRequest={userId,role,effectiveFrom,effectiveTo}`; conflito de vigência gera 409. |
| Auditoria | `GET /organization/audit?before=&pageSize=` | Apenas administração, eventos em ordem decrescente; não é histórico de casos de conciliação. |
| Organização técnica | `GET /admin/organizations`, `POST /admin/organizations` | Somente `systemAdmin`; manter seleção explícita. |

Para respostas completas de identidade, convite, lote de sincronização e registro bruto, usar o OpenAPI e [o contrato administrativo](workforce-admin-integration.md). Campos essenciais do histórico: `people[]` com `workforcePersonId`, `userId`, `displayName`, `email`, `records[]`; cada registro traz `id`, `source`, `externalKey`, `workDate`, `startedAt`, `endedAt`, `durationSeconds`, `state`, `title`, `url`, `detailsJson` e `lastSyncedAt`. O VR Mais pode devolver um dia `missing` ou `unrecognized` com duração `null`; Monday usa `closed` ou `running`. `detailsJson` é um **string JSON opcional e de formato específico da fonte**: fazer parse defensivo e renderizar texto, nunca HTML bruto. Só abrir `url` validada como HTTPS.

Não existe hoje endpoint para: resumo/saldo oficial, comparação diária, membros filtrados por `teamId` no histórico, cobertura por pessoa/dia, “último sucesso” de cada fonte, notificações, casos, justificativas, relatórios, calendário/regras ou exportação. Não inventar rotas ou preencher esses dados com mocks em produção.

## 5. Mapa de navegação proposto

```text
Público
├─ Entrar / recuperar senha / redefinir senha
└─ Aceitar convite (nova tela)

Membro — Minha jornada
├─ Meu resumo (agora: registros e atualização; depois: comparação oficial)
├─ Meu histórico → detalhe de dia e registros de origem
└─ Minhas pendências / avisos (somente quando houver API)

Líder — Minha jornada + Gestão dos times
├─ Meus times → pessoas do time → histórico da pessoa
├─ Visão comparativa / ranking (quando houver API)
└─ Divergências e relatórios (quando houver API)

Coordenador — Organização
├─ Visão geral da organização (comparação depende de API)
├─ Pessoas / correspondências / convites / usuários e papéis
├─ Times e vínculos
├─ Sincronização e qualidade das fontes
├─ Histórico bruto / auditoria
└─ Regras, calendário, divergências, notificações e relatórios (API pendente)

SystemAdmin — Selecionar organização → ferramentas administrativas da organização
```

Evitar criar um item de navegação clicável que leva a uma página vazia. Para telas bloqueadas por API, usar uma visão explicitamente rotulada “Em preparação” **ou não exibir** a rota até o backend existir; nunca produzir números demonstrativos sem marcação inequívoca.

## 6. Especificação das telas que já podem consumir a API

### T01 — Entrada, sessão e conta

- Conservar a tela de login existente e as implementações distintas de navegador (`/auth/web/*`, cookie de refresh `HttpOnly`) e desktop (bridge WPF/DPAPI). Após login/`restore`, chamar `/me` conforme fluxo atual e encaminhar pelo papel real.
- `organizationAdmin` abre Administração; `systemAdmin` escolhe uma organização antes de operar; `user` abre Minha jornada. Para `user`, `GET /teams` não vazio habilita Gestão dos times. O próprio usuário continua acessível na visão pessoal quando também é líder.
- Em 401, a infraestrutura atual tenta uma única renovação serializada; falha encerra a sessão. Em 403, não redirecionar para uma tela administrativa. Preservar `correlationId` copiável.
- Aceite de convite: tela pública com e-mail, código recebido, nome e senha; chamar `/auth/invitations/accept` e encaminhar após sucesso. `invalid_invitation` cobre código inválido/expirado; `workforce_person_unavailable` pede suporte do coordenador. Não expor código em logs/analytics. O convite de pessoa associada vale 48 horas e o coordenador pode reenviar, invalidando o código anterior. Validar o desenho do transporte desktop antes de tentar usar esse endpoint pelo bridge, cuja lista de rotas atualmente não o permite.
- Recuperação: não informar se o e-mail existe; confirmar “Se houver uma conta ativa, enviaremos instruções”. Redefinição exige e-mail, código e nova senha. Não presumir que o front receba diretamente o status de entrega do e-mail.

### T02 — Minha jornada: início do Membro

- Cabeçalho com nome, organização, período aplicado, `Atualizar meus dados` e situação de **minha última tentativa**. Período padrão sugerido: últimos 7 dias civis em São Paulo; permitir seleção dentro dos 90 dias disponíveis para consulta, sem prometer que uma atualização normal reprocessará o intervalo escolhido.
- Na fase atual, mostrar `Meus registros` separados por fonte e dia, com explicação: “Ainda não há conciliação oficial de saldo; estes são os registros recebidos.” Buscar `/people` e localizar `userId === /me.id`; consultar `/history` com `workforcePersonId` quando houver. O servidor restringe o escopo; não enviar ID arbitrário obtido de URL sem validação.
- Dia com VR `missing`, duração `null`, cronômetro Monday `running`, falha de sincronização ou ausência de cobertura deve ser marcado como **incompleto/provisório**, não como `00h00` ou “conciliado”. Mostrar última importação por registro e estado da fonte quando disponível.
- Estados: conta sem associação, associação sem vínculo ativo, primeira sincronização ainda não feita, nenhuma hora no intervalo, erro de rede, fonte parcial, atualização em andamento, dados de dia atual provisórios. As orientações de cada estado devem apontar para a ação possível (atualizar, ajustar período, contatar coordenador).
- `Atualizar meus dados` chama POST normal, sem `full`. Desabilitar durante a operação, mostrar feedback por fonte e refazer histórico ao concluir. A ação não muda o período do filtro nem prova que os 90 dias foram atualizados.
- Ao receber a futura API de conciliação, esta tela substitui o bloco bruto principal por totais VR/Monday, saldo assinado, divergência absoluta, cobertura e dias analisados **todos derivados do mesmo filtro inclusivo**; conservar o histórico bruto como drill-down.

### T03 — Meu histórico e detalhe do dia

- Filtros: início, fim, fonte (ambas/Monday/VR), botão Consultar. Intervalo válido de até 90 dias **inclusivos**; datas civis não sofrem conversão de fuso. `GET /history` já agrupa por pessoa, mas a área de Membro só apresenta a pessoa autenticada.
- Agrupar registros por `workDate`; mostrar fonte, atividade/título, duração, estado, início/fim quando conhecidos e link HTTPS da atividade. O detalhe lista batidas do VR (`detailsJson.timeCards`, quando válido), cronômetro em andamento e indicação de lançamento manual do Monday quando disponível. `startedByUserId`, se exibido para diagnóstico autorizado, **não** substitui o responsável da atividade.
- `durationSeconds` pode passar de 24h; formatar por segundos totais, nunca como objeto `Date`/relógio circular. `null` permanece “Indisponível”. Ordenar registros de forma estável e usar `id`/`externalKey` para chaves de componente.
- Não mostrar uma diferença diária concludente baseada apenas na soma desses registros. A API ainda não diz quais dias são elegíveis, se o ponto é completo, como tratar intervalos/jornadas noturnas ou quais atividades Monday entram no cálculo.

### T04 — Gestão dos times do Líder

- Ao abrir a visão de gestão, carregar `GET /teams` (sem `includeInactive`) para obter somente os times geridos. Se vazio, retornar à visão pessoal. Mostrar nome, número de membros e líderes em `asOf=hoje`, situação ativa e botão “Ver pessoas”. `activeMembers`/`activeManagers` são **contagens de vínculos**, não de registros de ponto ou produtividade.
- Dentro de um time, carregar `GET /teams/{id}/assignments?includeHistory=false&asOf=hoje`; usar vínculos `member` para a lista de membros e identificar `manager` separadamente. Correspondência com `/people` por `userId`, não por nome/e-mail. Uma pessoa sem `WorkforcePerson` associada deve aparecer como “Aguardando associação” e não como zero horas.
- O líder vê seus próprios registros na visão pessoal; se também for membro do time, evitar duplicar linha. Pessoa em múltiplos times pode aparecer em cada lista, mas a futura visão geral não pode somar suas horas duas vezes sem política explícita.
- Ação “Atualizar dados do meu escopo” chama o POST normal e atualiza **a própria pessoa e todos os usuários visíveis nos times liderados**. A API não oferece atualização de um único time/pessoa escolhida pelo líder. Não rotular botão como “Atualizar apenas este time”.
- O detalhamento de uma pessoa usa `/history?workforcePersonId=<id>&from=&to=`. Se o vínculo mudou e a API negar/ocultar, limpar a seleção e explicar perda de acesso. Não cachear dados de outro time após troca de perfil ou sessão.
- A futura tabela de diferenças por pessoa deve ter colunas período, VR, Monday, saldo, divergência absoluta, dias comparáveis, cobertura e estado; precisa de endpoint novo. O backend atual não aceita `teamId` em `/history`, nem retorna totais conciliados.

### T05 — Coordenação: pessoas, correspondências e usuários

- Reutilizar `PeoplePage` e `InvitePage`: listas paginadas separadas de identidades `monday` e `vrMais`, somente ativas/não associadas, com busca e seleção explícita. Sugestão por e-mail idêntico é apenas sugestão; **nunca** confirmar automaticamente. Mostrar divergência de nome/e-mail, IDs externos e `lastSeenAt` para revisão humana.
- Enviar `mondayIdentityId` e `vrMaisIdentityId` internos a `/people/invitations`. Exibir “Convite colocado na fila de envio”, não “E-mail entregue”. Reenvio usa o convite existente; não cria nova associação.
- Estado da pessoa: sem conta/convite pendente, expirado, revogado (quando obtido de `/organization/invitations`) ou aceito/conta ativa. `userId` preenchido indica vínculo após aceite. Se a consulta complementar de convites falhar, mostrar estado incerto, não afirmar que ainda vale.
- Criar/complete uma área **Usuários e coordenadores** usando `GET /organization/users`, `PATCH /organization/users/{id}` e convites administrativos. Coordenador pode promover `user` a `organizationAdmin` e rebaixar outro coordenador, mas a API preserva pelo menos um coordenador ativo (`last_organization_admin`). Antes de `PATCH`, recuperar `role`/`status` atuais e preservar `products` quando a alteração não tratar deles. Mudanças de papel/status revogam sessões; avisar quem é afetado.
- Novas contas não associadas criadas via `/organization/invitations` não ganham automaticamente identidades Monday/VR; não prometer histórico pessoal para elas. Para pessoas operacionais, preferir o fluxo de associação + convite.

### T06 — Coordenação: times e vínculos

- Reutilizar `TeamsPage`: listar, criar, renomear, ativar/desativar e consultar vínculos por data. Mostrar `member` como Membro e `manager` como Líder. Para adicionar vínculo, exigir conta ativa da mesma organização; enviar vigência inclusiva, com fim opcional.
- Tratar `team_name_unavailable`, `team_inactive`, `team_assignment_overlap`, `invalid_assignment_period`, `user_inactive`, `team_assignment_already_ended` e `team_assignment_not_found` com mensagens orientadas à ação. Mudança de vínculo modifica o escopo de consulta imediatamente; invalidar caches de times, pessoas, histórico e navegação do usuário afetado.
- O backend não tem endpoint para “transferir pessoa” em uma transação, nem para dividir horas antigas entre times. A UI pode encerrar um vínculo e criar outro, mas deve explicar as datas e não apresentar uma reconciliação histórica inexistente.

### T07 — Sincronização e saúde dos dados

- Na área administrativa, manter cards independentes para Monday e VR Mais: estado, início/fim, contagens de identidades e registros, período coberto, código/mensagem de falha. `partiallySucceeded` é sucesso parcial; uma fonte falha sem apagar a outra. Não exibir percentual de progresso inventado.
- Distinguir dois botões/fluxos: `Atualizar últimos 7 dias` (todos os perfis, escopo do solicitante) e `Reprocessar até 90 dias e diretórios` (só administração, com confirmação). O primeiro pedido administrativo em instalação sem identidades pode automaticamente fazer a carga de 90 dias.
- `POST` é aguardado até concluir, mas a tela pode consultar `/{batchId}` quando já souber o ID. Se receber `409 sync_already_running` porque **outra pessoa** iniciou a atualização, `latest` de Membro/Líder não revela esse lote: informar o conflito e oferecer nova tentativa depois; não fazer polling infinito de um ID desconhecido. Coordenador pode consultar `latest` da organização.
- `409 sync_scope_empty`: nenhuma identidade ativa associada nas **duas** fontes para o escopo; orientar contato com o coordenador/associação. `full_sync_forbidden`: esconder a ação e tratar 403 se chamada. `sync_not_found` em GET é estado inicial vazio.
- A atualização normal consulta o VR Mais por IDs/data e o Monday por itens/subitens filtrados pelo Responsável, porém o histórico de sessões dos itens selecionados ainda é lido e filtrado por data localmente. Persistência é idempotente (upsert/correção, sem duplicação); **a consulta externa não é delta puro por mudança desde o último cursor**. Os registros guardados têm retenção de 90 dias; uma atualização de 7 dias não atualiza automaticamente dias antigos.
- O endpoint mostra a **última tentativa**, não fornece “último sucesso por fonte” como campo dedicado. Não derivar “dados atuais” do horário de uma tentativa falha. `completeSnapshot` de tentativa normal limita-se ao escopo processado, não à organização inteira. `receivedCount=0` pode significar “diretório não consultado”.
- Se Monday retornar `monday_responsible_column_unavailable`, orientar revisão da configuração na origem. Sessões com múltiplos profissionais são ignoradas, não transferidas para quem clicou no cronômetro.

### T08 — Histórico administrativo e auditoria

- Corrigir a tela de histórico existente para 90 dias. Filtros por pessoa, fonte e texto; tabelas por pessoa/dia; conservar o resultado anterior se uma nova consulta falhar, com aviso de desatualização/erro. `GET /history` retorna até 200 pessoas sem paginação, suficiente para a organização pequena atual mas não para escala futura.
- A auditoria administrativa (`GET /organization/audit`) é uma tela distinta, com evento, ator, alvo, data e detalhes seguros; paginação por `before`, não por número de página. Não mostrar dados sensíveis brutos ou prometer trilha de justificativas ainda inexistentes.

## 7. Telas planejadas que dependem de novos contratos do backend

Estas telas fazem parte do produto, mas **não devem ser ligadas a dados fictícios nem a cálculos oficiais feitos só no React**. Criar componentes/páginas vazias tipadas é opcional; a entrega funcional exige endpoints homologados.

| Tela/fluxo | Experiência desejada | Contrato mínimo a solicitar antes de ativar |
|---|---|---|
| Painel do Membro | Cards de VR, Monday, saldo do período, diferença absoluta, dias comparáveis/cobertura; calendário diário e detalhe | Endpoint de conciliação por pessoa/período com estados de qualidade, totais, regras e referências dos registros. |
| Visão geral do Líder | Seletor de time autorizado, ranking por pessoa, distribuição de diferenças, drill-down por dia | Resumo por time/período + linhas por pessoa, escopo validado no servidor, sem dupla contagem. |
| Visão geral do Coordenador | Organização toda, comparação entre times e pessoas | Resumo por organização/período e por time; regra explícita para pessoas em múltiplos times ou transferidas. |
| Calendário e dia conciliado | Ponto, Monday, diferença, estado de qualidade, motivo, registros participantes/excluídos | DTO diário com origem, elegibilidade, calendário/jornada, completude, regra aplicada e última atualização por fonte. |
| Divergências e justificativas | Fila, pedido, resposta, decisão, devolução, reabertura, histórico | API de casos/transições, autorização por papel/time, controle de concorrência/idempotência e auditoria. |
| Alertas/notificações | Não lidas, histórico, link para caso, marcar como lida | API de notificações destinatário-escopo, leitura e vínculo com caso. |
| Relatórios/exportação | Prévia fiel aos filtros, planilha do próprio usuário/time/organização | API de relatório/exportação com mesmo cálculo, escopo e metadados de geração/cobertura. |
| Regras e calendário | Tolerâncias, jornada, feriados/ausências, atividades elegíveis com vigência | API administrativa versionada; definições aprovadas de fuso e distribuição em jornadas noturnas. |
| Qualidade detalhada | Não associados, inválidos, descartados, cobertura por pessoa/dia e último sucesso | Endpoints de diagnósticos/qualidade; o lote atual não oferece tudo isso. |

Para esses novos contratos, preservar a regra transversal: **todo card, tabela, gráfico, ranking e exportação usa exatamente o mesmo período inclusivo e o mesmo escopo**, inclusive quando o Líder seleciona cinco dias. A diferença de um dia incompleto é “não calculável”, não zero. O dia atual é provisório. Uma fonte indisponível não vira zero. Ranking padrão por soma das diferenças absolutas diárias, nunca apenas pelo saldo líquido. Justificativa aceita não apaga a diferença de origem. Nenhum usuário aprova o próprio caso.

## 8. Componentes, estados e UX transversais

- Manter a identidade já existente do `CEP-FRONT` (logo, laranja/cinza, Manrope, componentes administrativos), adaptando-a ao uso operacional em desktop/WebView2 e navegador. Não criar uma segunda biblioteca visual sem necessidade.
- Um componente de filtro de período compartilhado deve trabalhar com **datas civis de São Paulo**, validar intervalos inclusivos e deixar visível o período aplicado. Não usar `new Date('YYYY-MM-DD')` para deslocar a data de trabalho por fuso. Permitir consulta de 90 dias, mas informar que “Atualizar” normal recarrega apenas a última semana.
- Criar um resumo de estado da fonte separado para Monday e VR Mais. `running`, `succeeded`, `partiallySucceeded`, `failed`, ausência de lote e dado antigo são estados distintos. Não apagar dados persistidos da tela só porque uma tentativa falhou.
- Reutilizar o tratamento central de `ProblemDetails`; traduzir `code`, mostrar `correlationId` copiável, não expor stack trace, `detailsJson` completo ou tokens no console. Tratar explicitamente 401, 403, 404 esperado, 409, 429 e rede offline. Erro de mutação com resposta incerta exige conferir o estado antes de repetir.
- Buscar/paginar pessoas/identidades com cancelamento e debounce; abortar polling ao desmontar. Mutações invalidam caches de pessoas, identidade, times, histórico e estado de atualização conforme impacto.
- Todos os estados precisam de texto e próximo passo: carregando, vazio real, filtro sem resultado, não associado, sem vínculo, fonte parcial, consulta falha, autorização negada. Diferenciar “não há lançamento confirmado” de “não conseguimos consultar”.
- Formatar `durationSeconds` como duração total (`160h00`, por exemplo), não como hora do dia; instantes em `America/Sao_Paulo` e datas `YYYY-MM-DD` como datas civis. Rótulos de status devem ter texto e ícone/cor, nunca só cor.
- Tabelas devem ter cabeçalho legível, rolagem horizontal em janela pequena e navegação por teclado. Foco visível, `aria-live` para conclusão de sincronização/convite, respeito a movimento reduzido e conteúdo de gráfico também em tabela acessível.
- Links externos só HTTPS. O host desktop já tem política de navegação/bridge; novas rotas HTTP precisam ser adicionadas à allowlist **após** revisão de segurança, inclusive convite/novos endpoints. Não incluir segredo Monday/VR no bundle, armazenamento, query string ou logs.

## 9. Sequência de implementação recomendada para o agente do front

1. **Compatibilidade primeiro:** rodar a API com PostgreSQL, atualizar/inspecionar OpenAPI conforme `CEP-FRONT/AGENTS.md`; corrigir 60→90 dias, texto de sync 7/90, `completeSnapshot`, erros e testes existentes. Se a API não estiver rodando, registrar que a validação foi contratual, não de integração real.
2. **Entrada operacional:** criar `UserShell` para `role=user`, com Minha jornada, Histórico e Gestão dos times quando houver `GET /teams` não vazio. Reusar `AuthClient`; limpar cache ao trocar sessão/escopo.
3. **Telas utilizáveis agora:** histórico pessoal, detalhe bruto do dia, lista dos times liderados, pessoas do time e histórico autorizado da pessoa, ação de atualizar escopo com estado por fonte. Manter aviso honesto de que saldo comparado ainda não existe.
4. **Administração completa:** corrigir as telas existentes, adicionar usuários/coordenadores, aceite público de convite e auditoria, respeitando o escopo técnico de `systemAdmin`.
5. **Fase condicionada ao backend:** integrar novos endpoints de conciliação, cobertura, divergências, notificações, calendário e relatórios quando existirem, usando os mesmos filtros/escopos. Não antecipar contratos em mocks de produção.

### Testes de aceite obrigatórios do front

1. Membro autenticado encontra somente a própria pessoa/histórico; não vê navegação administrativa; sem vínculo ativo aparece estado explicativo, não “zero horas”.
2. Líder de A e B vê A/B, seus membros e seus próprios registros; não vê C. Trocar de time ou perder vínculo limpa seleção e dados em cache. `asOf` passado não amplia acesso.
3. Coordenador vê organização inteira, cria time, vincula conta ativa, associa identidades e reenvia convite sem criar pessoa duplicada. `SystemAdmin` deve escolher organização.
4. Filtro de 5 dias mantém exatamente esses 5 dias em todas as consultas e, quando houver conciliação, em todos os indicadores. Até lá, não renderizar saldo oficial.
5. Histórico aceita 90 dias inclusivos e rejeita 91; `null`, `missing`, `unrecognized`, `running` e falha de fonte nunca aparecem como 0h conciliado.
6. Atualização normal é 7 dias e escopo do solicitante; `full=true` só para administração. Dois cliques não disparam duas solicitações. `sync_already_running` de outro usuário não inicia polling impossível.
7. Sucesso parcial identifica a fonte que falhou; tentativa falha preserva registros já exibidos e não afirma último sucesso. `receivedCount=0` em tentativa normal não vira “sem usuários”.
8. Responsável da atividade é quem recebe a hora Monday, não `startedByUserId`; UI não faz heurística por nome nem mistura identidades iguais.
9. Convite é descrito como enfileirado. Aceite inválido/expirado, promoção que removeria o último coordenador, 401, 403, 409, 429 e erro de rede têm estados próprios.
10. Validar `pnpm lint`, `pnpm test`, `pnpm build`, testes Playwright e, para qualquer alteração de bridge/rotas, testes do host desktop. Testes de contrato não substituem homologação Monday/VR com credenciais e dados autorizados.

## 10. Prompt de transferência para o agente do `CEP-FRONT`

> Implemente o front do CEP Horas conforme `CEP-API/docs/guia-telas-frontend-cep-horas.md` e a especificação funcional do produto. Antes de alterar código, leia `CEP-FRONT/AGENTS.md`, `docs/produto/especificacao-funcional.md`, `docs/compatibilidade-backend.md`, `README.md` e o snapshot OpenAPI real. A branch atual do backend tem consulta/histórico bruto, escopo de Membro/Líder/Coordenador e sincronização normal de 7 dias com persistência idempotente e retenção de 90 dias; **não** oferece conciliação oficial, saldo, notificações ou relatórios. Corrija primeiro a documentação/UI que ainda diz 60 dias e implemente as telas funcionais possíveis agora sem inventar endpoints. Mantenha o front web e o host WPF/WebView2 seguros, preserve o cliente de autenticação, escreva testes de escopo/estados e valide lint, testes, build e desktop. Para telas bloqueadas por backend, entregue a especificação e registre a dependência, sem números fictícios em produção.
