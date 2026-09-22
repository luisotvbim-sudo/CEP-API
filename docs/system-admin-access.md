# Administração global e contexto de organização

`SystemAdmin` tem acesso às operações administrativas globais e às operações de `OrganizationAdmin`. Para as rotas `/api/v1/organization/*`, deve informar `?organizationId=<uuid>` em cada requisição. A seleção é validada no servidor e não altera o usuário autenticado nem seu JWT.

Sem seleção válida, a API retorna HTTP 400 `organization_context_required`; uma organização inexistente retorna HTTP 404 `organization_not_found`. `OrganizationAdmin` continua usando a organização da sessão e recebe HTTP 403 `organization_context_forbidden` se tentar escolher outra. `User` continua sem acesso às operações administrativas.

As consultas e gravações preservam os filtros por organização. A auditoria de cada operação mantém o SystemAdmin como ator e a organização selecionada como contexto. Acesso global não permite transferir registros entre organizações ou ignorar validações de domínio, validade de convites e proteção do último administrador.

`POST /api/v1/plugin/grants` também aceita SystemAdmin com `organizationId`, para qualquer produto, desde que a organização esteja ativa. Contas comuns mantêm a exigência de acesso ao produto. O grant registra a organização selecionada.

O frontend apresenta uma lista paginada de organizações para o administrador global. Ao escolher uma, abre as telas existentes e inclui o contexto nas chamadas. Ao trocar de organização, desmonta as telas anteriores para descartar seleção de pessoa e dados locais. A seleção não é persistida na sessão compartilhada entre abas.

Não há migration. Publicar o backend antes do frontend: versões anteriores da API não reconhecem o acesso global às rotas de organização.
