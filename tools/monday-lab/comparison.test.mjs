import test from 'node:test';
import assert from 'node:assert/strict';
import { buildComparison } from './comparison.mjs';

const session = (id, day, hours, userId = '1') => ({ itemId: id, columnId: 'clock', sessionId: id, userId, startedAt: `2026-09-${day}T12:00:00Z`, endedAt: `2026-09-${day}T${String(12 + hours).padStart(2, '0')}:00:00Z`, durationSeconds: hours * 3600, running: false });
const input = (sessions = [], rows = []) => ({ mondaySnapshot: { sessions, diagnostics: {}, at: '2026-09-17T12:00:00Z' }, mondayStatus: { complete: true }, vrReport: { employeeId: '7', from: '2026-09-14', to: '2026-09-16', rows, fetchedAt: '2026-09-17T12:01:00Z', coverage: { complete: true } }, userId: '1', employeeId: '7', from: '2026-09-14', to: '2026-09-16', now: '2026-09-17T13:00:00Z' });
const vr = (day, hours) => ({ date: `2026-09-${day}`, totalSeconds: hours === null ? null : hours * 3600, state: hours === null ? 'missing' : 'reported', timeCards: ['09:00'] });

test('daily shortage and excess do not cancel absolute divergence; summaries use common base', () => {
  const result = buildComparison(input([session('a', '14', 6), session('b', '15', 10), session('c', '16', 5)], [vr('14', 8), vr('15', 8), vr('16', null)]));
  assert.equal(result.rows[0].differenceSeconds, -7200); assert.equal(result.rows[1].differenceSeconds, 7200);
  assert.equal(result.rows[2].differenceSeconds, null); assert.equal(result.summary.comparedDays, 2);
  assert.equal(result.summary.differenceSeconds, 0); assert.equal(result.summary.absoluteDifferenceSeconds, 14400);
  assert.equal(result.summary.moreSeconds, 7200); assert.equal(result.summary.lessSeconds, 7200);
  assert.equal(result.summary.mondaySeconds, 16 * 3600); assert.equal(result.summary.vrSeconds, 16 * 3600);
});
test('only session starter is selected; explicit zeros and absent VR are distinct', () => {
  const result = buildComparison(input([session('a', '14', 6, '2')], [vr('14', 0), vr('15', null)]));
  assert.equal(result.rows[0].state, 'no_records'); assert.equal(result.rows[0].differenceSeconds, null); assert.equal(result.rows[0].comparable, false);
  assert.equal(result.rows[0].sessions.length, 0); assert.equal(result.rows[1].vrSeconds, null); assert.equal(result.rows[1].differenceSeconds, null);
});
test('midnight crossings flag every affected date including sessions starting before the period', () => {
  const s = { ...session('a', '14', 1), startedAt: '2026-09-14T02:00:00Z', endedAt: '2026-09-15T04:00:00Z' };
  const result = buildComparison(input([s], [vr('14', 8), vr('15', 8), vr('16', 0)]));
  assert.equal(result.rows[0].state, 'review'); assert.equal(result.rows[0].mondaySeconds, null);
  assert.equal(result.rows[1].differenceSeconds, null); assert.equal(result.rows[1].sessions.length, 1);
  assert.equal(result.rows[2].differenceSeconds, null); assert.equal(result.rows[2].state, 'no_records');
});
test('open sessions and today never produce settled differences', () => {
  const data = input([{ ...session('a', '15', 1), endedAt: null, running: true }], [vr('14', 8), vr('15', 8), vr('16', 8)]);
  data.now = '2026-09-16T13:00:00Z';
  const result = buildComparison(data);
  assert.equal(result.rows[1].state, 'provisional'); assert.equal(result.rows[2].state, 'provisional');
  assert.equal(result.rows[1].differenceSeconds, null); assert.equal(result.rows[2].differenceSeconds, null);
});
test('closed session ending exactly at midnight affects only the prior day', () => {
  const s = { ...session('a', '14', 1), startedAt: '2026-09-15T02:00:00Z', endedAt: '2026-09-15T03:00:00Z' };
  const result = buildComparison(input([s], [vr('14', 1), vr('15', 0)]));
  assert.equal(result.rows[0].differenceSeconds, 0); assert.equal(result.rows[0].mondaySeconds, 3600);
  assert.equal(result.rows[0].reasons.includes('cross_midnight_session'), false);
  assert.equal(result.rows[1].sessions.length, 0); assert.equal(result.rows[1].state, 'no_records');
});
test('incomplete collection cannot invent Monday zero; global diagnostics mark reference review', () => {
  const data = input([session('a', '14', 6)], [vr('14', 8)]);
  data.mondayStatus.complete = false;
  assert.equal(buildComparison(data).rows[0].mondaySeconds, null);
  data.mondayStatus.complete = true; data.mondaySnapshot.diagnostics = { unclassifiedSessions: 1, runningColumnsWithoutSession: 1 };
  const result = buildComparison(data); assert.equal(result.rows[0].state, 'review'); assert.equal(result.rows[0].differenceSeconds, -7200);
  data.mondaySnapshot.diagnostics = { missingUser: 1 };
  assert.equal(buildComparison(data).rows[1].mondaySeconds, null);
});
test('source employee mismatch fails and missing snapshot gives no comparable days', () => {
  const data = input([], [vr('14', 0)]); data.vrReport.employeeId = '8';
  assert.throws(() => buildComparison(data), error => error.safe && error.code === 'comparison_source_mismatch');
  data.vrReport.employeeId = '7'; data.mondaySnapshot = null;
  const result = buildComparison(data); assert.equal(result.summary.comparedDays, 0);
  assert.equal(result.summary.mondaySeconds, null); assert.equal(result.summary.vrSeconds, null);
  assert.equal(result.summary.differenceSeconds, null); assert.equal(result.summary.absoluteDifferenceSeconds, null);
});
