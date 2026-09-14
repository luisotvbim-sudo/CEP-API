# Contexto para continuidade do backend de conciliação de horas

Atualizado em 14/09/2026. Este documento reúne o estado necessário para outro agente continuar o trabalho sem depender do histórico do chat. Ele não contém chaves, senhas, tokens, IP residencial nem caminhos de credenciais.

## 1. Objetivo do produto

Construir, neste repositório, o backend multiempresa da aplicação que compara por pessoa e dia/jornada:

- o tempo líquido de trabalho vindo do Ponto VR Mais; e
- as sessões de controle de tempo do Monday no quadro **EG03_MICRO PLANEJAMENTO**.

A aplicação é uma central de conferência e tratamento. Ela não substitui o ponto ou o Monday, não altera horas nas fontes e não deve tratar divergência como prova de ausência, produtividade ou irregularidade trabalhista.

A interface prevista é React dentro do desktop por WebView2. Este repositório contém o backend ASP.NET Core e ainda não contém essa interface.

## 2. Documentos e precedência das decisões

Ler, nesta ordem:

1. [Plano de produto e estudo de viabilidade](plano-produto-estudo.md), que contém as decisões mais recentes e evidências das integrações.
2. [Especificação funcional](especificacao-funcional.md), que define o escopo funcional amplo, regras e critérios de aceite.
3. [Manual proposto](manual.html), que ilustra a experiência esperada, mas não comprova funcionalidades implementadas.

Quando houver conflito, as decisões confirmadas no plano mais recente prevalecem. Em especial:

- o cadastro inicial de funcionários vem do VR Mais;
- o escopo Monday é o quadro EG03_MICRO PLANEJAMENTO e seus subitens;
- cada sessão pertence exclusivamente ao usuário que iniciou o relógio (`started_user_id`);
- não usar como substitutos o responsável atual da atividade, a coluna PROFISSIONAL ou quem encerrou o relógio;
- nomes auxiliam a correspondência inicial, mas o vínculo definitivo usa identificadores estáveis;
- a central corrige somente por nova leitura das fontes; não escreve nelas;
- somar item e resumo de subitens não pode duplicar sessões;
- falha ou carga parcial de uma fonte nunca vira zero;
- uma justificativa aceita encerra o atendimento, mas preserva a diferença numérica.

O plano dizia para aguardar autorização antes de implementar. Depois da criação do documento, o proprietário autorizou explicitamente o início do backend neste chat.

Ainda dependem de homologação:

- significado do total líquido do VR em ajustes, abonos e jornadas especiais;
- população e cobertura histórica completas;
- fuso e tratamento de jornadas noturnas/multidia — `America/Sao_Paulo` é a proposta inicial;
- tolerância diária — 10 minutos é proposta, não regra aprovada;
- prazo de lançamento, frequência de atualização, retenção e acesso histórico.

## 3. Estado do Git

Branch de trabalho publicada:

```text
feature/controle-de-ponto-backend
```

Commits próprios da branch sobre `origin/main`:

```text
5b0094c Configure Resend SMTP deployment
73ef27d Add time control team management
```

Na criação deste documento, `origin/main` apontava para `3a1259e`, que adicionou a especificação e o manual. A `main` remota não contém os dois commits acima. Não desenvolver diretamente na `main` nem implantar a branch sem revisão.

O workflow de CI executa em pushes para `main`, em pull requests e por disparo manual. Um push isolado para a feature branch não inicia CI; abra um PR ou dispare o workflow para obter a validação completa.

## 4. Arquitetura existente

- .NET 10 / ASP.NET Core 10.
- PostgreSQL 17 com Entity Framework Core e migrations explícitas.
- Camadas `Domain`, `Application`, `Infrastructure` e `Api`.
- ASP.NET Core Identity com UUID, JWT RS256, refresh token rotativo e sessões revogáveis.
- Multiempresa por `OrganizationId` presente no usuário e no token.
- Papéis de autenticação existentes: `SystemAdmin`, `OrganizationAdmin` e `User`.
- O papel de gestor de equipe não é um novo `UserRole`: nesta primeira etapa ele é um vínculo efetivo `TeamAssignmentRole.Manager`. A autorização de consultas gerenciais ainda precisa ser implementada usando esse vínculo.
- Auditoria persistida em `audit_events`.
- E-mails por outbox durável protegido com Data Protection.
- API executa em contêiner não privilegiado como UID/GID 1654.

O módulo de controle de ponto deve continuar dentro dessa arquitetura e preservar isolamento por organização em consultas, gravações, relatórios e histórico.

## 5. Primeira etapa implementada na branch

Arquivos principais:

- `src/CepApi.Domain/TimeControlEntities.cs`
- `src/CepApi.Application/TimeControlContracts.cs`
- `src/CepApi.Api/Controllers/TimeControlTeamsController.cs`
- migration `20260914045033_TimeControlTeams`

Entidades:

