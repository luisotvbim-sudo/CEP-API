#!/usr/bin/env bash
set -Eeuo pipefail
test "$(id -u)" -eq 0
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

bash -n "$script_dir/healthcheck.sh"
install -m 0755 "$script_dir/healthcheck.sh" /usr/local/sbin/cep-api-healthcheck
install -m 0644 "$script_dir/cep-api-healthcheck.service" /etc/systemd/system/cep-api-healthcheck.service
install -m 0644 "$script_dir/cep-api-healthcheck.timer" /etc/systemd/system/cep-api-healthcheck.timer
systemd-analyze verify /etc/systemd/system/cep-api-healthcheck.service /etc/systemd/system/cep-api-healthcheck.timer
systemctl daemon-reload
systemctl enable --now cep-api-healthcheck.timer
systemctl start cep-api-healthcheck.service
systemctl list-timers cep-api-healthcheck.timer --no-pager
