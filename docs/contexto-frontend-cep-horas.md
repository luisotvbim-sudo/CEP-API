# Contexto para o front-end — CEP Horas

Documento histórico de 20/09/2026.

Este documento preserva o briefing inicial, anterior ao login e ao permissionamento atuais, e não orienta novas implementações. A fonte de verdade é a [`especificação funcional`](conciliacao-horas/especificacao-funcional.md), complementada pelo OpenAPI gerado pela branch atual.

## 1. Objetivo do produto

O CEP Horas permite que uma pessoa compare, por dia e por período, as horas registradas em atividades do **Monday** com as horas registradas no **VR Mais**.

A primeira entrega é uma consulta pessoal simples. Ela deve responder:

- Quantas horas foram registradas em cada fonte?
- Qual é a diferença entre Monday e VR Mais?
- Em quais dias há algo que precisa ser revisto?
- Quais atividades, sessões, batidas e turnos formam cada total?

A aplicação compara registros; uma diferença não comprova falta, baixa produtividade, hora extra ou irregularidade.

## 2. Decisões já tomadas

- O produto é uma aplicação desktop para Windows.
- A interface é React incorporada ao desktop por WebView2.
- O MVP abre diretamente na consulta, **sem tela de login**, porque ainda não há usuários cadastrados.
- A VM define qual funcionário pode ser consultado. O front-end não envia e não permite escolher e-mail, matrícula ou identificador de funcionário.
- Os tokens do Monday e do VR Mais permanecem exclusivamente na VM.
- O front-end nunca recebe, persiste ou exibe esses tokens.
- No computador do usuário ficam apenas os resultados dos períodos que ele consultou.
- O cache local deve ser protegido pelo Windows para o usuário atual, expirar em 24 horas e poder ser apagado pela interface.
- O período máximo de uma consulta é de 31 dias, com datas inclusivas.
- O fuso de apresentação é `America/Sao_Paulo`.
- A convenção da diferença é: **Monday menos VR Mais**.

## 3. Situação real do repositório

A branch `main` possui hoje o backend de organizações, usuários, auditoria e equipes de controle de ponto. Ela ainda **não possui** o endpoint de consulta Monday/VR nem o projeto do desktop.

Para o front funcionar com dados reais, o backend precisa receber antes ou junto da integração:

```http
GET /api/v1/time-logs?from=YYYY-MM-DD&to=YYYY-MM-DD
```

O código desse endpoint e um protótipo desktop foram preparados separadamente, mas não estão aplicados à `main`. Não simular que a integração está disponível em produção e não chamar diretamente as APIs do Monday ou do VR Mais a partir do front-end.

Enquanto o endpoint não estiver disponível, o front pode ser construído com um adaptador de dados mockados que respeite exatamente o contrato deste documento.

### 3.1. Como o agente consulta o Swagger

Com a CEP API executando localmente em modo `Development`, estes endereços ficam disponíveis:

- Interface Swagger: `http://127.0.0.1:8080/swagger`
- Contrato OpenAPI JSON: `http://127.0.0.1:8080/swagger/v1/swagger.json`

O repositório do front mantém também uma cópia em `docs/openapi.json`. Antes de implementar ou alterar uma integração, o agente deve ler esse arquivo e não inventar endpoints, parâmetros ou respostas que não estejam nele.

Para atualizar a cópia usando a API local:

```powershell
.\scripts\sync-openapi.ps1
```

O Swagger consegue iniciar sem PostgreSQL, mas nesse cenário serve apenas para documentação: chamadas que acessam dados falharão. Para testar os fluxos reais, é necessário iniciar o PostgreSQL, aplicar as migrations e então executar a API.

## 4. Arquitetura esperada

```text
React no WebView2
        |
        | mensagens JSON pelo bridge nativo
        v
Host desktop WPF
        |-- consulta a CEP API por HTTPS
        |-- protege o cache com DPAPI/CurrentUser
        |-- aplica expiração de 24 horas
        v
CEP API na VM
        |-- identifica o funcionário fixado na configuração da VM
        |-- usa os tokens mantidos na VM
        |-- consulta Monday e VR Mais
```

No build de produção, os assets do React devem ser empacotados localmente. Não carregar React, fontes, scripts ou estilos por CDN.

O React deve depender de uma interface de dados pequena, e não diretamente de `fetch`, para permitir:

1. um adaptador do bridge WebView2 em produção;
2. um adaptador mockado no navegador durante o desenvolvimento;
3. testes de componentes sem o executável desktop.

Operações mínimas do bridge:

```ts
type DesktopBridge = {
  ready(): Promise<{ mode: "anonymous"; displayName: string }>;
  query(input: { from: string; to: string }): Promise<QueryResult>;
  clearCache(): Promise<void>;
};
```

Não criar operação de login no MVP.

## 5. Contrato de consulta

