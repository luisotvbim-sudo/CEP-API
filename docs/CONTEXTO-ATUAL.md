# Contexto atual — CEP-API

Política de senha alterada por solicitação do responsável em 05/10/2026: mínimo de 6 e máximo de 200 caracteres para ativação, recuperação e troca. Identity usa mínimo de 6, sem exigir dígitos, maiúsculas, minúsculas ou símbolos. Front e mensagens de convite acompanham a regra. Publicar API antes do cliente; sem migration ou troca das senhas existentes.

Revisão: 04/10/2026. Comportamento conferido na base `b36c6e149b42253b44860d98c6ffe44f98c53dd6`; alterações desta revisão são documentação. Não identificar esse SHA como produção sem prova operacional. Para retomada: leia [instruções](../AGENTS.md), [índice](README.md), [especificação](conciliacao-horas/especificacao-funcional.md) e [decisões/pendências](DECISOES-PENDENCIAS.md).

## Aplicação e fronteiras

A solução separa domínio, contratos, infraestrutura e API. CEP Horas compara Monday com VR Mais; administração e grants dos plugins compartilham autenticação multiempresa, mas autorização offline de plugin não é sessão offline do CEP Horas. React e WPF consomem resultado; fórmulas, período, tolerância e destinatários pertencem ao backend.

| Área | Entrada de código | Responsabilidade |
|---|---|---|
| Inicialização | `src/CepApi.Api/Program.cs` | API, health/JWKS e comandos explícitos migrate/bootstrap-admin/configure-power-pin |
| Organização | `src/CepApi.Api/Authorization/OrganizationScopeService.cs` | Validar organização; SystemAdmin seleciona contexto nas rotas organizacionais |
| Pessoa/time | `src/CepApi.Api/Authorization/TimeControlAccessService.cs` | Escopo pelos vínculos vigentes hoje em times ativos |
| Sessão | `src/CepApi.Api/Startup/JwtAuthenticationRegistration.cs`, `src/CepApi.Api/WebSessionCookie.cs` e `src/CepApi.Api/Services` | JWT, status/organização/segurança/família, refresh e cookie web |
| Domínio de análise | `src/CepApi.Domain/TimeAnalysis.cs` | Calendário São Paulo, períodos, corte, totais, diferença e qualidade |
| Fontes/persistência | `src/CepApi.Infrastructure/Services` e `src/CepApi.Infrastructure/Persistence` | Normalização, importação, leitura atual, locks, snapshots e filas |
| HTTP | `src/CepApi.Api/Controllers` | Contrato/validação/autorização; não manter fórmula paralela |

O [OpenAPI](openapi-current.json) da base revisada tem 52 caminhos/72 schemas; é necessário regenerá-lo após mudanças HTTP. O Front deve atualizar seus snapshots, tipos, cliente e allowlist nativa na mesma entrega de contrato.

## Autenticação e autorização

Native/plugins usam access JWT RS256 e refresh rotativo. Bearer revalida conta ativa, organização, papel, security stamp e família de sessão no banco. Logout/revogação invalidam a família; troca/recuperação de senha invalida sessões. Reutilização de refresh revoga a família; renovação concorrente/resposta perdida não deve repetir o token antigo.

Web tem rotas login/refresh/logout próprias. Refresh fica no cookie `__Host-cep-session`, HttpOnly, Secure, SameSite Strict, Path=/, sem Domain, protegido por Data Protection. Expiração absoluta é de até sete dias desde login; rotação não amplia. Operações exigem marcador e Origin de mesma origem. Front guarda access apenas em memória e serializa operações entre abas. Desktop guarda sessão no host com DPAPI, sem tokens no React. Contratos: [web](browser-sessions.md), [plugins](plugin-integration.md), [admin global](system-admin-access.md).

