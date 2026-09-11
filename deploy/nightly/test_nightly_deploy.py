import datetime as dt
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, call
import nightly_deploy as deploy


class NightlyTests(unittest.TestCase):
    def test_public_health_requires_200_and_healthy_response(self):
        with patch.object(deploy.urllib.request, 'urlopen') as opened, patch.object(deploy.time, 'sleep'):
            response = opened.return_value.__enter__.return_value
            response.status = 503
            response.read.return_value = b'Healthy'
            with self.assertRaises(deploy.DeployError):
                deploy.verify_health()
            response.status = 200
            response.read.return_value = b'Healthy'
            deploy.verify_health()
            self.assertEqual('CEP-API-nightly-deployment', opened.call_args.args[0].get_header('User-agent'))

    def test_only_0200_to_0300_sao_paulo_is_allowed(self):
        for utc_hour, minute, expected in [(4, 59, False), (5, 0, True), (5, 59, True), (6, 0, False), (15, 0, False)]:
            now = dt.datetime(2026, 9, 12, utc_hour, minute, tzinfo=dt.timezone.utc)
            self.assertEqual(expected, deploy.in_window(now))

    def test_ci_must_match_exact_main_push_and_latest_attempt(self):
        passed = {'head_sha': 'a' * 40, 'head_branch': 'main', 'event': 'push', 'status': 'completed',
                  'conclusion': 'success', 'run_number': 10, 'run_attempt': 1,
                  'repository': {'full_name': deploy.REPOSITORY}}
        self.assertTrue(deploy.ci_passed({'workflow_runs': [passed]}, 'a' * 40))
        self.assertFalse(deploy.ci_passed({'workflow_runs': [passed]}, 'b' * 40))
        for change in [{'status': 'in_progress'}, {'conclusion': 'failure'}, {'event': 'pull_request'},
                       {'head_branch': 'other'}, {'repository': {'full_name': 'other/repo'}}]:
            self.assertFalse(deploy.ci_passed({'workflow_runs': [{**passed, **change}]}, 'a' * 40))
        failed_retry = {**passed, 'run_attempt': 2, 'conclusion': 'failure'}
        self.assertFalse(deploy.ci_passed({'workflow_runs': [passed, failed_retry]}, 'a' * 40))

    def test_daytime_run_does_not_touch_git_or_containers(self):
        with patch.object(deploy, 'in_window', return_value=False), patch.object(deploy, 'record'), patch.object(deploy, 'run') as command:
            deploy.main()
            command.assert_not_called()

    def test_schema_and_infrastructure_changes_need_review(self):
        paths = ['src/CepApi.Api/Controllers/MeController.cs',
                 'src/CepApi.Infrastructure/Persistence/Migrations/new.cs',
                 'compose.production.yaml', 'deploy/nightly/nightly_deploy.py']
        self.assertEqual(paths[1:3], deploy.requires_manual(paths))

    def test_no_new_commit_does_not_build_backup_or_restart(self):
        with tempfile.TemporaryDirectory() as temp:
            app = Path(temp)
            (app / '.local').mkdir()
            (app / '.local/deployed-commit').write_text('a' * 40)
            outputs = ['', 'a' * 40, f'https://github.com/{deploy.REPOSITORY}.git', '', 'a' * 40]
            with patch.object(deploy, 'APP', app), patch.object(deploy, 'run', side_effect=outputs), \
                 patch.object(deploy, 'in_window', return_value=True), patch.object(deploy, 'record'), \
                 patch.object(deploy, 'deploy') as rollout:
                deploy.main()
                rollout.assert_not_called()

    def exercise_rollout(self, *, healthy=True, window=True):
        with tempfile.TemporaryDirectory() as temp:
            app = Path(temp)
            (app / '.local').mkdir()
            previous, target = 'a' * 40, 'b' * 40
            old_env = b'API_DOMAIN=api.cep.lat\nAPI_IMAGE_TAG=old\nSMTP_HOST=unconfigured.invalid\n'
            (app / '.env').write_bytes(old_env)
            (app / '.local/deployed-commit').write_text(previous + '\n')
            with patch.object(deploy, 'APP', app), patch.object(deploy, 'record'), \
                 patch.object(deploy, 'run', return_value='100') as command, \
                 patch.object(deploy, 'shutil') as disk, patch.object(deploy, 'verify_health'), \
                 patch.object(deploy, 'request_json', return_value={'keys': ['same']}), \
                 patch.object(deploy, 'build_image'), patch.object(deploy, 'backup', return_value=app / 'before-test.tar.gz'), \
                 patch.object(deploy, 'approved', return_value=True), \
                 patch.object(deploy, 'in_window', return_value=window), \
                 patch.object(deploy, 'deploy_image', side_effect=[None] if healthy else [deploy.DeployError('unhealthy'), None]) as restart:
                disk.disk_usage.return_value.free = 100 * 1024**3
                if healthy or not window:
                    deploy.deploy(target, previous)
                else:
                    with self.assertRaises(deploy.DeployError):
                        deploy.deploy(target, previous)
                if not window:
                    restart.assert_not_called()
                    self.assertNotIn(call('git', 'checkout', '--detach', target), command.call_args_list)
                elif not healthy:
                    self.assertEqual(2, restart.call_count)
                    self.assertIn(call('git', 'checkout', '--detach', previous), command.call_args_list)
                else:
                    self.assertIn(b'API_IMAGE_TAG=bbbbbbbbbbbb', (app / '.env').read_bytes())
                    self.assertEqual(target, (app / '.local/deployed-commit').read_text().strip())
                    return
                self.assertEqual(old_env, (app / '.env').read_bytes())
                self.assertEqual(previous, (app / '.local/deployed-commit').read_text().strip())

    def test_failed_new_version_restores_old_configuration_and_restarts_old_image(self):
        self.exercise_rollout(healthy=False)

    def test_build_finishing_outside_window_leaves_old_version_running(self):
        self.exercise_rollout(window=False)

    def test_success_records_the_new_version_only_after_health_checks(self):
        self.exercise_rollout()


if __name__ == '__main__':
    unittest.main()
