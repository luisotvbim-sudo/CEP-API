# Plano de trabalho para o CEP-FRONT

Atualizado em 23/09/2026. Este arquivo registra **o que pretendo fazer em uma próxima etapa**. Ele não indica que as mudanças no front já foram executadas.

## Referências antes de implementar

- Ler as instruções e o estado do `CEP-FRONT`: `AGENTS.md`, `README.md`, `docs/produto/especificacao-funcional.md` e `docs/compatibilidade-backend.md`.
- Usar [o guia de telas do CEP Horas](guia-telas-frontend-cep-horas.md) como roteiro funcional e conferir o contrato efetivo da branch da API. Em caso de divergência técnica, prevalecem o OpenAPI e o código da API que será consumida.
- Se a API local estiver disponível, atualizar primeiro `CEP-FRONT/docs/openapi-backend-current.json` pelo script `scripts/sync-openapi.ps1`. Só substituir o contrato usado pelo cliente e regenerar os tipos quando o front puder consumir integralmente a mudança.

## Sequência pretendida no front

1. Corrigir limites e textos desatualizados: histórico de até **90 dias por requisição**, sincronização normal dos últimos **7 dias** e carga completa `full=true` de até **90 dias**, restrita ao administrador conforme o contrato. Ajustar validações, testes e documentação relacionada.
2. Criar a navegação do usuário comum sem quebrar o login e a sessão existentes: visão pessoal para Membro e visão pessoal mais equipes gerenciadas para Líder. Tratar ausência de vínculo ativo e dados indisponíveis explicitamente.
3. Expor o histórico bruto pessoal e, para o Líder, as equipes, pessoas e históricos que a API autoriza. Usar os filtros e escopos retornados pelo backend; não inferir permissões nem transformar lista vazia em “zero horas”.
4. Disponibilizar a sincronização manual conforme o perfil e completar o fluxo público de aceite de convite. Revisar as telas administrativas existentes de pessoas, equipes, vínculos, integrações e auditoria onde o contrato já oferece suporte.
5. Validar os fluxos com testes e build do front. Se API e PostgreSQL estiverem disponíveis, testar chamadas reais; caso contrário, registrar expressamente que a validação foi feita com contrato e mocks.

## Limites e dependências

- Não calcular ou apresentar como oficiais saldo, diferença Monday × VR Mais, conciliação, divergências, alertas, justificativas ou relatórios: esses recursos ainda dependem de endpoints próprios do backend.
- Não inventar rotas, campos, enums ou permissões. Toda integração com Monday e VR Mais continua passando pela CEP API; tokens externos não vão para o navegador.
- Preservar a arquitetura de autenticação, a separação de deploy e o cliente existente do `CEP-FRONT`.
- Implementação, commit/push e desligamento do computador ficam fora desta etapa, que consiste apenas em registrar o plano neste arquivo.
