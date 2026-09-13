#!/usr/bin/env bash
set -Eeuo pipefail

health_url=${HEALTH_URL:-https://api.cep.lat/health/ready}
max_disk_percent=${MAX_DISK_PERCENT:-85}
containers=(cep-api-production-postgres-1 cep-api-production-api-1 cep-api-production-nginx-1)
failures=()

for container in "${containers[@]}"; do
  state=$(docker inspect --format '{{.State.Status}}' "$container" 2>/dev/null || true)
  if [[ "$state" != running ]]; then
    failures+=("$container:$state")
    continue
  fi
  health=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container")
  if [[ "$health" == unhealthy ]]; then
    failures+=("$container:unhealthy")
  fi
done

http_status=$(curl --silent --show-error --max-time 10 --output /dev/null --write-out '%{http_code}' "$health_url" || true)
if [[ "$http_status" != 200 ]]; then
  failures+=("public-api:http-$http_status")
fi

disk_percent=$(df -P / | awk 'NR==2 {gsub(/%/, "", $5); print $5}')
if [[ ! "$disk_percent" =~ ^[0-9]+$ ]] || (( disk_percent >= max_disk_percent )); then
  failures+=("root-disk:${disk_percent:-unknown}%")
fi

if (( ${#failures[@]} > 0 )); then
  printf 'CEP API healthcheck failed: %s\n' "${failures[*]}" >&2
  exit 1
fi

printf 'CEP API healthy: public=200 disk=%s%% containers=running\n' "$disk_percent"
