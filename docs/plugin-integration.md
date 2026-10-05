# Integração dos plugins

## Sessão online

1. Envie e-mail, senha e metadados do cliente para `POST /api/v1/auth/login`.
2. Use `accessToken` como `Authorization: Bearer` nas chamadas da API.
3. Guarde somente o `refreshToken` no Windows Credential Manager ou usando DPAPI. Nunca grave senha ou token em arquivo de texto.
4. Antes de o access token expirar, troque o refresh token em `POST /api/v1/auth/refresh`. A resposta contém outro refresh token; substitua o anterior de forma atômica.
5. Se um refresh token já rotacionado reaparecer, toda a família de sessões é revogada e um novo login será necessário.

Serialize a renovação no cliente: só uma chamada de refresh por sessão pode ficar em andamento. Duas chamadas com o mesmo token são tratadas como reutilização e encerram a família. Em caso de resposta perdida, não reutilize automaticamente o token antigo; solicite novo login.

O bearer está vinculado à família da sessão e à versão de segurança do usuário. Logout ou revogação de sessão invalida também os access tokens dessa família na próxima requisição; troca ou recuperação de senha invalida todas as sessões do usuário. Tokens sem as informações de família/segurança exigidas são rejeitados e requerem novo login.

Somente o código de recuperação mais recente é aceito. Solicitar outro invalida os anteriores; recuperar ou trocar a senha invalida todos os códigos pendentes. O envio é assíncrono, com retentativas de SMTP.

## Autorização offline

1. Gere uma identificação aleatória por instalação; ela não deve ser derivada de serial de disco, MAC address ou outro identificador invasivo.
2. Com a API online, solicite `POST /api/v1/plugin/grants` com `product`, `pluginVersion` e `installationId`.
3. Valide o JWT recebido usando as chaves de `/.well-known/jwks.json` e confira assinatura RS256, `kid`, `iss`, `aud=cep-plugin`, `nbf`, `exp` e o produto esperado.
4. Armazene o grant e as chaves públicas em cache protegido. O grant funciona por no máximo 72 horas e nunca é renovado sem contato com a API.
5. Não use o grant offline como bearer token da API: seu audience e tipo são diferentes do access token.

Suspensão da conta ou organização impede novas sessões, refreshes e grants. Alteração dos acessos a produtos pelo endpoint administrativo revoga sessões existentes e altera a versão de segurança; a conta ativa pode voltar a autenticar, mas não obter novo grant de produto removido. Um grant já emitido continua criptograficamente válido até `exp`; essa é a janela offline deliberada.


O `installationId` e a versão são obrigatórios e auditados, mas o grant emitido não vincula criptograficamente o token à instalação ou ao hardware. A API possuir este contrato não comprova que os plugins Revit/ZWCAD já o consumam; validar a integração em seus repositórios e hosts reais.
