# Publicação da API — 06/10/2026

## Escopo e base

Os PRs [#26](https://github.com/luisotvbim-sudo/CEP-API/pull/26) e [#27](https://github.com/luisotvbim-sudo/CEP-API/pull/27) foram integrados nessa ordem. A `main` funcional resultante é `78a71e4c6d63e618a221dd9b14eb66328ec0efa2`, a partir de `6da062756428255a3ed49c2903bc61ba5cf6476b`. O PR #28, laboratório isolado, não entrou na `main` nem na imagem de produção.

O #26 consulta `reports/time_cards` quando falta a linha de hoje em `reports/work_days`, preservando nulo em resposta vazia ou inválida. O #27 permite consultar a própria associação e histórico sem vínculo vigente com time e amplia a sincronização normal Monday/VR Mais para 20 dias inclusivos (hoje e 19 anteriores em São Paulo). Full/bootstrap administrativo continua em 90 dias. Não houve alteração de schema, migration ou contrato HTTP estrutural.

## Evidência de validação e deploy

- A [CI de push da main](https://github.com/luisotvbim-sudo/CEP-API/actions/runs/37543735483) concluiu com sucesso no SHA exato, incluindo build, unidades, integração PostgreSQL, script de migration idempotente, imagem e stack HTTPS.
- Antes do deploy, a VM tinha checkout e marker de implantação em `6da0627`, com arquivos rastreados limpos, imagem da API em execução saudável e script nightly instalado idêntico ao versionado. O catálogo do backup anterior era legível.
- O script versionado sob lock implantou `cep-api:78a71e4c6d63`. `git rev-parse HEAD` e `.local/deployed-commit` na VM coincidiram com o SHA completo; o ID da imagem do contêiner coincidiu com o ID da tag (`sha256:c3454221c0f29184c20b9b4258743870d7193f67620234dc9f808a7f8d394c35`). O contêiner API ficou `running/healthy`; Nginx, `running`.
- O backup novo de 06/10 às 20h01 (São Paulo) teve 394484 bytes e catálogo aceito por `pg_restore --list`. Essa verificação não equivale a restaurar o banco. A etapa de migration informou que nenhuma migration foi aplicada; o modelo EF também não tinha alteração pendente.
- `https://api.cep.lat/health/live` e `/health/ready` retornaram HTTP 200. `/api/v1/me/time-control/overview` e a rota de histórico sem sessão retornaram HTTP 401.
- Uma sessão produtiva já autenticada como coordenador abriu o histórico de 01–06/10 pela web: a consulta retornou 40 registros, com Monday e VR Mais, dias parciais e detalhes das duas fontes. A inspeção foi somente leitura.

## Limites e recuperação

A verificação autenticada acima não testa uma conta pessoal sem time. Nenhuma sincronização foi acionada após o deploy, portanto os últimos lotes persistidos de Monday e VR Mais (ambos `Succeeded`, 20/09–06/10) não comprovam a janela nova de 20 dias nem a consulta de fallback VR de hoje na fonte real. Esses comportamentos foram validados por testes com PostgreSQL e fixtures na CI; a homologação produtiva das fontes permanece necessária com conta autorizada e observação de cobertura por fonte.

A imagem anterior `cep-api:6da062756428` e o commit anterior permaneciam disponíveis após a implantação. O script consegue retornar aplicação e Nginx à imagem anterior em falha; ele não desfaz migrations. Nesta publicação nenhuma migration foi aplicada. Health e catálogo do backup não comprovam restauração externa, SMTP, jobs, instalação Windows ou cobertura das fontes.
