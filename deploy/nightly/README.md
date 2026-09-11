# Atualização diária às 02h

O timer systemd da VM verifica `main` todos os dias às **02:00, America/Sao_Paulo**. O GitHub Actions continua testando cada push. A VM só instala o commit exato se a execução de CI de `push` na `main` tiver terminado com sucesso. Não há deploy durante pushes diurnos nem dependência de um computador pessoal ligado.

O repositório é público: a VM consulta GitHub por HTTPS, sem token e sem guardar uma chave SSH no GitHub. Se o repositório ficar privado, as consultas falharão e a versão atual continuará em execução até configurar acesso de leitura.

## Fluxo

1. Janela de início/troca: 02:00 até antes de 03:00 em São Paulo. Nenhuma execução atrasada é recuperada no horário comercial.
2. Se não houver commit novo, não constrói imagem, não faz backup e não reinicia a API.
3. Confere CI do commit, árvore local limpa e avanço normal da `main`. CI pendente/falhando ou histórico reescrito impedem a atualização.
4. Constrói a imagem ARM na VM enquanto a API atual atende. Confere espaço livre e cria backup antes da troca.
5. Substitui somente o contêiner da API, dando até 60 segundos para a parada. PostgreSQL e Nginx continuam ligados; o Nginx é recarregado para atualizar o endereço interno da API.
6. Confere HTTPS público e permanência das chaves JWT. Se a troca falhar, tenta recriar a imagem anterior e verificar sua saúde. Não apaga nem restaura automaticamente o banco sobre gravações existentes.

Este é um deploy simples com **possibilidade de breve interrupção durante a troca**. Não implementa blue-green. O horário reduz o impacto; não garante ausência de falhas na nova aplicação. Falha de energia ou da VM durante a troca pode exigir recuperação manual.

## Mudanças que precisam de manutenção separada

Novas migrations, alterações no mapeamento do banco, Compose, inicialização do PostgreSQL ou configuração do Nginx são detectadas e **não entram automaticamente**. O motivo é permitir voltar à imagem anterior sem executá-la contra um schema incompatível. Nesse caso, `last-result.json` fica com `manual_review_required`; a versão atual continua atendendo. Depois de uma implantação manual, atualize `.local/deployed-commit` com o commit realmente validado em execução.

Os arquivos do agendador são instalados fora do checkout. Mudar seu código na `main` não muda automaticamente o programa executado como root; reinstale os arquivos após revisar a alteração.

## Instalação e diagnóstico

Na VM, a partir de uma cópia revisada destes arquivos:

```bash
sudo bash deploy/nightly/install.sh
sudo python3 /usr/local/lib/cep-api/nightly_deploy.py --check
sudo systemctl list-timers cep-api-update.timer
sudo journalctl -u cep-api-update.service --no-pager -n 100
sudo cat /var/lib/cep-api-deploy/last-result.json
```

`--check` verifica o candidato sem construir imagem, gravar dados no banco ou reiniciar contêineres. O comando normal também verifica a janela: iniciá-lo manualmente durante o dia não faz deploy.

Para suspender: `sudo systemctl disable --now cep-api-update.timer`. Não há cron duplicado nem agendamento no Codex.

## Backup e limites

Backups anteriores a atualizações ficam em `/opt/cep-api/.local/backups/nightly/`. Cada pacote contém dump PostgreSQL, configurações, segredos e chaves de Data Protection, em diretório privado para root. O catálogo do dump é validado antes da troca. Após atualizações bem-sucedidas são mantidos os sete últimos pacotes dessa rotina; o backup inicial é preservado.

Esses backups locais não protegem contra perda da VM/disco e não substituem uma rotina periódica externa. Não há backup em dias sem atualização. O script não limpa globalmente imagens/cache do Docker nem atualiza Ubuntu, PostgreSQL ou Nginx. Logs de falhas ficam no journal e em `last-error.json`; alertas externos ainda não estão configurados. A manutenção por SSH deve evitar concorrência com o timer e usar o mesmo bloqueio `/var/lib/cep-api-deploy/update.lock`.