- `WorkforceTeam`: equipe pertencente à organização, nome normalizado, estado ativo/inativo e datas de criação/alteração.
- `TeamAssignment`: vínculo de uma pessoa com uma equipe como `Member` ou `Manager`, com início e fim de vigência, autoria e histórico.

Garantias implementadas:

- nome de equipe único, sem diferença entre maiúsculas e minúsculas, dentro da organização;
- somente `OrganizationAdmin` administra equipes e vínculos;
- usuário vinculado precisa pertencer à mesma organização e estar ativo;
- equipe precisa estar ativa para receber novo vínculo;
- período final não pode preceder o inicial;
- vínculos de mesma equipe, pessoa e papel não podem se sobrepor;
- repetição idêntica da criação é idempotente;
- bloqueios transacionais serializam nomes de equipe e vínculos concorrentes;
- encerramento preserva a linha histórica em vez de excluí-la;
- criação, alteração e encerramento geram eventos de auditoria.

Endpoints atuais:

```text
GET   /api/v1/organization/time-control/teams
GET   /api/v1/organization/time-control/teams/{teamId}
POST  /api/v1/organization/time-control/teams
PATCH /api/v1/organization/time-control/teams/{teamId}
GET   /api/v1/organization/time-control/teams/{teamId}/assignments
POST  /api/v1/organization/time-control/teams/{teamId}/assignments
PATCH /api/v1/organization/time-control/teams/{teamId}/assignments/{assignmentId}/end
```

As listagens aceitam uma data `asOf`; na ausência, usam a data UTC atual. Não reutilizar essa escolha para conciliação diária: o fuso das jornadas ainda precisa ser homologado.

## 6. Testes e validação

Validado localmente após a implementação:

- `dotnet build CEP-API.sln --no-restore`: sucesso, zero avisos;
- 9 testes unitários: aprovados;
- migration idempotente: SQL gerado;
- `dotnet ef migrations has-pending-model-changes`: nenhuma diferença pendente;
- `git diff --check`: sem erros.

O fluxo de integração foi ampliado para cobrir:

- criação de equipe;
- criação idempotente de vínculo;
- rejeição de período sobreposto;
- consulta por vigência;
- proibição de acesso por usuário comum;
- encerramento do vínculo.

A suíte de integração não rodou localmente porque o Docker Desktop não estava ativo. Os erros observados eram de conexão com `npipe://./pipe/docker_engine`, antes da execução dos testes. A CI deve ser considerada obrigatória antes do merge. O WSL também não estava instalado/ativado neste host; a sintaxe Bash do script de deploy foi validada usando uma cópia temporária na VM.

## 7. Produção e deploy

Produção usa `https://api.cep.lat` atrás da Cloudflare, em uma VM Oracle. Na última verificação:

- imagem da API: commit `3a1259e`;
- API pública: HTTP 200 no health check;
- contêiner da API: `running/healthy`;
- timers de atualização e monitoramento: ativos.

A feature branch não está implantada. O atualizador noturno instala somente a continuidade aprovada da `main` com CI bem-sucedida e executa migration antes de trocar a API. Não aplicar manualmente a migration desta branch em produção.

O commit `5b0094c` atualiza o exemplo de produção para Resend e garante que os segredos SMTP sejam legíveis pelo grupo não privilegiado da aplicação em futuros deploys.

## 8. SMTP e recuperação de senha

Estado operacional já configurado diretamente na VM:

- domínio `cep.lat` verificado no Resend;
- região de envio São Paulo;
- remetente `no-reply@cep.lat`;
- SMTP `smtp.resend.com`, porta 465, TLS direto;
- credencial armazenada somente como secret na VM, com acesso restrito ao grupo da aplicação;
- arquivo temporário local que continha a API Key foi excluído;
- envio real de recuperação para Gmail foi validado de ponta a ponta.

Nunca registrar a API Key no Git, em documentação, logs, comandos visíveis ou mensagens. Se for necessário rotacioná-la, criar uma chave restrita a envio pelo domínio e substituir o secret fora do repositório.

Foi criado manualmente em produção um usuário comum apenas para o teste SMTP. Por privacidade, o endereço não é repetido neste documento. O registro ficou ativo, sem organização, sem privilégios administrativos e inicialmente sem senha; deve ser removido ou regularizado antes de considerar o cadastro de produção consistente. Não apagar sem confirmação do proprietário.

## 9. Segurança e infraestrutura já aplicadas

Antes desta branch, a `main` já recebeu:

- HMAC-SHA256 com segredo separado para códigos de convite e recuperação;
- código de recuperação aleatório de oito dígitos, expiração de 15 minutos e máximo de cinco erros globais por código;
- limitação por rota para login e recuperação;
- resposta resistente à enumeração de e-mail;
- timeouts de banco, logs e papéis PostgreSQL separados;
- Nginx com headers de segurança;
- banco sem porta pública;
- origem HTTPS e acesso público via Cloudflare;
- monitoramento a cada cinco minutos e deploy noturno com backup local e rollback da aplicação.

