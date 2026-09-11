#!/usr/bin/env bash
set -euo pipefail
test "$(id -u)" -eq 0
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
test -d /opt/cep-api/.git
test -f /opt/cep-api/.local/deployed-commit
bash -n "$script_dir/deploy.sh"
systemd-analyze calendar '*-*-* 02:00:00 America/Sao_Paulo'
install -m 0755 "$script_dir/deploy.sh" /usr/local/sbin/cep-api-nightly-deploy
install -m 0644 "$script_dir/cep-api-update.service" /etc/systemd/system/cep-api-update.service
install -m 0644 "$script_dir/cep-api-update.timer" /etc/systemd/system/cep-api-update.timer
systemd-analyze verify /etc/systemd/system/cep-api-update.service /etc/systemd/system/cep-api-update.timer
systemctl daemon-reload
# Enabling the timer does not immediately deploy or make up a missed daytime run.
systemctl enable --now cep-api-update.timer
systemctl list-timers cep-api-update.timer --no-pager
