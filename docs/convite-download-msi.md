# Convite com download do CEP Horas — 06/10/2026

O convite agora dá boas-vindas ao CEP Horas pela organização e apresenta três passos: ativar a conta, instalar o MSI com auxílio da TI e entrar com e-mail e senha. O assunto é **Bem-vindo ao CEP Horas — ative sua conta**. HTML e texto alternativo mantêm código em destaque, validade em São Paulo e orientação de não compartilhá-lo.

Demanda e dependências transversais: [CEP-ORQUESTRADOR #23](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues/23).

**Ativar minha conta** conserva `Email__InvitationActivationUrl`. **Baixar CEP Horas para Windows — MSI** usa a nova configuração `Email__DesktopDownloadUrl`, padrão `https://plugincep.com.br/download`. `DESKTOP_DOWNLOAD_URL` alimenta essa opção nos arquivos Compose de desenvolvimento e produção; o exemplo de produção inclui o endereço público padrão. A URL de download exige HTTPS absoluto e não admite credenciais. A construção do convite não acrescenta e-mail, código ou token aos links. Organização, código e atributos HTML são codificados como texto.

O MSI não é anexado ao e-mail. A página estável permite publicar um instalador versionado no GitHub sem mudar o endereço dos convites antigos. Antes de publicar esta API, disponibilizar o MSI fora de rascunho, conferir acesso sem conta GitHub e publicar a página `/download` apontando à versão aprovada. Não usar `releases/latest` para resolver um beta, porque prereleases não são selecionadas por esse endpoint. O orquestrador coordena a entrega do frontend e a validação Windows; esta mudança de e-mail não certifica o instalador para a frota.

Ativação permanece 204 sem sessão, cookies ou tokens. Senha continua 6–200 caracteres. Convite, expiração, tentativas, reenvio e outbox não mudaram. Recuperação mantém seu assunto, botão **Abrir portal**, código e instruções, sem seção de download. Sem mudança de OpenAPI, schema, migration ou envio SMTP real.

## Prévia sintética

A prévia local é gerada pelo próprio `SecurityEmailTemplate.Invitation`, com **Organização Exemplo (fictícia)**, código de demonstração **PREVIA-FICTICIA** e validade fixa de 08/10/2026 às 15h30 em São Paulo. Não usa uma pessoa ou convite real e não envia e-mail.

```text
Bem-vindo ao CEP Horas

Você foi convidado para acessar o CEP Horas pela organização Organização Exemplo (fictícia).

1. Ative sua conta: clique no botão, informe o e-mail que recebeu este convite, seu nome e o código abaixo. Crie uma senha de 6 a 200 caracteres.

Código de ativação: PREVIA-FICTICIA
Válido até 08/10/2026 15:30 (horário de São Paulo).

Ativar minha conta: https://plugincep.com.br/?convite=1

2. Instale o aplicativo para Windows
Depois de ativar a conta, baixe o instalador MSI. Peça auxílio à TI para verificar os requisitos do computador e realizar a instalação.
Baixar CEP Horas para Windows — MSI: https://plugincep.com.br/download

3. Entre no CEP Horas
Abra o aplicativo e entre com seu e-mail e a senha criada na ativação da conta.

O código de ativação não é sua senha. Se o convite expirar, peça um novo ao administrador.

Se não esperava este convite, ignore esta mensagem. Não compartilhe seu código de ativação.
```

## Verificação

Verificação executada em 06/10/2026 na base de trabalho derivada de `5225b914440be13c39f9e248c212fb4fe5e4ac6b`:

- `dotnet build CEP-API.sln -m:1 --no-restore`: aprovado, zero avisos/erros.
- `dotnet test tests/CepApi.UnitTests/CepApi.UnitTests.csproj --no-build --no-restore`: 126 testes aprovados, zero ignorados.
- Configuração dos dois Compose validada com `config --quiet`, usando `deploy/production.env.example` para produção, sem iniciar serviços.
- Prévia gerada pelo template real e conferida visualmente em 1000px e 390px, sem overflow horizontal, com dois links independentes e sem código nas URLs. Artefatos HTML/texto/PNG permanecem locais e sintéticos.

Os testes de template fazem round-trip MIME, conferem HTML/texto, validade São Paulo, instruções, dois links independentes, escape de nome/código/URL, recusa de download HTTP/relativo/JavaScript/com credenciais e recuperação sem botão de download. CI do SHA exato e acesso público ao MSI/página continuam etapas separadas da publicação. Não houve merge, deploy, envio SMTP real ou teste Windows nesta frente.
