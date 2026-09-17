import test from 'node:test';
import assert from 'node:assert/strict';
import { createLab, extractSessions, filterSessions } from './server.mjs';

const entry = (id, start, end = null, extra = {}) => ({ id, started_at: start, ended_at: end, started_user_id: '7', status: 'active', ...extra });
const item = history => ({ id: '1', name: 'Synthetic service', url: 'https://example.invalid/1', board: { id: '9920862624' }, column_values: [{ id: 'clock', running: true, started_at: '2026-09-17T13:00:00Z', history }] });

test('closed sessions remain closed even when status is active; running must match column', () => {
  const result = extractSessions([item([
    entry('closed', '2026-09-17T10:00:00Z', '2026-09-17T11:00:00Z'),
    entry('current', '2026-09-17T13:00:00Z'),
    entry('ambiguous', '2026-09-16T13:00:00Z'),
    entry('deleted', '2026-09-16T12:00:00Z', null, { status: 'deleted' }),
  ])], Date.parse('2026-09-17T14:00:00Z'));
  assert.equal(result.sessions.length, 2);
  assert.equal(result.sessions[0].running, false);
  assert.equal(result.sessions[1].running, true);
  assert.equal(result.sessions[1].durationSeconds, 3600);
  assert.equal(result.diagnostics.unclassifiedSessions, 1);
  assert.equal(result.diagnostics.deletedSessions, 1);
});

test('Sao Paulo dates, user and state filters combine; duration stays at snapshot', () => {
  const result = extractSessions([item([entry('night', '2026-09-17T01:00:00Z', '2026-09-17T02:00:00Z'), entry('current', '2026-09-17T13:00:00Z')])], Date.parse('2026-09-17T14:00:00Z'));
  assert.equal(filterSessions(result.sessions, new URLSearchParams('userId=7&state=closed&from=2026-09-16&to=2026-09-16')).length, 1);
  assert.equal(filterSessions(result.sessions, new URLSearchParams('userId=8')).length, 0);
  assert.equal(filterSessions(result.sessions, new URLSearchParams('state=running'), Date.parse('2026-10-17T14:00:00Z'))[0].durationSeconds, 3600);
  assert.throws(() => filterSessions([], new URLSearchParams('from=2026-02-30')), /inválidos/);
  assert.throws(() => filterSessions([], new URLSearchParams('from=2026-09-18&to=2026-09-17')), /inválidos/);
});

test('subitems deduplicate and invalid dates remain diagnostics', () => {
  const child = { ...item([entry('bad', 'oops'), entry('good', '2026-09-17T10:00:00Z', '2026-09-17T11:00:00Z', { manually_entered_start_time: true })]), id: '2' };
  const result = extractSessions([{ ...item([]), subitems: [child] }, child]);
  assert.equal(result.itemCount, 2); assert.equal(result.sessions.length, 1);
  assert.equal(result.diagnostics.invalidDates, 1); assert.equal(result.sessions[0].manual, true);
});

test('failed subsequent page preserves successful snapshot and reports incomplete', async () => {
  let fail = false; let calls = 0;
  const lab = createLab({ token: 'synthetic', fetchImpl: async (url, options) => {
    assert.equal(url, 'https://api.monday.com/v2'); assert.equal(options.redirect, 'error');
    assert.equal(options.headers['API-Version'], '2026-07');
    const { query } = JSON.parse(options.body); calls++;
    if (query.includes('users(')) { assert.match(query, /status: \[ACTIVE\]/); return Response.json({ data: { users: [{ id: '7', name: 'Synthetic', email: 'test@example.invalid' }] } }); }
    if (query.includes('next_items_page')) return Response.json({ errors: [{ message: 'private upstream content' }] });
    return Response.json({ data: { boards: [{ id: '9920862624', items_page: { cursor: fail ? 'next' : null, items: [item([entry('closed', '2026-09-17T10:00:00Z', '2026-09-17T11:00:00Z')])] } }] } });
  } });
  await lab.sync(); const first = lab.getSnapshot(); assert.equal(lab.getStatus().complete, true);
  fail = true; await lab.sync(); assert.equal(lab.getStatus().complete, false);
  assert.equal(lab.getSnapshot(), first); assert.equal(lab.getStatus().hasSnapshot, true);
  assert.equal(lab.getStatus().errors[0].code, 'monday_query_error');
  assert.ok(!JSON.stringify(lab.getStatus()).includes('private upstream')); assert.equal(calls, 4);
});
