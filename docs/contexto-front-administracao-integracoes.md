# Contexto para o front-end — Administração Monday e VR Mais

Atualizado em 23/09/2026.

Este documento é o handoff para o front-end administrativo do **CEP Horas**. A especificação funcional e o OpenAPI da branch atual prevalecem sobre referências históricas de branch ou commit.

O contrato detalhado do backend também está em [`workforce-admin-integration.md`](workforce-admin-integration.md). Em caso de divergência, o OpenAPI e o código atual da API prevalecem.

## 1. Objetivo desta etapa

Construir a área em que um administrador da organização consegue:

1. sincronizar os cadastros e registros do Monday e do Ponto VR Mais;
2. visualizar separadamente o resultado de cada fonte;
3. localizar pessoas ainda não associadas;
4. confirmar qual identidade do Monday corresponde a qual identidade do VR Mais;
5. informar nome e e-mail e enviar o convite de acesso;
6. acompanhar pessoas já associadas e o estado do convite;
7. consultar o histórico bruto persistido, limitado a 90 dias.

Não implementar ainda conciliação diária, justificativas, ranking, cálculo trabalhista ou edição de dados nas plataformas externas.

## 2. Regras que não podem ser alteradas pelo front

- Mutações administrativas e sincronização com `full=true` exigem `organizationAdmin`; `systemAdmin` também pode executá-las após selecionar explicitamente uma organização. A atualização manual dos últimos sete dias e a consulta do próprio estado são permitidas a Membro e Líder autenticados na organização.
- Para `organizationAdmin`, a organização vem do token JWT. Para `systemAdmin`, o front envia `organizationId` em todas as chamadas da organização selecionada. Outros usuários não podem alterar esse escopo.
- Os tokens do Monday e do VR Mais ficam somente na VM. O front não recebe, armazena ou solicita essas credenciais.
- O front chama somente a CEP API. Nunca chamar Monday ou VR Mais diretamente.
- Uma pessoa só é criada depois de o administrador selecionar explicitamente uma identidade de cada fonte.
- Nunca associar automaticamente somente por nome ou e-mail. Nomes iguais e dados divergentes precisam de confirmação humana.
- Os IDs enviados no convite são os campos internos `id` das identidades, não `externalId`.
- O histórico aceita no máximo 90 dias inclusivos, respeitando sempre o escopo da pessoa autenticada.
- A retenção física do backend é de 90 dias, com remoção dos registros antigos após sincronização bem-sucedida da respectiva fonte.
- Datas são enviadas como `YYYY-MM-DD`; instantes são ISO 8601.
- JSON e enums são serializados em `camelCase`.
- `null` significa desconhecido ou não calculável e nunca deve virar zero silenciosamente.

## 3. Ambientes e contrato

API local:

```text
http://127.0.0.1:8080
```

Swagger local:

```text
http://127.0.0.1:8080/swagger
http://127.0.0.1:8080/swagger/v1/swagger.json
```

Produção não expõe Swagger. O agente do front deve consultar o OpenAPI local antes de criar ou alterar tipos e não deve inventar rotas.

Todas as chamadas protegidas usam:

```http
Authorization: Bearer <accessToken>
```

Erros seguem `application/problem+json`:

```ts
export type ApiProblem = {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
};
```

Mostrar uma mensagem em português e preservar o `correlationId` em uma área copiável para suporte. Não mostrar stack trace.

## 4. Autenticação e autorização

Se o front ainda não tiver autenticação, usar:

```http
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "admin@empresa.com",
  "password": "...",
  "client": {
    "type": "web-admin",
    "version": "<versão do front>"
  }
}
```

Resposta relevante:

```ts
export type UserRole = "systemAdmin" | "organizationAdmin" | "user";

export type AuthUser = {
  id: string;
  displayName: string;
  email: string;
  organizationId: string | null;
  role: UserRole;
  status: "active" | "suspended" | "archived";
  products: ("revit" | "zwcad")[];
};

export type TokenResponse = {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser;
};
```

Rotas auxiliares:

- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `GET /api/v1/me`

Criar um guard de rota: `organizationAdmin` entra na própria organização e `systemAdmin` entra após selecionar uma organização. Em `401`, tentar uma única renovação de sessão, com refresh em voo único; se falhar, limpar a sessão e voltar ao login. Em `403`, mostrar “Você não tem permissão para acessar esta área”.

## 5. Estrutura recomendada das telas

Criar uma área `Administração > Integrações e pessoas`, dividida em quatro abas:

1. **Visão geral**
2. **Correspondências**
3. **Pessoas**
4. **Histórico**

Em desktop, usar cabeçalho com título, descrição curta e o estado da última sincronização. Manter o botão principal `Sincronizar agora` visível nas três primeiras abas.

### 5.1. Visão geral

Exibir:

