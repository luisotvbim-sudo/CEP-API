# Preparação da VM

Use `compose.production.yaml` sozinho, sem combiná-lo com `compose.yaml`. Ele cria Nginx, API, PostgreSQL e uma tarefa de migração. Somente 443 é publicada. O banco não possui porta no host; a API executa como UID 1654, com filesystem somente leitura e volume separado para Data Protection.

## Segredos e persistência

Copie `deploy/production.env.example` para `.env`, ajustando domínio, SMTP e identificador da chave. `.env` não deve ser versionado. Crie `.local/production` fora de qualquer pasta compartilhada; no Linux, deixe o diretório acessível somente ao administrador (`chmod 700`). Não use os segredos descartáveis do script de teste em produção.

Arquivos esperados:

| Arquivo | Conteúdo |
|---|---|
| `postgres_password` | Senha aleatória forte do administrador PostgreSQL |
| `owner_password` | Senha distinta do usuário `cep_api_owner` usado nas migrations |
| `runtime_password` | Senha distinta de `cep_api_runtime` usado pela API |
| `migration_connection` | `Host=postgres;Database=cep_api;Username=cep_api_owner;Password=...` |
| `runtime_connection` | `Host=postgres;Database=cep_api;Username=cep_api_runtime;Password=...` |
| `jwt-private.pem` | Chave privada RSA, preferencialmente 3072 bits, persistente |
| `smtp_username`, `smtp_password` | Credenciais do provedor SMTP; arquivos vazios somente para relay autenticado por outro mecanismo |
| `origin-certificate.pem`, `origin-private.key` | Certificado HTTPS e chave para o domínio da API |

As senhas nas conexões precisam coincidir com seus respectivos arquivos. Gere senhas sem caracteres especiais de connection string, por exemplo 32 bytes aleatórios em hexadecimal. Não as escreva no histórico de comandos. Os arquivos são montados somente nos serviços que precisam deles.

Em Compose local, permissões de secrets baseados em arquivos dependem do bind mount: os processos precisam conseguir ler os arquivos. Uma opção é arquivos legíveis dentro do contêiner (`0444`) sob diretório do host protegido (`0700`); outra é ajustar proprietário/grupo por serviço. Teste a leitura como UID 1654 sem imprimir o conteúdo. Compose não é um cofre de segredos.

O script `init-db.sh` cria os papéis apenas no primeiro uso de um volume vazio. `cep_api_owner` possui o schema e pode migrar; `cep_api_runtime` tem acesso aos dados, mas não é superusuário nem pode criar tabelas. Não altere senhas apenas nos arquivos esperando que um volume existente seja atualizado.

Os volumes `postgres_data` e `protection_keys` sobrevivem à recriação de contêineres. Faça backup dos dados, das chaves de Data Protection e dos segredos de assinatura para armazenamento fora da VM; teste restauração. As chaves de Data Protection ficam protegidas pelas permissões do volume, sem criptografia adicional de arquivo neste Compose. Perder essas chaves impede ler e-mails ainda pendentes. A fila armazena payload protegido, remove mensagens entregues/expiradas e faz retentativas com intervalo crescente. Um crash após aceitação do SMTP pode produzir e-mail duplicado com o mesmo código.

## Entrada HTTPS

Nginx usa `172.30.10.2`; a faixa dinâmica começa em `172.30.10.8`, evitando colisão. Confira se `172.30.10.0/28` não conflita com uma rede existente. Ao mudar o endereço do Nginx, altere também `ReverseProxy__KnownProxies__0`.

O Nginx só interpreta `CF-Connecting-IP` quando a conexão vem de uma faixa oficial da Cloudflare; em seguida sobrescreve os cabeçalhos encaminhados à API. Revise `cloudflare-realip.conf` contra as listas [IPv4](https://www.cloudflare.com/ips-v4) e [IPv6](https://www.cloudflare.com/ips-v6) antes da implantação.

Na Cloudflare, configure SSL/TLS Full (strict), redirecionamento externo para HTTPS e ausência de cache/challenge interativo nas rotas da API. Um certificado Origin CA serve entre Cloudflare e Nginx; clientes acessam o domínio proxied. Na OCI/firewall, mantenha PostgreSQL fechado e restrinja SSH. Se depender das proteções da Cloudflare, restrinja a porta de origem às faixas dela após os testes administrativos.

## Primeira inicialização e atualizações

```bash
docker compose --env-file .env -f compose.production.yaml config --quiet
docker compose --env-file .env -f compose.production.yaml build
docker compose --env-file .env -f compose.production.yaml up -d --wait postgres
docker compose --env-file .env -f compose.production.yaml run --rm migrate migrate
```

Para criar o administrador, monte temporariamente dois secrets adicionais no serviço `migrate`, com os nomes de destino `BootstrapAdmin__Email` e `BootstrapAdmin__Password`, e execute `run --rm migrate bootstrap-admin`. Remova os dois mounts e os arquivos de bootstrap após o sucesso. A API recusa bootstrap se já existe SystemAdmin. O teste `Test-ProductionStack.ps1` exemplifica os mounts, usando credenciais descartáveis e sem imprimi-las.

O e-mail do administrador e os novos convites devem pertencer a um domínio habilitado em `allowed_email_domains`; inicialmente apenas `conceitoprojetos.com`. Veja [como adicionar domínios pelo banco](../docs/allowed-email-domains.md).

```bash
docker compose --env-file .env -f compose.production.yaml up -d --wait
```

Nas atualizações, tire backup, construa a nova imagem, execute a migration explicitamente e recrie a API. Para mudanças incompatíveis de schema, faça janela de manutenção; não prometa rollback do banco apenas por voltar a imagem. Use tags de release imutáveis para as imagens antes da implantação definitiva.

O healthcheck verifica banco e API, mas não substitui monitoramento nem reinicia sozinho um contêiner unhealthy. O limite inicial é 4 GiB para PostgreSQL, 2 GiB para API e 128 MiB para Nginx; ajuste após medir uso real. SMTP usa TLS obrigatório em produção. Swagger fica desativado.

Para DBeaver, adicione um override administrativo que publique PostgreSQL somente em `127.0.0.1:5432` e conecte por túnel SSH. Não publique `5432:5432`.

## Validação e limites

`Test-ProductionStack.ps1` cria um projeto Docker com nome aleatório, certificado local temporário e dados fictícios. Verifica migrations com papel limitado, login HTTPS através do Nginx, permanência da chave após reinício, Swagger desativado e revogação após logout. No encerramento remove somente os contêineres/volumes daquele projeto e seus arquivos de segredo. A opção de ignorar certificado existe exclusivamente para esse teste em localhost.

A validação local não substitui os testes na VM ARM64, no SMTP contratado e nos plugins reais. A operação offline deliberadamente permite grants já emitidos até sua expiração (máximo 72 horas). Não é possível revogá-los imediatamente sem conexão ao servidor.