O endpoint recebe `from` e `to` no formato `YYYY-MM-DD`. As propriedades JSON usam `camelCase` e os enums também são serializados em `camelCase`.

```ts
type TimeLogSession = {
  itemId: string;
  itemName: string;
  itemUrl: string | null;
  startedAt: string;
  endedAt: string | null;
  durationSeconds: number;
  running: boolean;
  manual: boolean;
};

type TimeLogShift = {
  index: number;
  start: string;
  end: string;
  pointSeconds: number;
  mondaySeconds: number | null;
  differenceSeconds: number | null;
};

type TimeLogDay = {
  date: string;
  mondaySeconds: number | null;
  vrSeconds: number | null;
  differenceSeconds: number | null;
  state: "comparable" | "provisional" | "review" | "missing" | "no_records" | string;
  reasons: string[];
  timeCards: string[];
  sessions: TimeLogSession[];
  shifts: TimeLogShift[];
};

type TimeLogResponse = {
  from: string;
  to: string;
  timeZone: string;
  fetchedAt: string;
  days: TimeLogDay[];
  summary: {
    comparedDays: number;
    excludedDays: number;
    mondaySeconds: number | null;
    vrSeconds: number | null;
    differenceSeconds: number | null;
    absoluteDifferenceSeconds: number | null;
  };
  warnings: string[];
};

type QueryResult = {
  source: "api" | "local-cache";
  cachedAt: string;
  warning?: string;
  data: TimeLogResponse;
};
```

Exemplo reduzido:

```json
{
  "source": "api",
  "cachedAt": "2026-09-20T13:10:00Z",
  "data": {
    "from": "2026-09-18",
    "to": "2026-09-18",
    "timeZone": "America/Sao_Paulo",
    "fetchedAt": "2026-09-20T13:10:00Z",
    "days": [
      {
        "date": "2026-09-18",
        "mondaySeconds": 27000,
        "vrSeconds": 28800,
        "differenceSeconds": -1800,
        "state": "review",
        "reasons": [],
        "timeCards": ["08:00", "12:00", "13:00", "17:00"],
        "sessions": [],
        "shifts": []
      }
    ],
    "summary": {
      "comparedDays": 1,
      "excludedDays": 0,
      "mondaySeconds": 27000,
      "vrSeconds": 28800,
      "differenceSeconds": -1800,
      "absoluteDifferenceSeconds": 1800
    },
    "warnings": []
  }
}
```

Valores `null` significam dado desconhecido ou não calculável. Nunca convertê-los para zero.

Erros da API seguem `application/problem+json` e podem conter:

```ts
type ApiProblem = {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
};
```

O front deve mostrar uma mensagem compreensível e, quando existir, preservar o `correlationId` para suporte técnico.

## 6. Telas e componentes do MVP

### Cabeçalho

- Marca `CEP Horas`.
- Área atual `Minha jornada`.
- Identificação discreta como `Consulta direta` ou `Perfil definido na VM`.
- Não mostrar botão “Entrar” ou “Sair” no modo sem autenticação.

### Introdução

- Título: `Seus registros de horas`.
- Texto curto: `Compare as atividades registradas no Monday com as batidas do VR Mais.`
- Selo de consulta protegida.

### Filtro de período

- Campos `De` e `Até`.
- Iniciar com ontem nas duas datas, calculado no fuso de São Paulo.
- Botão `Consultar período`.
- Validar data inicial posterior à final.
- Validar limite máximo de 31 dias antes de enviar.
- Bloquear envio duplicado enquanto a consulta estiver em andamento.

### Resumo do período

Quatro cards:

1. Horas no Monday.
2. Horas no VR Mais.
3. Diferença líquida, com a legenda `Monday menos VR`.
4. Divergência absoluta, sem cancelamento entre dias.

Mostrar também quantos dias foram comparados e quantos foram excluídos.

### Tabela diária

Colunas:

- Data.
- Monday.
- VR Mais.
- Diferença.
- Situação.
- Ação `Detalhes`.

Estados devem usar cor, texto e forma; nunca depender somente de cor. Rótulos iniciais:

| Valor | Rótulo |
|---|---|
| `comparable` | Comparável |
| `provisional` | Provisório |
| `review` | Revisar |
| `missing` | Sem base |
| `no_records` | Sem registros |

### Detalhes do dia

Abrir no contexto da mesma tela, sem perder filtros. Exibir em duas áreas:

- Atividades do Monday: nome, horário inicial/final, duração, manual/em andamento e link HTTPS quando disponível.
- VR Mais: batidas reconhecidas, turnos, total do ponto, Monday alocado ao turno e diferença.

Exibir os motivos de revisão em linguagem humana. Motivos conhecidos inicialmente:

