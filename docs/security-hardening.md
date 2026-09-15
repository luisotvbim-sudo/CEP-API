# Correções da auditoria — 11/09/2026

Base auditada: `e614a15caf93e86a601f6e5efd40eb93fa23f763`.

## Alterações

- Recuperação invalida códigos antigos na emissão e todos os pendentes após sucesso; consumo e tentativas são serializados por conta no PostgreSQL, na mesma transação da senha e revogação.
- Access tokens incluem identificação da família da sessão e versão de segurança. Logout/revogação bloqueiam a família; recuperação/troca de senha bloqueiam todas as sessões. Rotacionar normalmente não invalida um access token ainda vigente.
- Refresh simultâneo possui somente um vencedor. Reutilização posterior revoga a família. Clientes precisam serializar refresh e tratar resposta perdida com novo login.
- Alterações de permissões e do último administrador são protegidas por transação/bloqueio. Consumo de convites também bloqueia os registros durante a verificação.
- A fila de e-mail guarda conteúdo protegido com Data Protection na mesma gravação do convite, faz retentativas e remove mensagens entregues/expiradas. SMTP exige TLS em produção.
- Cabeçalhos de IP/HTTPS só são aceitos de proxy confiável. O limite anônimo é separado por IP e rota normalizada, com padrão configurável de 60/minuto; operações sensíveis autenticadas têm limite por conta.
- Compose de produção separado, PostgreSQL sem porta publicada, usuário limitado para API, proprietário distinto para migrations, chave JWT persistente, volume de proteção, filesystem da API somente leitura, limites de memória e rotação de logs.
- Dependência Testcontainers atualizada para 4.15.0. Docker restaura somente a aplicação e inclui bibliotecas de runtime Alpine. SDK pode avançar dentro de .NET 10 para acompanhar imagem e CI.
- Teste existente corrigido quanto a enums JSON e precisão de datas JWT; verificação criptográfica de assinatura, adulteração e audiência adicionada.

## Validação executada

- `dotnet test CEP-API.sln -c Release`: **8 unitários + 14 de integração aprovados**, nenhum ignorado.
- Concorrência de refresh/reset testada com duas requisições aguardando efetivamente o bloqueio da conta em PostgreSQL real.
- Testados códigos antigos, revogação, sessão rotacionada, isolamento de duas organizações, falha/retentativa de SMTP e cabeçalhos forjados de proxy.
- `dotnet list CEP-API.sln package --vulnerable --include-transitive`: nenhum pacote vulnerável reportado pelas fontes consultadas nesta data.
- Script idempotente de migrations gerado; modelo sem alterações pendentes de migration.
- Imagem Docker Linux construída com sucesso.
- `deploy/Test-ProductionStack.ps1`: aprovado com PostgreSQL, API e Nginx reais em localhost HTTPS; migrations, bootstrap, login, reinício mantendo chave/acesso, Swagger desativado e revogação após logout.
- O teste usa dados/segredos descartáveis e remove seus próprios contêineres e volumes. Os resultados locais estão em `artifacts/production-smoke.log` e `artifacts/docker-build.log` (não versionados).

## Antes de disponibilizar para usuários

Aplicar a migration `SecurityEmailOutbox`; fornecer os segredos e o certificado indicados em [deploy/README.md](../deploy/README.md); verificar SMTP real, ARM64 e comunicação com os plugins na VM; configurar backup externo e testar restauração. A VM e o DNS não foram alterados durante estas correções.

Tokens de acesso emitidos pela versão anterior exigem novo login. Grants já emitidos para operação offline continuam válidos até a expiração deliberada, no máximo 72 horas. Os testes não constituem uma garantia de ausência de vulnerabilidades nem um teste de carga.
