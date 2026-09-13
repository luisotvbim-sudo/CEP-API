# Atualização diária às 02h

A VM verifica a `main` diariamente às **02:00 no horário de São Paulo**. O timer não recupera uma execução perdida durante o dia.

O fluxo é direto:

1. Se a VM já usa o último commit da `main`, encerra sem reiniciar nada.
2. Confirma que a CI desse commit terminou com sucesso.
3. Compila a imagem enquanto a versão atual continua atendendo.
4. Faz um dump do PostgreSQL e valida o catálogo do backup.
5. Para somente a API, aplica migrations e inicia a nova versão.
6. Recarrega o Nginx e verifica `https://api.cep.lat/health/ready`.

PostgreSQL e Nginx continuam ligados. A API pode ficar indisponível por alguns segundos durante a etapa 5. Não há blue-green. Se a migration, a nova aplicação ou o health check falhar, o script retorna ao commit e à imagem anteriores; migrations já aplicadas não são revertidas automaticamente e devem permanecer compatíveis com a versão anterior. O backup fica em `/opt/cep-api/.local/backups/nightly/` e contém dados confidenciais.

O repositório é público, então a VM consulta GitHub por HTTPS sem token e sem chave SSH armazenada no GitHub. O agendamento não atualiza Ubuntu, PostgreSQL ou Nginx e não substitui backup externo ou alertas.

Para instalar ou atualizar o agendador:

```bash
sudo bash deploy/nightly/install.sh
```

Para conferir o horário e os últimos resultados:

```bash
sudo systemctl list-timers cep-api-update.timer
sudo journalctl -u cep-api-update.service --no-pager -n 100
```

Para suspender:

```bash
sudo systemctl disable --now cep-api-update.timer
```