- estado geral da última sincronização;
- horário de início e conclusão;
- um card para Monday;
- um card para VR Mais;
- cobertura de datas de cada fonte;
- quantidades recebidas, criadas, atualizadas e desativadas;
- quantidades de registros de horas recebidos, criados, atualizados e removidos;
- erro independente de cada fonte, quando houver;
- botão `Sincronizar agora`;
- ação secundária `Reprocessar últimos 90 dias` com confirmação, visível somente para coordenador/administração.

Não esconder uma falha parcial. Monday pode funcionar e VR Mais falhar, ou o contrário.

### 5.2. Correspondências

Exibir duas listas lado a lado:

- identidades Monday ainda não associadas;
- identidades VR Mais ainda não associadas.

Cada item deve mostrar nome, e-mail quando existir, ID externo, situação ativa/inativa e data da última visualização. A seleção de uma identidade em cada lista habilita o painel de confirmação.

No painel de confirmação:

- mostrar as duas identidades selecionadas;
- destacar divergências de nome e e-mail sem bloquear automaticamente;
- preencher `displayName` e `email` apenas como sugestão editável;
- exigir confirmação do administrador;
- botão final: `Associar e enviar convite`.

Após sucesso, remover as duas identidades das listas pendentes e adicionar a pessoa à aba Pessoas.

### 5.3. Pessoas

Tabela com:

- nome;
- e-mail;
- identidade Monday;
- identidade VR Mais;
- estado do convite;
- situação da conta;
- criado em;
- ação de detalhes.

Derivação visual do estado:

- `userId != null` ou `invitationAcceptedAt != null`: **Cadastro concluído**;
- convite sem aceite e ainda dentro de `invitationExpiresAt`: **Convite pendente**;
- convite sem aceite e expirado: **Convite expirado**.

Se for necessário reenviar convite expirado, usar `POST /api/v1/organization/invitations/{invitationId}/resend`. Não criar uma nova associação.

### 5.4. Histórico

Filtros:

- data inicial;
- data final;
- pessoa;
- fonte (`monday` ou `vrMais`);
- busca por nome/e-mail;
- botão `Consultar`.

Iniciar com os últimos 7 dias e impedir no cliente intervalos maiores que 90 dias. A API continua sendo a validação definitiva.

Agrupar por pessoa e depois por dia. Mostrar fonte, duração, estado, início, fim, título e última sincronização. Abrir `url` apenas quando for HTTPS. `detailsJson` deve ser interpretado defensivamente; nunca inserir conteúdo bruto como HTML.

Esta entrega apresenta registros brutos. Não calcular ou rotular automaticamente diferença, hora extra, falta, produtividade ou irregularidade.

## 6. Endpoints administrativos

### 6.1. Sincronização

```http
GET /api/v1/organization/time-control/synchronizations/latest
POST /api/v1/organization/time-control/synchronizations
POST /api/v1/organization/time-control/synchronizations?full=true
GET /api/v1/organization/time-control/synchronizations/{batchId}
```

`POST` aguarda a sincronização terminar e retorna o lote final. Se a tela encontrar um lote `running` iniciado por outro cliente, consultar o endpoint por ID a cada 2 segundos até estado terminal, interrompendo o polling ao desmontar a tela.

Estados:

```ts
export type WorkforceSource = "monday" | "vrMais";
export type SyncStatus = "running" | "succeeded" | "partiallySucceeded" | "failed";

export type WorkforceSyncSource = {
  source: WorkforceSource;
  status: SyncStatus;
  receivedCount: number;
  createdCount: number;
  updatedCount: number;
  deactivatedCount: number;
  timeRecordReceivedCount: number;
  timeRecordCreatedCount: number;
  timeRecordUpdatedCount: number;
  timeRecordRemovedCount: number;
  completeSnapshot: boolean;
  coverageFrom: string | null;
  coverageTo: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  startedAt: string;
  completedAt: string | null;
};

export type WorkforceSync = {
  id: string;
  status: SyncStatus;
  startedAt: string;
  completedAt: string | null;
  sources: WorkforceSyncSource[];
};
```

Regras de UX:

- `GET latest` com `404` e código `sync_not_found` é estado vazio, não falha da página;
- desabilitar botões enquanto o `POST` estiver pendente;
- `409 sync_already_running`: informar que já existe uma sincronização e carregar `latest`;
- `409 sync_scope_empty`: informar que não há identidades ativas associadas nas duas fontes para as pessoas do escopo; orientar contato com o coordenador;
- `partiallySucceeded`: usar alerta amarelo e mostrar qual fonte falhou;
- `failed`: manter o último conteúdo persistido visível e mostrar erro;
- erro Monday de coluna Responsável ausente/ambígua ou múltiplos responsáveis: encaminhar ao coordenador, sem apresentar totais como completos;
- “reprocessar” sempre pede confirmação porque pode consumir mais tempo e chamadas externas.

