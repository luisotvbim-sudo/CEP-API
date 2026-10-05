# Ativação de convite — contrato frontend

Contrato implementado na base revisada em 04/10/2026. O frontend consumidor deve usar backend com esta rota publicada; verificar a versão efetivamente implantada. A rota não altera schema.

## Experiência

O e-mail inclui instruções e link para `https://plugincep.com.br/?convite=1`. A URL é configurável no backend por `Email:InvitationActivationUrl`. Ela não contém código, senha ou e-mail. Na página de login existe também **Recebi um convite**, compatível com códigos já enviados e ainda válidos.

O formulário pede e-mail do convite, nome, código, senha e confirmação. Senha entre 12 e 200 caracteres; preservar todos os caracteres, inclusive espaços. Validar confirmação localmente e impedir envio duplicado. Nunca guardar esses dados no armazenamento do navegador nem na URL.

## Endpoint

`POST /api/v1/auth/invitations/activate`, anônimo, limitado pela política `auth` por IP/rota.

```json
{"email":"pessoa@example.com","code":"codigo-recebido","displayName":"Pessoa","password":"senha-escolhida","client":null}
```

Usa `AcceptInvitationRequest`: email obrigatório até 320 caracteres; code até 50; displayName até 200; password 12–200. `client` opcional.

Sucesso: **204 sem corpo, sem cookies e sem criação de sessão ou emissão de tokens**. A conta é ativada, recebe os acessos do convite e conserva a associação Monday/VR quando existir. Mostrar “Conta ativada” e retornar ao login. O navegador entra via `/auth/web/login`; desktop utiliza seu login nativo. O código não é uma senha de login.

Erros: `invalid_invitation` (400, inválido/expirado/revogado/utilizado ou tentativas esgotadas), `email_domain_not_allowed` (400), `invalid_password` (400), `organization_inactive` (403), `email_already_exists` e `workforce_person_unavailable` (409), rate limit (429). Exibir mensagens apropriadas e `correlationId` quando presente. Se a resposta se perder, orientar tentar login/recuperação antes de pedir novo convite, pois a ativação pode ter concluído.

Convites valem 48 horas; cinco erros esgotam o código. Reenvio é administrativo e invalida o código anterior. Não acrescentar reenvio anônimo. A fila transacional e proteção dos códigos permanecem inalteradas.

O endpoint antigo `/auth/invitations/accept` continua retornando `TokenResponse` para clientes existentes; não deve ser usado pelo novo formulário web. O adaptador Windows expõe somente `activate-invitation`, com campos explícitos e sem devolver tokens ao React.
