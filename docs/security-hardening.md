# Base de segurança — CEP-API

Revisão de contexto: 04/10/2026, código base `b36c6e149b42253b44860d98c6ffe44f98c53dd6`. Este documento descreve controles versionados e verificações necessárias; não reapresenta relatórios de testes antigos como validação atual.

## Sessões e códigos

- Bearer exige JWT válido, conta/org ativas, papel/organização consistentes com banco, security stamp e família ativa. Rotação normal não invalida access vigente; logout/revogação bloqueiam família e troca/recuperação de senha bloqueia sessões.
- Refresh é serializado no banco e no cliente; reutilização revoga família. Resposta perdida não permite replay automático do token anterior.
- Recuperação conserva apenas código mais recente; consumo/tentativas são serializados, e sucesso muda senha e revoga sessões/códigos na transação.
- Convite tem validade e limite de tentativas; ativação/reenvio são transacionais e auditados, com proteção dos códigos e associação no mesmo contexto organizacional.
- Sessão web usa cookie protegido, HttpOnly/Secure/SameSiteStrict, prefixo Host e prazo absoluto. Rotas exigem marcador/Origin de mesma origem e não podem ser cacheadas. Data Protection precisa persistir.

## Dados e administração

Organização é validada antes dos controllers organizacionais. SystemAdmin seleciona contexto explicitamente; acesso global não transfere dados ou ignora validações. Vínculos vigentes hoje delimitam acesso membro/líder. Consulta histórica não elimina autorização atual.

Alterações administrativas sensíveis, último administrador, convites, refresh, recuperação e PIN usam transações/bloqueios conforme serviço responsável. PIN tem hash salgado, limite por usuário/organização/global persistido e auditoria sem segredo. Não depende das senhas das contas.

Email é enfileirado na transação, com payload protegido e retentativas. SMTP exige TLS em produção. Mensagem aceita pelo SMTP antes de crash pode ser reenviada com mesmo código; outbox não garante email exatamente uma vez. HTTP de sucesso não prova entrega.

## Fontes, notificações e energia

Credenciais Monday/VR ficam exclusivamente no servidor. Não registrar tokens, dados pessoais, PIN, códigos, senhas ou conexões reais em docs/logs. Nome não é chave de associação e atividade com responsável ambíguo não é distribuída por suposição.

Worker usa exclusão mútua e pedidos idempotentes com revalidação de autorização/agenda/configuração. Relatório/notificação individual são persistidos atomicamente; receber/ler são estados distintos. Configuração global não concede acesso global aos destinatários.

Energia inconclusiva não é liberação. Erro HTTP é servidor acessível, não prova transporte offline. API não executa ação no sistema operacional. Token/PIN não chega ao serviço Windows.

## Rede, deploy e persistência

Somente proxies configurados podem definir esquema/IP encaminhado. Rate limiting distingue rotas anônimas por IP/rota e sensíveis por conta. Não habilitar CORS com credenciais para sessão web.

Produção usa Compose separado, PostgreSQL sem porta pública, credenciais owner/runtime distintas, API sem root/read-only, chaves JWT persistentes, volume de Data Protection, limites de recursos e rotação de logs. Secrets montados precisam de permissões mínimas verificadas por UID, sem imprimir conteúdo.

Migrations são explícitas e versionadas. Backup deve ser validado antes da troca e rollback exige schema compatível; voltar imagem não desfaz migration. Backup externo e restauração precisam ser comprovados, inclusive chaves de proteção/assinatura. Monitor local não equivale a alerta externo ou auto recuperação.

## Validação de mudança

Conforme impacto, verificar unidades, integração com PostgreSQL real, isolamento, autorização revogada, concorrência, replay, rollback transacional da outbox, retentativas, proxy e HTTPS, migrations com papel runtime limitado e modelo sem migration pendente. Consulte comandos no [README](../README.md) e stack descartável no [deploy](../deploy/README.md).

Relate base, comandos executados, resultados e limitações na entrega. Resultado antigo, ausência de falhas locais ou health público não garante ausência de vulnerabilidades, desempenho, fontes reais, SMTP, estado da VM ou consumo pelos plugins. A janela offline dos grants permite validade até 72h sem revogação imediata; não atribuir proteção por hardware que o grant não contém.