### 6.2. Identidades externas

```http
GET /api/v1/organization/time-control/external-identities
```

Query params:

```ts
type ExternalIdentityQuery = {
  source?: WorkforceSource;
  activeOnly?: boolean; // padrão true
  mapped?: boolean;
  search?: string;
  page?: number;        // mínimo efetivo 1
  pageSize?: number;    // 1..200, padrão 50
};
```

Resposta:

```ts
export type PagedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
};

export type ExternalWorkforceIdentity = {
  id: string;
  source: WorkforceSource;
  externalId: string;
  displayName: string;
  email: string | null;
  isActive: boolean;
  lastSeenAt: string;
  workforcePersonId: string | null;
};
```

Para as listas pendentes, usar `activeOnly=true&mapped=false` e uma requisição separada por fonte. Aplicar debounce de 300–500 ms na busca e cancelar requisições anteriores.

### 6.3. Associar e convidar

```http
POST /api/v1/organization/time-control/people/invitations
Content-Type: application/json

{
  "email": "pessoa@empresa.com",
  "displayName": "Pessoa da Silva",
  "mondayIdentityId": "uuid-interno",
  "vrMaisIdentityId": "uuid-interno"
}
```

Resposta `201`:

```ts
export type Invitation = {
  id: string;
  email: string;
  role: "user";
  canUseRevit: boolean;
  canUseZwcad: boolean;
  expiresAt: string;
  acceptedAt: string | null;
  revokedAt: string | null;
};

export type WorkforcePerson = {
  id: string;
  userId: string | null;
  displayName: string;
  email: string;
  monday: ExternalWorkforceIdentity;
  vrMais: ExternalWorkforceIdentity;
  invitationId: string | null;
  invitationExpiresAt: string | null;
  invitationAcceptedAt: string | null;
  createdAt: string;
  updatedAt: string;
};

export type InviteWorkforcePersonResponse = {
  person: WorkforcePerson;
  invitation: Invitation;
};
```

Erros de negócio que precisam de tratamento específico:

| Código | Mensagem de interface sugerida |
|---|---|
| `external_identities_required` | Selecione uma pessoa no Monday e outra no VR Mais. |
| `invalid_display_name` | Informe o nome da pessoa. |
| `email_domain_not_allowed` | O domínio deste e-mail não está autorizado. |
| `external_identity_not_found` | Uma das identidades não está mais disponível. Atualize a lista. |
| `external_identity_inactive` | Uma das identidades está inativa. Sincronize novamente. |
| `external_identity_already_mapped` | Uma das identidades já foi associada por outro administrador. |
| `email_unavailable` | Este e-mail já possui usuário ou convite pendente. |

### 6.4. Pessoas associadas

```http
GET /api/v1/organization/time-control/people?search=&page=1&pageSize=50
```

Retorna `PagedResponse<WorkforcePerson>`. `pageSize` aceita de 1 a 200.

### 6.5. Histórico

```http
GET /api/v1/organization/time-control/history
    ?from=YYYY-MM-DD
    &to=YYYY-MM-DD
    &workforcePersonId=<uuid-opcional>
    &source=monday|vrMais
    &search=<texto-opcional>
```

Resposta:

```ts
export type WorkforceTimeRecord = {
  id: string;
  source: WorkforceSource;
  externalKey: string;
  workDate: string;
  startedAt: string | null;
  endedAt: string | null;
  durationSeconds: number | null;
  state: string;
  title: string | null;
  url: string | null;
  detailsJson: string | null;
  lastSyncedAt: string;
};

export type WorkforcePersonHistory = {
  workforcePersonId: string;
  userId: string | null;
  displayName: string;
  email: string;
  records: WorkforceTimeRecord[];
};

export type WorkforceAdminHistory = {
  from: string;
  to: string;
  generatedAt: string;
  people: WorkforcePersonHistory[];
};
```

Erros de período:

- `invalid_history_period`
- `history_period_too_large`

## 7. Comportamentos de sincronização que a interface deve explicar

- Primeira sincronização administrativa sem diretório persistido: carga de 90 dias.
- Atualização normal: intervalo inclusivo de sete dias, hoje mais os seis dias anteriores.
- Membro atualiza os próprios dados; Líder, seus dados e os dos membros dos times vigentes; Coordenador, todas as pessoas associadas da organização.
- Organizações com cobertura antiga de 60 dias precisam de `full=true` administrativo para completar até 90 dias.
- `full=true`: reprocessa os últimos 90 dias, não todo o histórico, e é restrito ao coordenador/administração.
- Atualizações normais usam identidades associadas e ativas do banco; não recarregam os diretórios externos.
- O VR Mais consulta apenas os funcionários e dias do escopo. O Monday filtra itens/subitens por responsável na origem e depois limita localmente as sessões do cronômetro aos sete dias; sua API não permite filtrar essas sessões por data.
- Horas do Monday pertencem ao responsável único da atividade/subitem, mesmo que outra pessoa acione o cronômetro.
- Membro e Líder só consultam o estado das próprias solicitações. Uma tentativa de sete dias não representa a cobertura da organização inteira.
- O backend faz upsert idempotente; repetir não deve duplicar horas.
- `sync_scope_empty` indica que não há identidades ativas associadas às pessoas visíveis ao solicitante.
- Registros com mais de 90 dias são removidos após sucesso da respectiva fonte.
- Uma fonte pode ter sucesso e a outra falhar.
- Uma identidade ausente só é desativada quando a fonte declarou um retrato completo.

