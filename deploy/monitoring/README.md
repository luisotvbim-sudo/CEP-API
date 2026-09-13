# Monitoramento local

O timer verifica a cada cinco minutos os três contêineres, o readiness público e o uso do disco raiz. Falhas aparecem no journal e fazem a unidade terminar com status de erro.

```bash
sudo bash deploy/monitoring/install.sh
sudo systemctl status cep-api-healthcheck.service
sudo journalctl -u cep-api-healthcheck.service --since today
```

Este monitor não reinicia serviços automaticamente e não envia notificações externas. Uma integração de alerta ainda exige um destino, como OCI Notifications, e-mail ou webhook.
