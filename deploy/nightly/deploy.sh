#!/usr/bin/env bash
set -Eeuo pipefail

app_dir=/opt/cep-api
repo=luisotvbim-sudo/CEP-API
health_url=https://api.cep.lat/health/ready
cd "$app_dir"

compose=(docker compose --env-file .env -f compose.production.yaml)
if [[ -f .local/compose.pending-email.yaml ]]; then
  compose+=(-f .local/compose.pending-email.yaml)
fi

current=$(git rev-parse HEAD)
if [[ -n $(git status --porcelain --untracked-files=no) ]]; then
  echo 'Tracked files on the VM have local changes; deployment skipped.' >&2
  exit 1
fi
git fetch --no-tags origin main
target=$(git rev-parse FETCH_HEAD)

if [[ "$current" == "$target" ]]; then
  echo "CEP API already uses the latest main commit: ${target:0:12}"
  exit 0
fi

if ! git merge-base --is-ancestor "$current" "$target"; then
  echo 'The main branch is not a normal continuation of the deployed commit; deployment skipped.' >&2
  exit 1
fi

ci_file=$(mktemp)
source_dir=$(mktemp -d)
cleanup() {
  rm -f -- "$ci_file"
  rm -rf -- "$source_dir"
}
trap cleanup EXIT

curl --fail --silent --show-error --max-time 30 \
  -H 'Accept: application/vnd.github+json' \
  -H 'User-Agent: CEP-API-nightly-deployment' \
  "https://api.github.com/repos/$repo/actions/workflows/ci.yml/runs?branch=main&event=push&head_sha=$target&per_page=10" \
  -o "$ci_file"

python3 - "$ci_file" "$target" "$repo" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], encoding='utf-8'))
sha, repository = sys.argv[2:]
runs = data.get('workflow_runs', [])
if not runs:
    raise SystemExit('No CI run exists for the new main commit.')
run = max(runs, key=lambda item: (item.get('run_number', 0), item.get('run_attempt', 0)))
valid = (run.get('head_sha') == sha and run.get('head_branch') == 'main'
         and run.get('event') == 'push' and run.get('status') == 'completed'
         and run.get('conclusion') == 'success'
         and run.get('repository', {}).get('full_name') == repository)
if not valid:
    raise SystemExit('The latest CI run for this commit is not successful; deployment skipped.')
PY

tag=${target:0:12}
git archive "$target" | tar -x -C "$source_dir"
docker build -t "cep-api:$tag" "$source_dir"

backup_dir="$app_dir/.local/backups/nightly"
install -d -m 0700 "$backup_dir"
backup_file="$backup_dir/before-$(date -u +%Y%m%dT%H%M%SZ)-${current:0:12}.dump"
docker exec cep-api-production-postgres-1 pg_dump -U postgres -d cep_api \
  -Fc --no-owner --no-acl > "$backup_file"
chmod 0600 "$backup_file"
docker exec -i cep-api-production-postgres-1 pg_restore --list < "$backup_file" >/dev/null

# The accepted maintenance window starts here. PostgreSQL and Nginx remain running,
# while the API can be unavailable for a short period.
"${compose[@]}" stop --timeout 60 api
git checkout --detach "$target"
sed -i "s/^API_IMAGE_TAG=.*/API_IMAGE_TAG=$tag/" .env

"${compose[@]}" run --rm --no-deps migrate migrate
"${compose[@]}" up -d --no-deps --no-build --wait --wait-timeout 120 api
"${compose[@]}" exec -T nginx nginx -t
"${compose[@]}" exec -T nginx nginx -s reload

for attempt in {1..15}; do
  if [[ $(curl --silent --show-error --max-time 5 \
      -H 'User-Agent: CEP-API-nightly-deployment' \
      -o /dev/null -w '%{http_code}' "$health_url" || true) == 200 ]]; then
    printf '%s\n' "$target" > .local/deployed-commit
    echo "CEP API deployed successfully: ${current:0:12} -> $tag"
    exit 0
  fi
  sleep 2
done

echo 'The new API did not pass the public health check. Manual attention is required.' >&2
exit 1
