# Integração dos plugins

## Sessão online

1. Envie e-mail, senha e metadados do cliente para `POST /api/v1/auth/login`.
2. Use `accessToken` como `Authorization: Bearer` nas chamadas da API.
3. Guarde somente o `refreshToken` no Windows Credential Manager ou usando DPAPI. Nunca grave senha ou token em arquivo de texto.
4. Antes de o access token expirar, troque o refresh token em `POST /api/v1/auth/refresh`. A resposta contém outro refresh token; substitua o anterior de forma atômica.
5. Se um refresh token já rotacionado reaparecer, toda a família de sessões é revogada e um novo login será necessário.

## Autorização offline

1. Gere uma identificação aleatória por instalação; ela não deve ser derivada de serial de disco, MAC address ou outro identificador invasivo.
2. Com a API online, solicite `POST /api/v1/plugin/grants` com `product`, `pluginVersion` e `installationId`.
3. Valide o JWT recebido usando as chaves de `/.well-known/jwks.json` e confira assinatura RS256, `kid`, `iss`, `aud=cep-plugin`, `nbf`, `exp` e o produto esperado.
4. Armazene o grant e as chaves públicas em cache protegido. O grant funciona por no máximo 72 horas e nunca é renovado sem contato com a API.
5. Não use o grant offline como bearer token da API: seu audience e tipo são diferentes do access token.

Uma suspensão ou remoção de produto impede novas sessões, refreshes e grants. Um grant já emitido continua criptograficamente válido até `exp`; essa é a janela offline deliberada.

## Telemetria de uso

1. Gere um `eventId` UUID aleatório para cada execução de comando e mantenha identificadores estáveis, como `export.ifc`, sem depender do idioma da interface.
2. Guarde eventos pendentes em uma fila local protegida enquanto o plugin estiver offline.
3. Quando houver conexão, obtenha um access token da API e envie até 200 eventos por chamada para `POST /api/v1/plugin/telemetry/events`.
4. Reenvios são seguros: o servidor usa `eventId` para aceitar cada evento apenas uma vez. Remova da fila local eventos aceitos ou informados como duplicados.
5. Não envie nomes ou caminhos de arquivos, nomes de projetos, conteúdo de modelos ou outros dados pessoais. A API deriva usuário e organização do access token.

Eventos podem ter ocorrido até sete dias antes do recebimento, cobrindo a janela offline e atrasos de sincronização. O grant offline não deve ser usado como bearer token do endpoint de telemetria.
