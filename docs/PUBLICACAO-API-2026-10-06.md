# Publicação da API — 06/10/2026

O usuário autorizou revisão, integração e publicação da API e do Front web em produção, backend primeiro. Esta evidência registra somente a API e não representa distribuição MSI ou homologação Windows/energia.

## Revisão e integração

- PR #22 (senha mínima seis), head `daa5fefcacdcd3397a9f42222c030866a07eca2f`, integrado em `a778c63b6581102a019974ab91f23059894ffd1e`.
- PR #23 (sincronização normal 17 dias), head `a2715de426ff74e9374373a95ee3c9e55e12a4ad`, retarget para main e integrado em `857489e710b36c8889a7211dd44d702447a9a02f`.
- PR #24 (refatoração interna), head `69859eae559bd2b58a76dc1c50f2ed5b399bde85`, retarget para main e integrado em `5225b914440be13c39f9e248c212fb4fe5e4ac6b`.
- Cada árvore após merge foi comparada com o head correspondente. A árvore final de main é exatamente igual à de `69859ea`, sem incluir mock CA, secrets, Compose local ou alterações de fontes/env.
- CI dos heads: #22 [run 37394648443](https://github.com/luisotvbim-sudo/CEP-API/actions/runs/37394648443), #23 [run 37402006019](https://github.com/luisotvbim-sudo/CEP-API/actions/runs/37402006019), #24 [run 37408401334](https://github.com/luisotvbim-sudo/CEP-API/actions/runs/37408401334), todos aprovados.
- CI push do SHA final [run 37408984833](https://github.com/luisotvbim-sudo/CEP-API/actions/runs/37408984833): aprovada, incluindo build, 119 unidades, 92 integrações PostgreSQL, script idempotente/modelo de migrations, imagem e stack HTTPS com login nativo/web, reinício, persistência da sessão/chaves e revogação por logout.

As runs intermediárias de main após #22/#23 falharam na fixture antiga de energia dependente do horário (restart, delta 2000, expected blocked/actual allowed antes de 00h33 São Paulo). A correção determinística do teste em #24 resolve isso sem mudar produção. Nenhum desses SHAs intermediários foi implantado: o timer estava pausado e a publicação aguardou CI final aprovada.

Revisão: autorização continua nos controllers/serviços de escopo; helpers recebem queries filtradas; análise continua no motor; scheduler executa sob lock PostgreSQL; rede continua fora da transação curta; refresh preserva locks/rotação/revogação. Não adicionada migration nem alteradas políticas globais de notificações/fontes. O detalhamento está em [refatoração interna](REFATORACAO-INTERNA-2026-10-06.md).

## Publicação efetiva

Baseline confirmado por SSH com referência de acesso existente: checkout e marcador `cac19142c3c094e02d3509dc3397b0bc2f69feaa`, imagem saudável `cep-api:cac19142c3c0`, árvore versionada limpa. O destino e o método de acesso permanecem em registro privado; não constam neste documento.

O timer instalado estava ativo/habilitado às 02h São Paulo, serviço de deploy inativo e lock livre. Hash do script instalado correspondia ao script do checkout. O timer foi pausado temporariamente; não alterada sua agenda/habilitação.

Antes da publicação foi feito backup protegido e validado tanto pelo catálogo quanto por restauração completa em PostgreSQL descartável, sem rede e com dados em tmpfs. O serviço instalado foi acionado após reconfirmar main e sua CI. Ele compilou o archive do SHA aprovado, fez outro backup, aplicou migrations explicitamente, recriou API/Nginx e verificou saúde. Esse segundo dump também passou por restauração completa em container isolado. Ambos os containers de verificação foram removidos. Nenhuma restauração foi feita sobre o banco produtivo.

**API efetivamente implantada:** `5225b914440be13c39f9e248c212fb4fe5e4ac6b`.

**Imagem:** `cep-api:5225b914440b`, ID `sha256:44e273ac36a69475a6c4bbe993863ac0427b6cbf1a8d7ed96cae7dfe4325f0e8`.

Checkout, marcador de deploy, tag e ID do container foram conferidos; o ID corresponde à imagem construída pelo serviço. Serviço terminou com sucesso, sem rollback. Isso identifica o SHA por evidência operacional; health isolado não seria suficiente.

- Runtime environment exatamente igual ao baseline, incluindo políticas/integrações; nenhum token/env foi trocado.
- Mounts, UID, filesystem readonly e volumes de chaves preservados.
- Override operacional de e-mail preservado, com checksum igual.
- Sete migrations antes/depois idênticas, sem alteração de schema; execução migrate foi no-op.
- Imagem anterior permanece disponível: ID `sha256:3e1c83b7a4b87efbcf13415b0124b4da649b877118981a4802765bcdfa8dd6ea`. Backups protegidos permanecem no mecanismo operacional; voltar imagem não é restauração de schema.
- Timer retomado ao estado original ativo/habilitado, próximo disparo 06/10/2026 às 02h São Paulo; serviço de deploy inativo após sucesso.

## Verificações e limites

HTTPS com validação normal de certificado nos domínios `api.plugincep.com.br` e `api.cep.lat`: health live/ready 200, `/api/v1/me` sem sessão 401, Swagger 404 em Production. JWKS público idêntico antes/depois.

Snapshot gerado na imagem validada: 53 caminhos/77 schemas. Sem mudança de operações/schemas em relação aos snapshots API/Front; a única diferença de serialização encontrada na lista de tags é a ordem Organizations/OrganizationUsers. Swagger produtivo permanece desabilitado; não foi habilitado para inspeção.

Consulta somente de leitura observou avisos automáticos desabilitados e tolerância global 30 minutos; dois dispatches completed e nenhum pending/running na consulta. Esses estados não comprovam todos os jobs, entrega SMTP, leitura dos avisos nem cobertura Monday/VR. Não foi iniciada coleta, aviso, convite, alteração de senha/usuário ou ação de energia para testar produção. Workers e fontes mantiveram sua configuração existente.

**Login real em produção não homologado nesta rodada:** não foi fornecida conta operacional apropriada. Os testes autenticados/isolamento locais e de CI são evidência separada e não são apresentados como login real na VM. Não foram reutilizados usuários pessoais ou credenciais fictícias em produção.

O Front recebeu go para publicar somente após confirmação deste backend e de sua própria CI. O SHA/imagem da publicação do Front pertence ao relato da frente responsável. O PR #21 (timeout do proxy direto usado pelo desktop) continua fora da seleção #22/#23/#24; esta entrega não afirma sua implantação nem inclui MSI.

Fila e continuidade: [CEP-ORQUESTRADOR #21](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues/21). Este commit de evidência posterior não altera os binários implantados nem deve ser confundido com o SHA produtivo acima.
