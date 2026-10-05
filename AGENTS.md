# Orientações para CEP-API

Este repositório contém autenticação e administração multiempresa, autorização dos plugins Revit/ZWCAD e backend do CEP Horas. Antes de trabalhar, leia [README](README.md), [contexto atual](docs/CONTEXTO-ATUAL.md), [especificação funcional](docs/conciliacao-horas/especificacao-funcional.md) e [decisões e pendências](docs/DECISOES-PENDENCIAS.md). O [índice](docs/README.md) aponta os contratos especializados.

## Fontes e limites

- Confira branch, commit, alterações locais e base do Front antes de integrar contratos. Código demonstra comportamento implementado; especificação registra intenção funcional. Divergência deve ser registrada e resolvida, sem inventar aprovação.
- OpenAPI gerado e `docs/openapi-current.json` definem o contrato HTTP. Mudança exige regenerar snapshot, alinhar tipos/cliente/allowlist do Front e registrar compatibilidade de publicação.
- Main, branch, CI aprovada e produção são estados diferentes. Health check não prova SHA implantado, execução de jobs ou cobertura das fontes. Não transformar relatos antigos ou testes locais em estado operacional atual.
- Trabalho transversal, decisões e dependências ficam no [orquestrador](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR) e na [fila oficial](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues). Registre entregas no GitHub; o contexto de retomada não deve depender de chats ou caminhos de um computador.

## Fronteiras obrigatórias

- Autorização e isolamento por organização são revalidados no servidor. Configurações/agendas globais não tornam pessoas, destinatários ou relatórios globais. SystemAdmin seleciona organização nas rotas organizacionais; rotas pessoais não aceitam trocar o titular.
- Período, fuso São Paulo, tolerância, destinatários e fórmulas pertencem à API. `TimeAnalysisEngine` concentra cálculo; controllers, jobs, React e WPF não mantêm fórmulas paralelas. Desconhecido permanece nulo.
- Monday e VR Mais são acessados somente pelo backend. Associação utiliza IDs externos; nome não é identidade. Não inferir direção de batida inválida, cobertura de fonte, folga ou autoria de timer.
- Web usa mesma origem, refresh protegido em cookie e access em memória. Desktop protege tokens no host nativo; não fornecer tokens ao React. Refresh é serializado; resposta perdida exige login sem replay automático.
- Energia: API decide, WPF revalida e serviço Windows executa. HTTP de erro ou análise inconclusiva não equivale a transporte offline. PIN é dedicado, global, armazenado como hash e abre janela individual limitada pelo relógio do servidor.
- Email usa outbox transacional protegida; enfileirar não comprova entrega. Receber e ler notificação são estados distintos e não encerram ocorrências.
- Migrations são versionadas e explícitas. Alteração de schema exige backup validado, janela revisada e compatibilidade de rollback; voltar imagem não reverte migration.
- Não versionar senhas, PINs, códigos, tokens, chaves privadas, conexões reais, dados pessoais ou inventário da VM. Use configuração externa; logs/erros não podem expor segredos.

## Desenvolvimento e validação

Mantenha regras de domínio em `src/CepApi.Domain`, contratos em `src/CepApi.Application`, integração/persistência em `src/CepApi.Infrastructure` e orquestração HTTP/autorização em `src/CepApi.Api`. Leia o código responsável antes de alterar uma regra compartilhada.

Execute validações proporcionais ao impacto: build e unidades para comportamento; PostgreSQL real para persistência, concorrência/isolamento e migrations; stack HTTPS para proxy/sessão/deploy. Conferir modelo sem migration pendente e regenerate OpenAPI em mudança de contrato. Documentação simples não exige repetir testes da aplicação. Relate exatamente o que foi executado, suas limitações e o que ainda necessita homologação.

Ao concluir, atualize contexto/contrato afetado, registre requisito ou decisão e entregue commit/PR, base, validação e dependências. Não reintroduza planos/handoffs superados nem trate capacidade planejada como implementada.
