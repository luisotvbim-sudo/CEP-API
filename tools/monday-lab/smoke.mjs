// Execute with the local server running and a completed snapshot in memory.
import assert from 'node:assert/strict';
const base = 'http://127.0.0.1:4177';
async function get(path) {
  const response = await fetch(base + path);
  assert.equal(response.status, 200);
  return response.json();
}
const status = await get('/api/status');
assert.equal(status.hasSnapshot, true, 'Synchronize first');
assert.equal(status.complete, true, 'A completed collection is required');
const { users } = await get('/api/users');
const all = await get('/api/sessions');
assert.ok(users.length > 0);
assert.ok(all.sessions.length > 0);
const userId = all.sessions.find(s => s.userId)?.userId;
assert.ok(userId);
const selected = await get('/api/sessions?userId=' + encodeURIComponent(userId));
assert.ok(selected.sessions.length > 0);
assert.ok(selected.sessions.every(s => s.userId === userId));
assert.equal(selected.sessions.length, all.sessions.filter(s => s.userId === userId).length);
for (const state of ['running', 'closed']) {
  const result = await get('/api/sessions?state=' + state);
  assert.ok(result.sessions.every(s => state === 'running' ? s.running : Boolean(s.endedAt)));
  assert.equal(result.sessions.length, all.sessions.filter(s => state === 'running' ? s.running : Boolean(s.endedAt)).length);
}
const formatter = new Intl.DateTimeFormat('en-CA', {timeZone: 'America/Sao_Paulo', year:'numeric',month:'2-digit',day:'2-digit'});
const dated = all.sessions.find(s => s.startedAt);
assert.ok(dated);
const day = formatter.format(new Date(dated.startedAt));
const daily = await get('/api/sessions?from=' + day + '&to=' + day);
assert.ok(daily.sessions.length > 0);
assert.ok(daily.sessions.every(s => formatter.format(new Date(s.startedAt)) === day));
assert.equal(daily.sessions.length, all.sessions.filter(s => s.startedAt && formatter.format(new Date(s.startedAt)) === day).length);
assert.ok(all.sessions.every(s => s.itemId && s.itemName && s.sessionId));
const keys = all.sessions.map(s => [s.boardId, s.itemId, s.columnId, s.sessionId].join(':'));
assert.equal(new Set(keys).size, keys.length);
const invalid = await fetch(base + '/api/sessions?from=invalid');
assert.equal(invalid.status, 400);
const forbidden = await fetch(base + '/api/status', {headers: {Origin: 'https://example.com'}});
assert.equal(forbidden.status, 403);
console.log(JSON.stringify({passed:true, users:users.length, sessions:all.sessions.length, userFilter:true, stateFilters:true, dateFilter:true, itemAssociation:true, duplicateKeys:false, invalidDateRejected:true, foreignOriginRejected:true}));