O líder consulta união dos membros vigentes hoje dos times ativos que lidera e próprios dados. O membro consulta próprios dados quando há vínculo vigente. Sem vínculo pode haver resultados vazios. Registros antigos usam o mesmo escopo atual; não existe atribuição das horas ao time histórico. Admin da organização tem acesso completo dentro dela. Configurações globais não ampliam autorização.

## Pessoas, convites e importação

Convite com download em 06/10/2026: `SecurityEmailTemplate` inclui boas-vindas ao CEP Horas pela organização, ativação com senha de 6 a 200 caracteres, instalação MSI com apoio da TI e login posterior. `Email__DesktopDownloadUrl` aponta por padrão a `https://plugincep.com.br/download`, sem acrescentar dados do convite. Publicação depende de página e MSI versionado acessíveis; HTML/texto e HTTPS são validados sem envio real. Recuperação, outbox e contrato HTTP permanecem iguais. [Entrega e prévia](convite-download-msi.md).

Identidades Monday/VR são persistidas por fonte e ID externo. Associação exige duas identidades ativas, da mesma organização e ainda não utilizadas. Nome não define identidade. Convite, associação, outbox e auditoria são transacionais. Ativação `/auth/invitations/activate` retorna 204 sem sessão; endpoint legado accept retorna tokens. Código vale 48h com limite de tentativas; reenvio administrativo substitui o anterior e preserva associação. Email colocado na fila não significa entregue.

Sincronização normal lê hoje mais dezesseis dias, somente identidades ativas associadas do escopo. Carga inicial administrativa ou `full=true` cobre 90 dias e recarrega diretórios; membro/líder não executa full. Uma execução por organização evita concorrência. Fontes têm resultados independentes; falha não elimina a última cópia válida. Retenção dos importados ocorre após sucesso da fonte. `completeSnapshot` refere-se ao período/pessoas solicitados, não necessariamente a toda organização.

Monday atribui sessão ao profissional único do item/subitem. R.T. e iniciador do timer não definem o titular. Subitem sem profissional próprio herda do pai; ambiguidade impede atribuição presumida. A origem filtra responsáveis e a aplicação recorta sessões por datas após leitura. VR recebe IDs e período, com consultas limitadas. [Contrato de importação](workforce-admin-integration.md).

Correção de sincronização em 05/10/2026: fontes coletam independentemente, persistindo progresso sem aguardar a outra. Cada fonte possui prazo de coleta de até 120s para diretório e registros; cancelamento finaliza o estado por token independente. Bootstrap administrativo incompleto é retomado enquanto faltar um dos diretórios. Contrato, limites e códigos estão em [importação](workforce-admin-integration.md); produção depende da integração/implantação da correção.

## Formas de consultar horas

1. **Histórico importado:** consulta somente banco; retorna registros e `days`. Fonte filtrada limita detalhes/dias, mas resumo diário usa ambas as identidades. Não estende timer até agora; VR de hoje é nulo. Falta de linhas não prova fonte completa. [Resumo diário](workforce-daily-history.md).
2. **Relatório persistido:** GET analyses lista snapshots gerados pelo processamento, com corte/versão usados. Não chama fontes nem atualiza resultados anteriores.
3. **Análise atual:** serviço lê ambas as fontes para o mesmo corte e passa dados ao motor. Processamento de avisos e energia usam essa leitura; resultado incompleto não confirma coerência.
4. **Acompanhamento pessoal:** GET próprio com períodos oficiais, classificação do servidor e dias de atenção. Reutiliza a leitura/motor atuais sem gerar relatórios, importar histórico ou autorizar energia. [Contrato aditivo](personal-overview.md), implementado em branch de entrega de 05/10/2026; publicação operacional exige evidência própria.

O motor usa segundos, dia de São Paulo e diferença Monday − VR. Tolerância inicial global é 30min, estritamente acima gera ocorrência. Deduplica mesma chave externa, mas soma sessões distintas simultâneas sem reunir intervalos. Em dia VR fechado usa total oficial com ajustes e valida batidas; no atual soma intervalos válidos até o corte. Timer atravessando fechamento e batidas inválidas impedem conclusão.

