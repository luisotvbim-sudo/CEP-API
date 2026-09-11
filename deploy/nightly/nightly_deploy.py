#!/usr/bin/env python3
"""Daily pull deployment. No GitHub credentials or incoming SSH automation required."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tarfile
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from zoneinfo import ZoneInfo

APP = Path('/opt/cep-api')
STATE = Path('/var/lib/cep-api-deploy')
REPOSITORY = 'luisotvbim-sudo/CEP-API'
URL = 'https://api.cep.lat'
TIMEZONE = ZoneInfo('America/Sao_Paulo')
# Automatic rollback must not run old code against a newly changed schema.
# These changes require a separate, reviewed maintenance deployment.
MANUAL_PATHS = (
    'src/CepApi.Infrastructure/Persistence/Migrations/',
    'src/CepApi.Infrastructure/Persistence/AppDbContext.cs',
    'compose.production.yaml', 'deploy/init-db.sh',
    'deploy/nginx.conf.template', 'deploy/cloudflare-realip.conf',
)


class DeployError(Exception):
    pass


def in_window(now=None):
    return (now or dt.datetime.now(TIMEZONE)).astimezone(TIMEZONE).hour == 2


def requires_manual(paths):
    return [p for p in paths if any(p == rule or
            (rule.endswith('/') and p.startswith(rule)) for rule in MANUAL_PATHS)]


def ci_passed(payload, sha):
    runs = payload.get('workflow_runs', [])
    if not runs:
        return False
    latest = max(runs, key=lambda r: (r.get('run_number', 0), r.get('run_attempt', 0)))
    return (latest.get('head_sha') == sha and latest.get('head_branch') == 'main'
            and latest.get('event') == 'push' and latest.get('status') == 'completed'
            and latest.get('conclusion') == 'success'
            and latest.get('repository', {}).get('full_name') == REPOSITORY)


def run(*args, timeout=120, stdout=None, stdin=None):
    result = subprocess.run(args, cwd=APP, stdin=stdin, stdout=stdout or subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=timeout, check=False)
    if result.returncode:
        # Do not log arbitrary subprocess output: future tools might include secrets.
        raise DeployError(f'{args[0]} {args[1]} failed with exit code {result.returncode}')
    return result.stdout.decode().strip() if result.stdout is not None else ''


def request_json(url):
    request = urllib.request.Request(url, headers={
        'User-Agent': 'CEP-API-nightly-deployment', 'Accept': 'application/vnd.github+json'})
    with urllib.request.urlopen(request, timeout=20) as response:
        return json.load(response)


def atomic_write(path, data):
    tmp = path.with_name(path.name + '.tmp')
    with open(tmp, 'wb') as stream:
        os.chmod(tmp, 0o600)
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(tmp, path)


def record(status, **details):
    payload = {'time': dt.datetime.now(TIMEZONE).isoformat(), 'status': status, **details}
    print(json.dumps(payload), flush=True)
    try:
        atomic_write(STATE / 'last-result.json', json.dumps(payload, indent=2).encode())
    except OSError:
        print('Could not persist status; journal still contains the event.', flush=True)


def compose(*args, timeout=180):
    command = ['docker', 'compose', '--env-file', '.env', '-f', 'compose.production.yaml']
    if (APP / '.local/compose.pending-email.yaml').exists():
        command += ['-f', '.local/compose.pending-email.yaml']
    return run(*command, *args, timeout=timeout)


def verify_health():
    for _ in range(12):
        try:
            with urllib.request.urlopen(URL + '/health/ready', timeout=5) as response:
                if response.status == 200 and response.read().strip() == b'Healthy':
                    return
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(2)
    raise DeployError('Public HTTPS readiness did not recover')


def deploy_image():
    compose('up', '-d', '--no-deps', '--no-build', '--pull', 'never',
            '--wait', '--wait-timeout', '120', '--timeout', '60', 'api')
    # A recreated API can receive another Docker IP; refresh Nginx DNS resolution.
    compose('exec', '-T', 'nginx', 'nginx', '-t')
    compose('exec', '-T', 'nginx', 'nginx', '-s', 'reload')
    verify_health()


def backup(previous, target):
    directory = APP / '.local/backups/nightly'
    directory.mkdir(parents=True, exist_ok=True, mode=0o700)
    stamp = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    destination = directory / f'before-{stamp}-{previous[:12]}.tar.gz'
    with tempfile.TemporaryDirectory(prefix='backup-', dir=STATE) as working:
        dump = Path(working) / 'database.dump'
        with dump.open('wb') as output:
            run('docker', 'exec', 'cep-api-production-postgres-1', 'pg_dump',
                '-U', 'postgres', '-d', 'cep_api', '-Fc', '--no-owner', '--no-acl',
                stdout=output, timeout=600)
        with dump.open('rb') as source:
            run('docker', 'exec', '-i', 'cep-api-production-postgres-1',
                'pg_restore', '--list', stdin=source)
        keys = Path(run('docker', 'volume', 'inspect', 'cep-api-production_protection_keys',
                        '--format', '{{.Mountpoint}}'))
        if keys != Path('/var/lib/docker/volumes/cep-api-production_protection_keys/_data'):
            raise DeployError('Unexpected key volume location')
        manifest = Path(working) / 'manifest.json'
        manifest.write_text(json.dumps({'previous': previous, 'target': target, 'time': stamp}))
        temporary = destination.with_suffix('.tmp')
        try:
            with tarfile.open(temporary, 'w:gz') as archive:
                archive.add(dump, arcname='database.dump')
                archive.add(manifest, arcname='manifest.json')
                archive.add(APP / '.env', arcname='configuration/.env')
                archive.add(APP / '.local/production', arcname='production-secrets')
                archive.add(keys, arcname='protection-keys')
                archive.add(APP / 'compose.production.yaml', arcname='configuration/compose.production.yaml')
                pending = APP / '.local/compose.pending-email.yaml'
                if pending.exists():
                    archive.add(pending, arcname='configuration/compose.pending-email.yaml')
            os.replace(temporary, destination)
        finally:
            temporary.unlink(missing_ok=True)
    return destination


def build_image(target):
    tag = target[:12]
    record('building', target=target)
    with tempfile.TemporaryDirectory(prefix='build-', dir=STATE) as working:
        snapshot = Path(working) / 'source.tar'
        run('git', 'archive', '--format=tar', '-o', str(snapshot), target)
        source = Path(working) / 'source'
        source.mkdir()
        with tarfile.open(snapshot) as archive:
            archive.extractall(source, filter='data')
        run('docker', 'build', '-t', f'cep-api:{tag}', str(source), timeout=1500)


def deploy(target, previous):
    verify_health()
    previous_jwks = request_json(URL + '/.well-known/jwks.json')
    old_env = (APP / '.env').read_bytes()
    database_bytes = int(run('docker', 'exec', 'cep-api-production-postgres-1',
                            'psql', '-U', 'postgres', '-d', 'cep_api', '-Atc',
                            "SELECT pg_database_size('cep_api')"))
    if shutil.disk_usage(APP).free < database_bytes * 2 + 3 * 1024**3:
        raise DeployError('Insufficient free disk space for a safe update')
    tag = target[:12]
    build_image(target)
    backup_path = backup(previous, target)
    # Never start replacing containers after the maintenance window has ended.
    if not in_window():
        record('skipped_window_closed', target=target)
        return
    if not approved(target):
        record('skipped_ci_changed', target=target)
        return
    new_env, replacements = re.subn(rb'(?m)^API_IMAGE_TAG=[^\r\n]*',
                                    f'API_IMAGE_TAG={tag}'.encode(), old_env)
    if replacements != 1:
        raise DeployError('Expected exactly one API_IMAGE_TAG setting')
    record('deploying', target=target, backup=str(backup_path))
    try:
        run('git', 'checkout', '--detach', target)
        atomic_write(APP / '.env', new_env)
        deploy_image()
        if request_json(URL + '/.well-known/jwks.json') != previous_jwks:
            raise DeployError('JWT signing keys changed unexpectedly')
        atomic_write(APP / '.local/deployed-commit', (target + '\n').encode())
    except Exception:
        # Schema changes are excluded, so returning to the previous code is safe
        # with respect to schema. Never restore a database over concurrent writes.
        record('rolling_back', target=target, previous=previous)
        run('git', 'checkout', '--detach', previous)
        atomic_write(APP / '.env', old_env)
        deploy_image()
        record('rolled_back', target=target, previous=previous)
        raise
    record('deployed', target=target, previous=previous, backup=str(backup_path))
    for expired in sorted(backup_path.parent.glob('before-*.tar.gz'))[:-7]:
        expired.unlink()


def approved(target):
    query = urllib.parse.urlencode({'branch': 'main', 'event': 'push',
                                    'head_sha': target, 'per_page': 1})
    return ci_passed(request_json(
        f'https://api.github.com/repos/{REPOSITORY}/actions/workflows/ci.yml/runs?{query}'), target)


def main(check=False):
    if not check and not in_window():
        record('skipped_outside_window')
        return
    if run('git', 'status', '--porcelain', '--untracked-files=no'):
        raise DeployError('Tracked files have local modifications; automatic update refused')
    previous = run('git', 'rev-parse', 'HEAD')
    if previous != (APP / '.local/deployed-commit').read_text().strip():
        raise DeployError('Git checkout differs from recorded deployment')
    if run('git', 'remote', 'get-url', 'origin') != f'https://github.com/{REPOSITORY}.git':
        raise DeployError('Unexpected repository origin')
    run('git', 'fetch', '--no-tags', 'origin', 'main')
    target = run('git', 'rev-parse', 'FETCH_HEAD')
    if not re.fullmatch('[0-9a-f]{40}', target):
        raise DeployError('Invalid target commit')
    if target == previous:
        record('up_to_date', target=target, check_only=check)
        return
    run('git', 'merge-base', '--is-ancestor', previous, target)
    if not approved(target):
        record('skipped_ci_not_successful', target=target)
        return
    changed = run('git', 'diff', '--name-only', previous, target).splitlines()
    manual = requires_manual(changed)
    if manual:
        record('manual_review_required', target=target, paths=manual)
        return
    if check:
        record('ready_for_nightly_update', target=target, current=previous, check_only=True)
        return
    deploy(target, previous)


if __name__ == '__main__':
    import fcntl
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Check eligibility without building or deploying')
    args = parser.parse_args()
    os.umask(0o077)
    if os.geteuid() != 0:
        parser.error('Run as root')
    STATE.mkdir(parents=True, exist_ok=True, mode=0o700)
    def interrupted(*_):
        raise DeployError('Deployment interrupted')
    signal.signal(signal.SIGTERM, interrupted)
    with (STATE / 'update.lock').open('w') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('Another deployment is running; skipped.', flush=True)
            raise SystemExit(0)
        try:
            main(check=args.check)
        except Exception as error:
            # Preserve the detailed last phase (especially rollback results).
            atomic_write(STATE / 'last-error.json', json.dumps({
                'time': dt.datetime.now(TIMEZONE).isoformat(),
                'error': str(error)[:300]}).encode())
            print(f'Update failed: {type(error).__name__}; see last-error.json and journal.', flush=True)
            raise SystemExit(1)
