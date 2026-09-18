import test from 'node:test';
import assert from 'node:assert/strict';
import { buildShiftComparison, saoPauloInstant } from './shift-comparison.mjs';
const session = (id, start, end, userId = '1') => ({ itemId: id, columnId: 'clock', sessionId: id, userId, startedAt: `2026-09-14T${start}:00-03:00`, endedAt: end ? `2026-09-14T${end}:00-03:00` : null, running: !end });
const input = sessions => ({ date: '2026-09-14', timeCards: ['08:00', '12:00', '13:00', '17:00'], timeCardsComplete: true, vrSeconds: 28800, sessions, userId: '1', mondayComplete: true, now: Date.parse('2026-09-18T12:00:00Z') });
test('intersections include partial turns and separate lunch gaps and day outside', () => {
  const result = buildShiftComparison(input([session('1', '07:00', '09:00'), session('2', '11:00', '14:00'), session('3', '16:00', '18:00')]));
  assert.equal(result.turns[0].mondaySeconds, 7200); assert.equal(result.turns[1].mondaySeconds, 7200);
  assert.equal(result.mondaySeconds, 14400); assert.equal(result.differenceSeconds, -14400); assert.equal(result.outsideSeconds, 10800);
});
test('three consecutive punch pairs retain original order; no invented invalid pairs', () => {
  const base = input([]); base.timeCards = ['08:00', '10:00', '11:00', '13:00', '14:00', '16:00'];
  assert.equal(buildShiftComparison(base).turns.length, 3);
  for (const cards of [['08:00', '12:00', '13:00'], ['08:00', 'bad', '13:00', '17:00'], ['13:00', '17:00', '08:00', '12:00'], ['22:00', '06:00'], []]) {
    const result = buildShiftComparison({ ...base, timeCards: cards }); assert.equal(result.differenceSeconds, null); assert.equal(result.turns.length, 0);
  }
  assert.equal(buildShiftComparison({ ...base, timeCardsComplete: false }).differenceSeconds, null);
});
test('open/current/missing collection never invent zero; missing profile cannot match null authors', () => {
  assert.equal(buildShiftComparison(input([session('1', '09:00', null)])).differenceSeconds, null);
  assert.equal(buildShiftComparison({ ...input([]), now: Date.parse('2026-09-14T20:00:00Z') }).differenceSeconds, null);
  assert.equal(buildShiftComparison({ ...input([]), mondayComplete: false }).differenceSeconds, null);
  assert.equal(buildShiftComparison({ ...input([session('1', '08:00', '12:00', null)]), userId: null }).differenceSeconds, null);
  assert.equal(buildShiftComparison({ ...input([]), diagnostics: { missingUser: 1 } }).differenceSeconds, null);
});
test('cross-midnight Monday clips to day; duplicate sessions dedup; end exclusive', () => {
  const cross = { ...session('1', '08:00', '09:00'), startedAt: '2026-09-13T23:00:00-03:00' };
  const result = buildShiftComparison(input([cross, cross, session('2', '12:00', '13:00'), session('3', '17:00', '18:00')]));
  assert.equal(result.turns[0].mondaySeconds, 3600); assert.equal(result.turns[1].mondaySeconds, 0);
  assert.equal(result.outsideSeconds, 10 * 3600);
});
test('overlapping sessions remain explicit summed allocations and union coverage', () => {
  const result = buildShiftComparison(input([session('1', '08:00', '10:00'), session('2', '09:00', '11:00')]));
  assert.equal(result.turns[0].mondaySeconds, 14400); assert.equal(result.turns[0].coverageSeconds, 10800);
  assert.equal(result.overlapSeconds, 3600); assert.ok(result.reasons.includes('overlapping_sessions')); assert.equal(result.state, 'review');
});
test('point duration mismatch warns without substituting reported daily total', () => {
  const result = buildShiftComparison({ ...input([]), vrSeconds: 1 });
  assert.equal(result.pointSeconds, 28800); assert.equal(result.differenceSeconds, -28800); assert.ok(result.reasons.includes('point_total_mismatch'));
});
test('timezone conversion uses historical DST and rejects nonexistent local time', () => {
  assert.equal(new Date(saoPauloInstant('2026-09-14', '08:00')).toISOString(), '2026-09-14T11:00:00.000Z');
  assert.equal(new Date(saoPauloInstant('2018-12-14', '08:00')).toISOString(), '2018-12-14T10:00:00.000Z');
  assert.equal(saoPauloInstant('2018-11-04', '00:30'), null);
});
