# Decisões e pendências — CEP-API

Revisão: 04/10/2026, base de código `b36c6e149b42253b44860d98c6ffe44f98c53dd6`. Este registro distingue regras aprovadas, comportamento observado e lacunas. Não transforma implementação em aprovação de produto ou em comprovação operacional. IDs D-01 a D-13 permanecem na [especificação](conciliacao-horas/especificacao-funcional.md).

## Regras consolidadas

- Dia de negócio São Paulo, semana segunda e sprint dias 1/15; corte único para duas fontes; sem jornada após meia-noite no contrato aprovado.
- Tolerância global inicial de 30 minutos simétricos, comparação em segundos, limite exato permitido. Configurações/agendas globais, dados/destinatários organizacionais.
- API calcula e autoriza. Monday/VR somente no servidor; front apresenta resultados e nulos. Associação por IDs externos, atribuição Monday ao profissional e herança do pai quando cabível.
- Avisos iniciais 10h/11h50/17h em dias úteis; 10h somente ontem civil. Relatório diário não é popup. Recuperação integral dos avisos já gerados quando o cliente volta, sem descartá-los pela idade.
- Web cookie protegido de mesma origem; desktop sessão no host; ativação de convite sem sessão. Grants plugin até 72h são contrato diferente.
- Energia avaliada ao vivo para o solicitante; WPF revalida, Windows executa. PIN dedicado abre cinco minutos individuais; erro HTTP não é offline.

Fontes: [contrato aprovado](conciliacao-horas/contrato-analises-notificacoes.md), [implementação](time-notifications.md), [sessão web](browser-sessions.md), [importação](workforce-admin-integration.md), [energia](power-action-check.md) e [PIN](power-admin-unlock.md).

## Comportamentos que limitam o produto atual

| Tema | Código observado | Consequência |
|---|---|---|
| Transferência de time | `TimeControlAccessService` usa vínculo vigente hoje | Líder atual vê registros anteriores; não há atribuição histórica por time |
| Sobreposição Monday | `TimeAnalysisEngine` deduplica mesma chave e soma sessões distintas | Não prometer duração única de intervalos simultâneos ou ocorrência própria de sobreposição |
| Agregados | Cada total exige todos os dias calculáveis daquele total | Lacuna mantém agregado nulo; não há subtotal certificado ou taxa de cobertura no DTO |
| Histórico | `SummarizeImportedDay` não estende sessões e VR atual fica nulo | Snapshot importado não é decisão atual nem prova de cobertura |
| Relatórios | GET analyses lista banco | Consultar não reavalia fontes nem resolve ocorrências |
| Agendador | Recupera slots do mesmo dia, não dias anteriores nunca enfileirados | Interrupção do servidor pode deixar lacunas de execução; pendentes já geradas continuam recuperáveis |
| Plugin grant | `installationId`/versão são validados/auditados; grant não inclui vínculo criptográfico ao dispositivo | Não prometer proteção por hardware ou revogação offline imediata |

## Pendências preservadas

| ID | Próxima definição ou validação |
|---|---|
| D-01 | Retroatividade/reprocessamento, vigência e classificações adicionais; snapshots não devem mudar silenciosamente |
| D-02 | Homologar travessia de dia e casos reais; não criar jornada noturna por inferência |
| D-03 | Política de sessões simultâneas, atividades compartilhadas e novas categorias/boards |
| D-04 | Cobertura/campos/semântica das fontes reais; total oficial VR e atribuição Monday precisam de validação com dados autorizados |
| D-05 | Acesso após transferência, atribuição histórica por time e futura decisão/exportação de períodos anteriores |
| D-06 | Prazo de lançamento, limite de atualidade e recuperação de execução perdida em outro dia |
| D-07 | Casos/justificativas, mudança após fechamento, reabertura e competência mensal |
| D-08 | Feriados/férias/afastamentos/escalas e política de reuniões/treinamentos/dias dispensados |
| D-09 | Prazos/escalonamento/delegação e mensagens livres sem análise; eventual restrição de envio manual no fim de semana |
| D-10 | Planilha/PDF, impressão, colunas e comentários exportados |
| D-11 | Retenção/remoção de relatórios/notificações/auditoria/casos/anexos e prova de backup/restore externo |
| D-12 | Download/impressão no WebView e homologação Windows do instalador/atualizador na versão responsável |
| D-13 | Metas mensuradas de desempenho, carga e disponibilidade |

Versão efetivamente implantada, jobs/timers ativos, SMTP real, cobertura Monday/VR, restauração externa e homologação Windows exigem evidências operacionais. Health público não responde essas perguntas. Indisponibilidade dos chats antigos não impede análise do código; justificativa não documentada permanece lacuna, sem aprovação inventada.

A [fila transversal](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues) registra prioridade, dono, dependências e aceite. Pendência não é autorização automática para mudança funcional. Novas decisões devem ficar versionadas junto à entrega.
