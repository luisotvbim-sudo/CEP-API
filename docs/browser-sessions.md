# Sessão persistente do CEP Horas no navegador

O navegador usa rotas próprias; o contrato de tokens dos plugins e do executável permanece disponível.

| Operação | Rota | Corpo |
|---|---|---|
| Entrar | `POST /api/v1/auth/web/login` | `LoginRequest`, igual ao login existente |
| Retomar/renovar | `POST /api/v1/auth/web/refresh` | Nenhum campo; `{}` permitido |
| Sair | `POST /api/v1/auth/web/logout` | Nenhum campo; `{}` permitido |

Login e refresh devolvem `WebSessionResponse`: `accessToken`, `accessTokenExpiresAt`, `sessionExpiresAt` e `user`. O refresh token nunca aparece no JSON. As consultas continuam com bearer, incluindo `GET /api/v1/me`; autorização e organização continuam sendo validadas pelo backend.

A sessão tem prazo absoluto de até sete dias a partir do login (ou `Jwt:RefreshTokenDays`, se menor). A rotação preserva o vencimento do registro no banco. A validação de bearer também verifica esse prazo. Logout, revogação, inativação e recuperação/troca de senha podem encerrar a sessão antes do prazo. Sessões web usam `ClientType=cep-horas-browser`, reservado para preservar o vencimento em toda rotação.

O cookie `__Host-cep-session` é HttpOnly, Secure, SameSite Strict, sem Domain, Path=/ e Expires. Seu conteúdo é autenticado/criptografado com Data Protection, com finalidade própria. Apenas fora de produção e em Host de loopback HTTP utiliza-se `cep-session-local`, sem Secure. Produção exige HTTPS interpretado somente por proxies confiáveis. As chaves Data Protection existentes de produção devem persistir; não é necessária migration.

As três rotas exigem `X-CEP-Web-Session: 1` e `Origin` exatamente correspondente a Scheme/Host da requisição. Quando `Sec-Fetch-Site` está presente, somente `same-origin` é aceito. Rejeições usam `web_origin_invalid` (403); sessão ausente/expirada usa 401. Respostas não podem ser cacheadas. Não habilite CORS com credenciais para essas rotas.

O frontend e `/api` devem estar na mesma origem. O proxy deve preservar Host, Origin, cookies, cabeçalhos Fetch Metadata e Set-Cookie. Mantenha a configuração atual de proxies confiáveis; não aceite cabeçalhos encaminhados de qualquer origem. O proxy Vite preserva Host em desenvolvimento. Em produção, o fluxo HTTPS precisa ser mantido até o proxy confiável que informa o esquema à API.

Cookies são compartilhados por abas. O cliente deve serializar login, refresh e logout entre todas as abas (Web Locks), além de compartilhar encerramento/troca de conta. Reutilizar cookie anterior após rotação revoga a família, assim como no contrato nativo. Se a resposta de refresh se perder, exigir novo login, sem repetir o refresh automaticamente. Não guardar senha ou tokens em localStorage/sessionStorage/IndexedDB.

Publique essas rotas antes do frontend que as consome e atualize seu OpenAPI. Usuários do frontend antigo precisam entrar uma vez para criar o cookie persistente. Testes de integração cobrem persistência, atributos do cookie, prazo fixo, revogação, replay, adulteração e bloqueio de requisições de outra origem.
