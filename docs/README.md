# Documentação atual — CEP-API

Entrada para retomada em qualquer computador. A base funcional da API publicada em 06/10/2026 foi `78a71e4c6d63e618a221dd9b14eb66328ec0efa2`; consulte a [evidência de publicação](RELEASE-API-2026-10-06.md) e confira separadamente qualquer alteração posterior. [README do projeto](../README.md) contém execução/validação e [AGENTS](../AGENTS.md) define as fronteiras de trabalho.

## Leia primeiro

1. [Contexto atual](CONTEXTO-ATUAL.md): aplicação, fluxos, código e limites.
2. [Especificação funcional](conciliacao-horas/especificacao-funcional.md): RN/RF/CA/D, implementação e requisitos planejados.
3. [Decisões e pendências](DECISOES-PENDENCIAS.md): definições consolidadas e lacunas.
4. [OpenAPI](openapi-current.json): rotas/DTOs efetivos da base publicada no snapshot.
5. [Publicação da API em 06/10/2026](RELEASE-API-2026-10-06.md): SHA, imagem, validação e limites.

## Contratos especializados

| Tema | Documento |
|---|---|
| Segurança | [Controles e validação](security-hardening.md) |
| Navegador | [Cookie, mesma origem, prazo e rotação](browser-sessions.md) |
| Plugins/nativo | [Sessão e grant offline](plugin-integration.md) |
| SystemAdmin | [Seleção e isolamento de organização](system-admin-access.md) |
| Domínios | [Permissão de novos convites/cadastros](allowed-email-domains.md) |
| Convites | [Ativação](invitation-activation.md), [convite com download MSI](convite-download-msi.md) e [reenvio](people-invitation-resend.md) |
| Fontes/pessoas | [Importação e associação Monday/VR](workforce-admin-integration.md) |
| Histórico | [Resumo diário importado](workforce-daily-history.md) |
| Telemetria desktop | [Eventos estruturados, escopo, retenção e migration](desktop-telemetry.md) |
| Acompanhamento pessoal | [Consulta atual, situação, períodos e qualidade](personal-overview.md) |
| Análises/avisos | [Regras aprovadas](conciliacao-horas/contrato-analises-notificacoes.md) e [rotas/processamento](time-notifications.md) |
| Energia | [Decisão ao vivo](power-action-check.md) e [PIN temporário](power-admin-unlock.md) |
| Deploy | [Produção](../deploy/README.md), [nightly](../deploy/nightly/README.md) e [monitor](../deploy/monitoring/README.md) |

Não usar este índice como comprovação de versão implantada, fontes homologadas, email entregue, jobs ativos ou aceite Windows. Cada evidência deve indicar data/base/comando/resultado da verificação.

## Documentos substituídos nesta limpeza

O usuário autorizou em 04/10/2026 retirar contextos obsoletos e reescrever a documentação. As versões anteriores ficam recuperáveis no histórico Git; não manter cópias concorrentes como instrução atual.

| Arquivo retirado | Substituto atual e conteúdo preservado |
|---|---|
| `contexto-frontend-cep-horas.md` | [Contexto atual](CONTEXTO-ATUAL.md), [sessão web](browser-sessions.md) e [especificação](conciliacao-horas/especificacao-funcional.md); consulta sem login/mocks operacionais não representa produto atual |
| `contexto-front-administracao-integracoes.md` | [Importação/associação](workforce-admin-integration.md), [convites](invitation-activation.md) e [contexto](CONTEXTO-ATUAL.md); fluxo e segurança preservados |
| `guia-telas-frontend-cep-horas.md` | [Especificação](conciliacao-horas/especificacao-funcional.md), [OpenAPI](openapi-current.json) e contratos especializados; afirmação antiga de análises/avisos inexistentes foi removida |
| `plano-trabalho-cep-front.md` | [Decisões/pendências](DECISOES-PENDENCIAS.md) e [especificação](conciliacao-horas/especificacao-funcional.md); não retomar ordem de entrega datada ou bloqueios já superados |
| `conciliacao-horas/manual.html` | [Especificação](conciliacao-horas/especificacao-funcional.md); jornadas, perfis, regras, estados futuros e aceite úteis preservados sem demonstração com regras antigas |

README, especificação, contrato aprovado e especializados foram revisados para remover estado contraditório, prompts de etapas passadas e relatos de publicação/testes tratados como atuais. Preservam comportamento implementado, decisões e requisitos futuros úteis; não alteram código nem schema.

## Coordenação

Trabalho entre API, Front/instalador e VM: [orquestrador](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR) e [fila de entregas](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues). Apenas referências genéricas de coordenação pertencem a este repositório; registros privados de chats/máquinas e inventário da VM permanecem no local responsável.