| Código | Texto |
|---|---|
| `open_session` | relógio aberto |
| `cross_midnight_session` | sessão atravessa meia-noite |
| `vr_missing_or_unrecognized` | VR ausente ou não reconhecido |
| `current_or_future_day` | dia atual ou futuro |
| `no_records_in_both_sources` | sem registros nas duas fontes |

Códigos desconhecidos devem ser exibidos de forma segura e não podem quebrar a tela.

### Origem e cache

- Informar `Dados atualizados pela VM` quando `source` for `api`.
- Informar `Cache local protegido` quando `source` for `local-cache`.
- Mostrar a data/hora da consulta na fonte e a data/hora do cache.
- Oferecer a ação `Limpar dados locais`, com confirmação e retorno de sucesso/erro.
- Se a API estiver indisponível e houver cache válido, mostrar o cache com aviso claro.

## 7. Formatação e regras visuais

- Durações devem aparecer como `07:30` ou `07:30:15`; totais podem ultrapassar 24 horas.
- Diferenças positivas usam `+`; negativas usam `−`.
- Junto do valor, deixar claro que o sinal representa Monday menos VR Mais.
- Nunca formatar 7 horas e 30 minutos como `7,30`.
- Datas aparecem em português do Brasil, mas são enviadas à API como `YYYY-MM-DD`.
- O visual deve seguir a identidade já aprovada: fundo creme muito claro, cards brancos, laranja como ação principal, cinzas quentes e tipografia limpa.
- A janela é redimensionável. Tabelas podem ter rolagem horizontal em larguras menores, sem cortar ações essenciais.
- Estados de foco devem ser visíveis e toda ação deve funcionar por teclado.
- Respeitar `prefers-reduced-motion`.

## 8. Estados obrigatórios

Implementar e testar:

- inicialização do desktop;
- vazio antes da primeira consulta;
- carregamento;
- consulta concluída com dados;
- período válido sem registros;
- dados parciais ou provisórios;
- cache local;
- erro de validação;
- API indisponível sem cache;
- erro inesperado com `correlationId`;
- falha ao limpar cache.

Não deixar a tela em branco em nenhum desses casos.

## 9. Segurança e privacidade

- Não colocar tokens, senhas, chaves, e-mail fixo ou credenciais nos arquivos do React.
- Não registrar o corpo completo das respostas no console em produção.
- Não usar `localStorage` ou `IndexedDB` para os registros de horas no build desktop.
- O host nativo é responsável pelo cache protegido e pela comunicação HTTPS.
- Aceitar links de atividade somente quando o esquema for `https`.
- Desabilitar DevTools e menus de contexto no build de produção do WebView2.
- Usar política de conteúdo restritiva e assets locais.
- Como o modo atual não tem autenticação, a API deve ficar restrita por rede/VPN e retornar apenas o funcionário fixado na VM. Esse modo não deve permitir escolher outra pessoa.
- Quando contas forem criadas, desativar o acesso anônimo no backend antes de habilitar login no front.

## 10. Fora do escopo desta primeira tela

- Editar registros no Monday ou no VR Mais.
- Escolher outro funcionário.
- Criar usuários ou equipes.
- Ranking de pessoas e painel gerencial.
- Aprovar justificativas.
- Notificações externas.
- Folha de pagamento, banco de horas ou cálculo oficial de horas extras.
- Armazenar tokens de integração na máquina do usuário.

Essas capacidades podem ser adicionadas em etapas posteriores conforme a especificação funcional completa.

## 11. Critérios de aceite

O front do MVP está pronto quando:

1. Abre diretamente em `Minha jornada`, sem login.
2. Permite consultar um intervalo inclusivo de até 31 dias.
3. Mostra totais, diferença líquida e divergência absoluta sem tratar `null` como zero.
4. Mostra todos os dias e permite inspecionar as fontes que compõem cada total.
5. Explica visualmente o sinal da diferença.
6. Diferencia dados online, cache, dados provisórios, ausência de registros e erro.
7. Não recebe nenhuma credencial do Monday ou do VR Mais.
8. Não permite selecionar nem informar a identidade consultada.
9. Mantém os filtros ao abrir e fechar detalhes.
10. Funciona por teclado, tem foco visível e não depende apenas de cor.
11. Possui testes do adaptador de dados, formatação de duração, validação do período e estados principais da interface.
12. Gera um build com assets locais que pode ser carregado no WebView2.

## 12. Instrução pronta para o agente de implementação

> Implemente o front-end do CEP Horas seguindo integralmente este documento. Use React com TypeScript, componentes pequenos e uma camada de adaptação para o bridge do WebView2. O MVP deve abrir direto na consulta pessoal, sem login. Comece pelos tipos do contrato, adaptador mockado, formatação e validação; depois construa a tela e seus estados. Não chame Monday ou VR Mais diretamente, não inclua segredos e não invente endpoints. Se uma integração necessária ainda não existir na `main`, mantenha o mock isolado, documente o ponto de conexão e deixe o restante da interface funcional e testável.