SSH está restrito ao IP residencial do proprietário com CIDR `/32`. Esse IP pode mudar; alterações na regra devem ser feitas pelo console Oracle após confirmar o IP atual. Não registrar o endereço residencial no repositório.

Não existe backup externo/off-site por enquanto; há apenas uma VM e dumps locais antes do deploy. Isso continua sendo um risco conhecido.

Configurações Cloudflare que estavam pendentes e precisam ser verificadas novamente antes de qualquer alteração:

- regra de rate limit para rotas de autenticação estava preparada, mas não confirmada/publicada;
- modo SSL `Full (strict)`, TLS mínimo 1.2 e redirecionamento permanente para HTTPS ainda aguardavam confirmação explícita;
- não presumir que o estado continua igual sem consultar o painel.

## 10. Próximas etapas recomendadas

### Etapa 2 — correspondência de pessoas

Modelar registros de identidade das fontes e a associação confirmada com `ApplicationUser`:

- fonte `VrMais` ou `Monday`;
- identificador externo estável;
- nome/e-mail de exibição como atributos auxiliares, nunca como chave definitiva;
- estados sem associação, associado, ambíguo, duplicado e inativo;
- vigência, autoria, motivo e histórico;
- unicidade de um identificador externo ativo por fonte/organização;
- fila administrativa sem mistura entre organizações;
- reprocessamento dos dias afetados após correção de vínculo.

O cadastro VR pode existir antes da conta de acesso à central. Não force toda pessoa importada a possuir imediatamente um `ApplicationUser`; avalie separar `Employee` da identidade de login antes de implementar a migration seguinte. Esta é uma decisão arquitetural importante que a primeira etapa ainda não resolveu.

### Etapa 3 — ingestão rastreável e idempotente

- `SyncRun` separado por fonte, com tentativa, último sucesso, cobertura e contadores;
- staging/registros normalizados com chave natural da origem e payload mínimo rastreável;
- sessão Monday identificada pela sessão original, item/subitem, iniciador, início, fim e sinalização manual/aberta;
- registro diário VR preservando referência, total recebido, batidas/intervalos disponíveis e ajustes;
- upsert idempotente e retenção da última leitura válida;
- nenhuma credencial das fontes no frontend ou no Git.

### Etapa 4 — calendário, regra e conciliação diária

- durações armazenadas em unidade inteira de alta precisão, não em `double` ou horas decimais;
- `diferença = Monday - ponto`;
- separar qualidade/aplicabilidade, resultado numérico e tratamento;
- conservar versão/vigência da regra usada;
- excluir dias provisórios/incompletos dos indicadores conclusivos;
- implementar os cenários P-01 a P-28 do plano como testes.

### Etapa 5 — casos, justificativas e notificações

- máquina de estados descrita na especificação;
- concorrência e idempotência em cada transição;
- gestor não aprova o próprio caso;
- justificativa não altera os valores importados;
- atualização das fontes pode resolver ou sinalizar revisão sem apagar o histórico.

### Etapa 6 — consultas e exportação

- escopo pessoal e gerencial calculado no backend;
- ranking por soma das diferenças absolutas diárias;
- cobertura sempre junto da taxa de conciliação;
- filtros compartilhados entre cards, tabelas e exportação;
- planilha do MVP somente após os dados e permissões estarem validados.

## 11. Comandos de trabalho

```bash
dotnet tool restore
dotnet restore CEP-API.sln
dotnet build CEP-API.sln --no-restore
dotnet test tests/CepApi.UnitTests/CepApi.UnitTests.csproj --no-build --no-restore
dotnet test tests/CepApi.IntegrationTests/CepApi.IntegrationTests.csproj --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/CepApi.Infrastructure --startup-project src/CepApi.Api
```

Para gerar migration:

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/CepApi.Infrastructure \
  --startup-project src/CepApi.Api \
  --output-dir Persistence/Migrations
```

## 12. Guardrails para o próximo agente

- Não versionar `.env`, `.key`, `.pem`, tokens, dumps, respostas brutas das APIs ou dados pessoais de funcionários.
- Não operar Monday, VR, Cloudflare, Oracle ou Resend sem autorização dentro da tarefa correspondente.
- Não escrever nas fontes de horas; a primeira versão é somente leitura.
- Não implantar feature branch nem migrar produção antes de CI completa e aprovação.
- Não usar nomes para associação automática definitiva.
- Não inventar zero para fonte falha, parcial ou desconhecida.
- Não somar totais pai/subitem se isso repetir sessões.
- Não criar `UserRole.Manager` sem revisar a autorização existente; gestor hoje é vínculo efetivo de equipe.
- Não misturar as mudanças do arquivo de plano com conclusões de implementação: ele documenta produto e evidências, enquanto este handoff documenta o estado real do código e da operação.
