import test from 'node:test';
import assert from 'node:assert/strict';
import { createVrClient, normalizeWorkDays, validatePeriod } from './vr.mjs';
const filters = { employeeId: '7', from: '2026-09-14', to: '2026-09-16' };
const report = rows => ({ data: [[{ header: {}, data: rows, footer: [{ date: '2026-09-16', total_time: '99:00' }], totals: { total_time: '99:00' } }]] });

test('daily rows exclude totals, preserve missing and unknown totals, sanitize time cards', () => {
  const result = normalizeWorkDays(report([{ date: 'Seg, 14/09/2026', total_time: '08:30', time_cards: [{ csv_value: '08:00', title: 'private detail', value: '<b>08:00</b>' }] }, { date: 'Ter, 15/09/2026', total_time: '--', time_cards: [] }]), filters);
  assert.equal(result.rows[0].totalSeconds, 30600); assert.deepEqual(result.rows[0].timeCards, ['08:00']);
  assert.equal(result.rows[1].totalSeconds, null); assert.equal(result.rows[1].state, 'unrecognized');
  assert.equal(result.rows[2].totalSeconds, null); assert.equal(result.rows[2].state, 'missing');
  assert.equal(result.coverage.complete, false); assert.equal(result.coverage.reportedDays, 1);
  assert.ok(!JSON.stringify(result).includes('private detail')); assert.ok(!JSON.stringify(result).includes('99:00'));
});
test('explicit zero is valid, duplicates cannot silently sum or override, out-of-range rejected', () => {
  const result = normalizeWorkDays(report([{ date: '2026-09-14', total_time: '00:00' }, { date: '2026-09-15', total_time: '01:00' }, { date: '2026-09-15', total_time: '02:00' }, { date: '2026-09-17', total_time: '01:00' }]), filters);
  assert.equal(result.rows[0].totalSeconds, 0); assert.equal(result.rows[0].state, 'reported');
  assert.equal(result.rows[1].totalSeconds, null); assert.equal(result.diagnostics.duplicateDays, 1); assert.equal(result.diagnostics.outOfRangeRows, 1);
});
test('date bounds and ambiguous groups fail explicitly', () => {
  validatePeriod({ ...filters, from: '2026-08-01', to: '2026-08-31' });
  for (const invalid of [{ from: '2026-08-01', to: '2026-09-01' }, { from: '2026-02-30' }, { to: '2026-09-13' }, { employeeId: 'x' }]) assert.throws(() => validatePeriod({ ...filters, ...invalid }), error => error.safe && error.code === 'invalid_filter');
  assert.throws(() => normalizeWorkDays({ data: [[{ data: [] }, { data: [] }]] }, filters), /mais de um grupo/);
  assert.throws(() => normalizeWorkDays({ error: 'hidden' }, filters), /reconhecido/);
});
test('client sends exact read-only report body, restricts employee, strips private data', async () => {
  const calls = [];
  const client = createVrClient({ token: 'synthetic', fetchImpl: async (url, options) => {
    calls.push({ url, options }); assert.equal(options.redirect, 'error');
    return Response.json(url.includes('/employees?') ? { employees: [{ id: 7, first_name: 'Synthetic', last_name: 'Employee', email: 'test@example.invalid', cpf: 'private' }] } : report([{ date: '2026-09-14', total_time: '08:00', time_cards: [] }]));
  } });
  assert.equal(calls.length, 0); const employees = await client.getEmployees(); assert.ok(!JSON.stringify(employees).includes('cpf'));
  assert.equal(employees.employees[0].name, 'Synthetic Employee'); assert.ok(calls[0].url.endsWith('attributes=id,first_name,last_name,email&incluirAnexos=false'));
  await client.getWorkDays(filters); assert.equal(calls.length, 2);
  assert.equal(calls[1].options.method, 'POST'); assert.deepEqual(JSON.parse(calls[1].options.body), { report: { start_date: filters.from, end_date: filters.to, employee_id: 7, group_by: 'employee', columns: 'date,total_time,time_cards', format: 'json' } });
  await assert.rejects(client.getWorkDays({ ...filters, employeeId: '8' }), error => error.code === 'invalid_filter'); assert.equal(calls.length, 2);
});
test('missing credentials and upstream failure never become zero-hour reports', async () => {
  await assert.rejects(createVrClient().getEmployees(), error => error.safe && error.code === 'vr_unavailable');
  const client = createVrClient({ token: 'synthetic', fetchImpl: async () => Response.json({ errors: ['private raw error'] }) });
  await assert.rejects(client.getEmployees(), error => error.safe && !error.message.includes('private'));
});

test('discarded time cards cannot silently create valid entry-exit pairs', () => {
  const result = normalizeWorkDays(report([{ date: '2026-09-14', total_time: '08:00', time_cards: ['08:00', 'invalid', '12:00'] }, { date: '2026-09-15', total_time: '04:00', time_cards: ['08:00','12:00'] }]), filters);
  assert.deepEqual(result.rows[0].timeCards, ['08:00','12:00']);
  assert.equal(result.rows[0].timeCardsComplete, false);
  assert.equal(result.rows[1].timeCardsComplete, true);
  assert.equal(result.rows[2].timeCardsComplete, false);
});
