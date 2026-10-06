-- Run only inside the cep-real-read-lab PostgreSQL container after restoring a dump.
BEGIN;

TRUNCATE TABLE
  refresh_sessions,
  password_resets,
  invitations,
  email_outbox,
  user_tokens,
  user_logins,
  audit_events,
  time_control.notifications,
  time_control.analysis_reports,
  time_control.notification_dispatches,
  time_control.power_action_overrides,
  time_control.power_pin_configuration;

UPDATE time_control.app_settings SET "AutomaticEnabled" = false;
UPDATE time_control.notification_schedules SET "IsEnabled" = false;

DELETE FROM allowed_email_domains;
INSERT INTO allowed_email_domains (domain, is_enabled) VALUES ('lab.invalid', true);

-- Original accounts remain linked to workforce records, but cannot authenticate.
-- A separate private step assigns new local credentials to the two lab accounts.
WITH numbered AS (
  SELECT "Id", row_number() OVER (ORDER BY "Id") AS n FROM users
)
UPDATE users AS u SET
  "Email" = 'clone-' || numbered.n::text || '@lab.invalid',
  "NormalizedEmail" = 'CLONE-' || numbered.n::text || '@LAB.INVALID',
  "UserName" = 'clone-' || numbered.n::text || '@lab.invalid',
  "NormalizedUserName" = 'CLONE-' || numbered.n::text || '@LAB.INVALID',
  "DisplayName" = 'Clone User ' || numbered.n::text,
  "PasswordHash" = NULL,
  "SecurityStamp" = gen_random_uuid()::text,
  "ConcurrencyStamp" = gen_random_uuid()::text,
  "PhoneNumber" = NULL,
  "PhoneNumberConfirmed" = false,
  "TwoFactorEnabled" = false,
  "LockoutEnd" = NULL,
  "AccessFailedCount" = 0
FROM numbered WHERE u."Id" = numbered."Id";

COMMIT;