## 8. Estados obrigatórios da interface

Implementar e testar:

- carregamento inicial;
- nenhuma sincronização executada;
- sincronização em andamento;
- sucesso completo;
- sucesso parcial;
- falha total;
- lista vazia de identidades pendentes;
- busca sem resultado;
- seleção de apenas uma das duas fontes;
- convite enviando, enviado e com erro;
- conflito porque outra sessão associou a identidade;
- tabela de pessoas vazia, carregando e paginada;
- histórico vazio;
- histórico com dados desconhecidos (`null`);
- erro `401`, `403`, `409`, `429` e erro inesperado;
- rede indisponível com ação de tentar novamente.

Nunca deixar uma aba em branco e nunca apagar dados já exibidos apenas porque uma atualização falhou.

## 9. Direção visual e acessibilidade

- Aplicação administrativa clara, inspirada na organização de boards do Monday, sem copiar a marca.
- Fundo creme muito claro, cards brancos, laranja para ações principais e cinzas quentes para navegação e texto.
- Situações devem combinar cor, ícone e texto; nunca depender apenas de cor.
- Botões destrutivos ou de alto custo precisam de confirmação.
- Tabelas devem funcionar em janela redimensionável, com rolagem horizontal quando necessário.
- Toda ação deve funcionar por teclado, com foco visível.
- Usar `aria-live` para informar conclusão de sincronização e envio de convite.
- Respeitar `prefers-reduced-motion`.
- Formatar datas e números com `pt-BR`, preservando valores ISO na camada de API.
- Durações devem ser formatadas a partir de segundos e podem ultrapassar 24 horas.

## 10. Organização técnica recomendada

Separar ao menos:

```text
features/workforce/
  api/
  model/
  pages/
  components/
  hooks/
  formatters/
```

Regras:

- uma única camada HTTP tipada;
- componentes não fazem `fetch` diretamente;
- filtros serializados em uma função testada;
- cache de servidor pode usar a solução já adotada pelo front, mas mutações precisam invalidar `latest`, identidades pendentes, pessoas e histórico;
- cancelar buscas e polling ao desmontar;
- não registrar tokens de sessão, respostas completas ou `detailsJson` no console de produção;
- não usar dados fictícios silenciosamente: mocks devem estar isolados e identificados como demonstração.

## 11. Critérios de aceite

O front desta etapa está pronto quando:

1. `organizationAdmin` acessa a própria organização e `systemAdmin` somente a organização selecionada;
2. a última sincronização e os resultados por fonte são compreensíveis;
3. sincronização normal e reprocessamento completo têm fluxos distintos;
4. uma falha parcial não é exibida como sucesso total;
5. o administrador pesquisa e seleciona explicitamente uma identidade de cada fonte;
6. nenhuma correspondência é criada automaticamente por nome;
7. o convite é enviado com os IDs internos corretos;
8. conflitos retornados pela API atualizam as listas sem perder o restante da tela;
9. pessoas associadas e estado do convite são exibidos;
10. o histórico respeita 90 dias e preserva valores `null`;
11. nenhuma credencial de Monday ou VR Mais aparece no bundle, armazenamento, logs ou rede do front;
12. estados de vazio, carregamento, erro e sucesso possuem testes;
13. o front passa em lint, testes e build de produção;
14. o agente valida os contratos finais contra o Swagger local.

## 12. Prompt pronto para o agente do front

> Implemente a área administrativa Monday + VR Mais seguindo integralmente `docs/contexto-front-administracao-integracoes.md`. Antes de codar, leia o OpenAPI local em `http://127.0.0.1:8080/swagger/v1/swagger.json` e adapte os tipos à arquitetura existente do repositório CEP-FRONT. Crie as abas Visão geral, Correspondências, Pessoas e Histórico, com autenticação e guard para `organizationAdmin`. Não chame Monday ou VR Mais diretamente, não inclua credenciais, não associe pessoas automaticamente por nome e não invente endpoints. Implemente estados de carregamento, vazio, sucesso parcial, erro e conflito; escreva testes dos fluxos críticos e valide lint, testes e build ao terminar.
