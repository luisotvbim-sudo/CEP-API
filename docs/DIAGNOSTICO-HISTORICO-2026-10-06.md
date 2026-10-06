# Diagnóstico de acesso ao histórico — 06/10/2026

Base produtiva conferida: `5225b914440be13c39f9e248c212fb4fe5e4ac6b`. Investigação autorizada somente de leitura; nenhum vínculo, usuário, fonte, envio ou sessão produtivos foram alterados.

## Causa observada

A consulta encontrou exatamente uma conta User ativa em organização ativa. Sua associação por `WorkforcePerson.UserId` existe na mesma organização, com duas identidades externas ativas e registros importados. Não há TeamAssignment para essa conta nem time ativo na organização. A identificação usa a unicidade desse universo e chaves relacionais; não inferiu identidade externa, titular ou equipe por nome.

`TimeControlAccessService` retorna `VisibleUserIds` vazio para User sem vínculo vigente em time ativo. Tanto GET people quanto GET history usam esse escopo. O Front procura a pessoa da sessão em visiblePeople e, quando ela não vem, mostra o estado vazio e não monta a consulta de histórico.

O acompanhamento pessoal usa `ActiveNotificationRecipients`, que exige conta ativa e associação na mesma organização, mas não exige vínculo em time. Isso explica a diferença entre as telas. Atualizar/importar registros não cria time/vínculo nem amplia autorização.

Não foi uma regressão do deploy: o serviço de autorização permaneceu idêntico ao baseline `cac1914`. A exigência de vínculo para histórico/relatórios já consta no contexto e na especificação vigentes.

## Reprodução PostgreSQL

Teste `MemberHistoryAccessTests.Associated_member_has_personal_overview_but_history_requires_current_team_assignment`: aprovado, 1/1. Fixture com pessoa associada e registro persistido, sem time: overview encontra associação; people e history vazios. Após vínculo explícito criado somente na fixture, a mesma pessoa e o mesmo registro tornam-se visíveis sem reimportação.

Comando: `dotnet test tests/CepApi.IntegrationTests -c Release --filter FullyQualifiedName~MemberHistoryAccessTests --logger trx`. Não chama fontes externas nem usa credenciais produtivas. O TRX local é `history-access-characterization.trx`.

## Correção depende do escopo funcional

- Preservar a regra atual: coordenador define um time ativo e vínculo explícito com vigência. Essa decisão de cadastro não pode ser inventada pelo executor.
- Se for aprovada consulta própria independente de time: separar acesso ao próprio titular da autorização para terceiros por liderança vigente, conservando organização, conta ativa e associação. Trata-se de alteração funcional de autorização, com atualização de decisão/contrato e regressões de isolamento, não de refatoração sem mudança de comportamento.

Nenhuma alternativa foi aplicada em produção nesta investigação. A mensagem atual mistura ausência de associação com ausência de vínculo; o Front deve acompanhar a decisão funcional. Sua lista people não é recarregada apenas pelo historyRevision após sincronização, o que também precisa ser considerado se houver cadastro alterado em outra sessão.
