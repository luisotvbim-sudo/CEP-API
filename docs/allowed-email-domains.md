# Domínios de e-mail permitidos

A tabela PostgreSQL `allowed_email_domains` controla os domínios que podem receber convites e criar contas. A migration inclui inicialmente `conceitoprojetos.com`. O bootstrap do primeiro SystemAdmin também consulta a tabela.

Cada linha tem `domain` (chave primária, domínio em minúsculas, sem `@`) e `is_enabled` (verdadeiro por padrão). Pelo DBeaver, conectado via túnel SSH, adicione uma linha ou execute:

```sql
INSERT INTO allowed_email_domains (domain) VALUES ('novaempresa.com');
```

Para suspender novos cadastros de um domínio:

```sql
UPDATE allowed_email_domains SET is_enabled = false WHERE domain = 'novaempresa.com';
```

O efeito ocorre nas próximas requisições, sem reiniciar ou recompilar. Não há cache nem permissão automática para subdomínios: `pessoa@sub.novaempresa.com` exige uma linha própria. Domínios semelhantes e sufixos adicionais são recusados. Uma tabela vazia bloqueia novos cadastros.

A regra vale para o administrador inicial de uma organização, convites de usuários, reenvios e aceitação de convites pendentes. Recusas retornam HTTP 400 com código `email_domain_not_allowed`. Convites e organizações recusados não são gravados nem geram e-mail.

Desativar um domínio bloqueia novos cadastros e convites; contas já existentes continuam podendo autenticar e recuperar sua senha. Para retirar acesso de uma conta existente, desative o usuário ou sua organização. O convite com código continua obrigatório: pertencer a um domínio permitido não cria acesso automaticamente.

O envio de convites e recuperação depende de SMTP configurado. A primeira implantação na Oracle mantém o dispatcher de e-mail desativado enquanto o provedor não estiver definido.