Cada total agregado requer todos seus valores diários disponíveis; saldo/divergência absoluta exigem diferença calculável em todos os dias. Uma semana com VR ausente no fim de semana pode ter agregado nulo, pois calendário dispensando dias não existe. Dias e motivos permanecem apresentados. Não substituir lacunas por zero nem prometer cobertura percentual não publicada.

## Notificações

Configuração e agendas são globais/versionadas; relatórios, pedidos e destinatários continuam organizacionais. Administradores autorizados podem editar configurações e enviar no escopo. Automaticamente desativado na criação; habilitação exige homologação.

Worker a cada 30s com lock PostgreSQL. Gera relatório diário do dia anterior inclusive fins de semana. Agendas automáticas: 10h considera somente problemas confirmados de ontem; 11h50/17h comparam hoje no corte; sábado/domingo não têm avisos automáticos. Manual diário/semanal/sprint exige chave idempotente e revalida ator/destinatários antes de processar. Cada execução produz até um aviso consolidado por pessoa.

Slots devidos do mesmo dia podem ser recuperados com corte original; dias anteriores nunca enfileirados não são reconstruídos. Habilitar/alterar configuração não recria slots anteriores à mudança. Avisos existentes são recuperados por todas as páginas pelo cliente, com horário original. Recebida e lida são operações diferentes; nenhuma resolve ocorrência. [Contrato e implementação](time-notifications.md).

## Energia

As rotas pessoais autenticadas avaliam somente o próprio usuário/org ativa, sem aceitar pessoa/organização/período. Janela administrativa ativa é verificada antes das fontes. Fora dela, leitura atual do dia retorna allowed/blocked/indeterminate; inconclusivo não libera. WPF revalida imediatamente antes e serviço Windows executa. Um HTTP 503 continua sendo API acessível, sem contingência de transporte.

PIN dedicado global de seis dígitos abre janela individual exata de cinco minutos para as três ações, por relógio do servidor. Hash salgado, versão, limites persistidos/HTTP e auditoria sem segredo; rotação/security stamp/org/expiração invalidam janela. Provisionamento é interativo sem eco com migration explícita. [Energia](power-action-check.md) e [PIN](power-admin-unlock.md).

## Operação e trabalho seguinte

Compose produção usa PostgreSQL interno, credenciais separadas owner/runtime, API sem root com filesystem readonly, Data Protection persistente e Nginx de borda. Front e API compartilham rede de borda; navegador utiliza proxy de mesma origem. Nightly API é configurado para 02h São Paulo: CI do SHA exato, build, backup, migration e health; rollback de imagem não desfaz schema. Monitor local não envia alertas externos nem reinicia serviços. Scripts presentes não comprovam instalação, timer ativo ou backup externo.

Antes de nova entrega, conferir a base efetiva, requisito/contrato, estado local e mudanças concorrentes. Registrar homologação das fontes, versão implantada, execução de jobs, SMTP e aceite Windows com evidência própria. Calendário, workflow, exportações e política histórica continuam pendentes; [decisões](DECISOES-PENDENCIAS.md) orienta seu escopo. Coordenação e fila: [CEP-ORQUESTRADOR](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues).

Sincronização normal em 05/10/2026: janela móvel inclusiva de 17 dias, hoje mais 16 anteriores no fuso São Paulo. Front apresenta Atualizar sprint; isso não altera os períodos oficiais de análise. Full/bootstrap permanece em 90 dias. Sem alteração de schema ou contrato HTTP estrutural; produção depende de publicação.

Refatoração interna de 06/10/2026: projeções administrativas compartilhadas, análise atual injetada no processador, scheduler separado e arquivos próprios para outbox/dispatcher/worker. Contrato, autorização, motor, locks e migrations preservados. Evidências e limites em [auditoria interna](REFATORACAO-INTERNA-2026-10-06.md).
