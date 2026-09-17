import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { createVrClient } from './vr.mjs';
import { buildComparison } from './comparison.mjs';
import { validatePeriod } from './vr.mjs';

export const BOARD_ID = '9920862624';
const API_VERSION = '2026-07';
const PORT = 4177;
const directory = fileURLToPath(new URL('.', import.meta.url));
const historyFields = 'id status started_at ended_at started_user_id manually_entered_start_date manually_entered_start_time manually_entered_end_date manually_entered_end_time';
const itemFields = `id name url board { id } column_values(types: [time_tracking]) { id ... on TimeTrackingValue { running started_at history { ${historyFields} } } }`;
const pageFields = `cursor items { ${itemFields} subitems { ${itemFields} } }`;
const dateFormatter = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' });
const safeError = (code, message) => Object.assign(new Error(message), { code, safe: true });
const normalizeDate = value => value && Number.isFinite(Date.parse(value)) ? new Date(value).toISOString() : null;

export function extractSessions(items, now = Date.now()) {
  const sessions = new Map();
  const seenItems = new Set();
  const diagnostics = { missingUser: 0, invalidDates: 0, unclassifiedSessions: 0, deletedSessions: 0, duplicateSessions: 0, runningColumnsWithoutSession: 0 };
  function visit(item) {
    if (seenItems.has(String(item.id))) return;
    seenItems.add(String(item.id));
    for (const column of item.column_values || []) {
      let openCount = 0;
      for (const history of column.history || []) {
        if (/deleted/i.test(history.status || '')) { diagnostics.deletedSessions++; continue; }
        const startedAt = normalizeDate(history.started_at);
        const endedAt = normalizeDate(history.ended_at);
        if (!startedAt || (history.ended_at && !endedAt) || (endedAt && Date.parse(endedAt) < Date.parse(startedAt))) { diagnostics.invalidDates++; continue; }
        const openCandidates = (column.history || []).filter(entry => !entry.ended_at && entry.started_at && !/deleted/i.test(entry.status || ''));
        const columnStart = normalizeDate(column.started_at);
        const running = !endedAt && column.running === true && (columnStart ? columnStart === startedAt : openCandidates.length === 1);
        if (!endedAt && !running) { diagnostics.unclassifiedSessions++; continue; }
        if (running) openCount++;
        const userId = history.started_user_id == null ? null : String(history.started_user_id);
        if (!userId) diagnostics.missingUser++;
        const key = `${item.id}:${column.id}:${history.id}`;
        if (sessions.has(key)) { diagnostics.duplicateSessions++; continue; }
        sessions.set(key, { itemName: item.name, itemId: String(item.id), itemUrl: item.url, boardId: String(item.board?.id || BOARD_ID), columnId: column.id, sessionId: String(history.id), userId, startedAt, endedAt, running, manual: ['manually_entered_start_date', 'manually_entered_start_time', 'manually_entered_end_date', 'manually_entered_end_time'].some(field => history[field] === true), durationSeconds: Math.max(0, Math.floor(((endedAt ? Date.parse(endedAt) : now) - Date.parse(startedAt)) / 1000)) });
      }
      if (column.running && !openCount) diagnostics.runningColumnsWithoutSession++;
      if (openCount > 1) diagnostics.unclassifiedSessions += openCount - 1;
    }
    for (const child of item.subitems || []) visit(child);
  }
  items.forEach(visit);
  return { sessions: [...sessions.values()], diagnostics, itemCount: seenItems.size };
}

function validDay(day) { return /^\d{4}-\d{2}-\d{2}$/.test(day) && normalizeDate(day)?.startsWith(day); }
export function filterSessions(sessions, params, now = Date.now()) {
  const state = params.get('state') || params.get('status') || 'all';
  const from = params.get('from') || ''; const to = params.get('to') || '';
  if (!['all', 'running', 'closed'].includes(state) || (from && !validDay(from)) || (to && !validDay(to)) || (from && to && from > to)) throw safeError('invalid_filter', 'Filtros de período ou situação inválidos.');
  return sessions.filter(session => {
    const date = dateFormatter.format(new Date(session.startedAt));
    return (!params.get('userId') || session.userId === params.get('userId')) && (state === 'all' || (state === 'running') === session.running) && (!from || date >= from) && (!to || date <= to);
  }).sort((a, b) => b.startedAt.localeCompare(a.startedAt));
}

export function createLab({ token, vrToken, fetchImpl = fetch } = {}) {
  const vr = createVrClient({ token: vrToken, fetchImpl });
  let snapshot = null; let users = null; let userPromise = null;
  let status = { syncing: false, complete: false, hasSnapshot: false, boardId: BOARD_ID, apiVersion: API_VERSION, startedAt: null, finishedAt: null, lastSuccessAt: null, itemsRead: 0, pagesRead: 0, sessionCount: 0, runningCount: 0, userCount: 0, errors: [], diagnostics: {} };
  async function query(queryText, variables = {}) {
    if (!token) throw safeError('missing_token', 'Configure o arquivo de token do Monday no servidor.');
    let response;
    try { response = await fetchImpl('https://api.monday.com/v2', { method: 'POST', redirect: 'error', signal: AbortSignal.timeout(30000), headers: { Authorization: token, 'Content-Type': 'application/json', 'API-Version': API_VERSION }, body: JSON.stringify({ query: queryText, variables }) }); }
    catch { throw safeError('monday_unreachable', 'Não foi possível consultar o Monday no prazo.'); }
    if (!response.ok) throw safeError('monday_http_error', `Monday respondeu HTTP ${response.status}. Tente novamente mais tarde.`);
    let result; try { result = await response.json(); } catch { throw safeError('monday_invalid_response', 'Resposta inválida do Monday.'); }
    if (result.errors?.length || !result.data) throw safeError('monday_query_error', 'Monday não concluiu a consulta. Verifique a permissão do token e a disponibilidade da API.');
    return result.data;
  }
  async function getUsers() {
    if (users) return users;
    if (userPromise) return userPromise;
    userPromise = (async () => {
      const list = [];
      for (let page = 1; page <= 20; page++) {
        const data = await query('query ($page: Int!) { users(status: [ACTIVE], limit: 100, page: $page) { id name email } }', { page });
        if (!Array.isArray(data.users)) throw safeError('users_unavailable', 'Lista de usuários indisponível.');
        list.push(...data.users.map(user => ({ id: String(user.id), name: user.name, email: user.email })));
        if (data.users.length < 100) { users = list; status.userCount = users.length; return users; }
      }
      throw safeError('users_limit', 'A lista de usuários excedeu o limite deste laboratório.');
    })().finally(() => { userPromise = null; });
    return userPromise;
  }
  async function sync() {
    if (status.syncing) return;
    status = { ...status, syncing: true, complete: false, startedAt: new Date().toISOString(), finishedAt: null, itemsRead: 0, pagesRead: 0, errors: [] };
    try {
      const people = await getUsers();
      const names = new Map(people.map(person => [person.id, person.name]));
      const allItems = []; const cursors = new Set(); let cursor = null;
      const deadline = Date.now() + 10 * 60 * 1000;
      for (let page = 0; page < 500; page++) {
        if (Date.now() > deadline) throw safeError('sync_timeout', 'A coleta excedeu o prazo do laboratório.');
        let dataPage;
        if (!page) {
          const data = await query(`query ($ids: [ID!]) { boards(ids: $ids) { id items_page(limit: 50) { ${pageFields} } } }`, { ids: [BOARD_ID] });
          if (data.boards?.length !== 1) throw safeError('board_unavailable', 'O quadro configurado não está acessível.');
          dataPage = data.boards[0].items_page;
        } else {
          const data = await query(`query ($cursor: String!) { next_items_page(cursor: $cursor, limit: 50) { ${pageFields} } }`, { cursor });
          dataPage = data.next_items_page;
        }
        if (!Array.isArray(dataPage?.items)) throw safeError('items_unavailable', 'Uma página de atividades não foi recebida.');
        allItems.push(...dataPage.items); status.pagesRead++; status.itemsRead = allItems.length;
        cursor = dataPage.cursor;
        if (!cursor) break;
        if (cursors.has(cursor)) throw safeError('pagination_repeated', 'O Monday repetiu uma página; a coleta foi interrompida.');
        cursors.add(cursor);
        if (page === 499) throw safeError('pagination_limit', 'A coleta excedeu o limite de páginas.');
      }
      const extracted = extractSessions(allItems);
      const completedAt = new Date().toISOString();
      snapshot = { ...extracted, sessions: extracted.sessions.map(session => ({ ...session, userName: names.get(session.userId) || null })), at: completedAt };
      status = { ...status, complete: true, hasSnapshot: true, lastSuccessAt: completedAt, itemsRead: extracted.itemCount, sessionCount: extracted.sessions.length, runningCount: extracted.sessions.filter(session => session.running).length, diagnostics: extracted.diagnostics };
    } catch (error) { status.errors = [{ code: error.safe ? error.code : 'sync_failed', message: error.safe ? error.message : 'Falha na coleta. A última coleta concluída foi preservada.' }]; }
    finally { status.syncing = false; status.finishedAt = new Date().toISOString(); }
  }
  function json(response, code, value) { response.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8' }); response.end(JSON.stringify(value)); }
  const server = http.createServer(async (request, response) => {
    response.setHeader('Cache-Control', 'no-store'); response.setHeader('X-Content-Type-Options', 'nosniff'); response.setHeader('Referrer-Policy', 'no-referrer');
    response.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'");
    const allowedHosts = new Set([`127.0.0.1:${PORT}`, `localhost:${PORT}`]);
    if (!allowedHosts.has(request.headers.host) || (request.headers.origin && request.headers.origin !== `http://${request.headers.host}`) || request.headers['sec-fetch-site'] === 'cross-site') { json(response, 403, { code: 'forbidden_origin', message: 'Origem não permitida.' }); return; }
    try {
      const url = new URL(request.url, `http://${request.headers.host}`);
      if (request.method === 'GET' && url.pathname === '/api/comparison') {
        const employeeId = url.searchParams.get('employeeId');
        const userId = url.searchParams.get('userId');
        const from = url.searchParams.get('from');
        const to = url.searchParams.get('to');
        validatePeriod({ employeeId, from, to });
        if (url.searchParams.get('confirmed') !== 'true') throw safeError('invalid_filter', 'Confirme que os dois perfis pertencem à mesma pessoa para esta consulta.');
        const people = await getUsers();
        if (!people.some(person => person.id === userId)) throw safeError('invalid_filter', 'Selecione um perfil da lista do Monday.');
        const mondaySnapshot = snapshot;
        const mondayStatus = { ...status };
        const vrReport = await vr.getWorkDays({ employeeId, from, to });
        const comparison = buildComparison({ mondaySnapshot, mondayStatus, vrReport, userId, employeeId, from, to });
        json(response, 200, comparison); return;
      }
      if (request.method === 'GET' && url.pathname === '/api/vr/employees') { json(response, 200, await vr.getEmployees()); return; }
      if (request.method === 'GET' && url.pathname === '/api/vr/work-days') {
        json(response, 200, await vr.getWorkDays({ employeeId: url.searchParams.get('employeeId'), from: url.searchParams.get('from'), to: url.searchParams.get('to') })); return;
      }
      if (request.method === 'GET' && url.pathname === '/api/users') { json(response, 200, { users: await getUsers() }); return; }
      if (request.method === 'GET' && url.pathname === '/api/status') { json(response, 200, status); return; }
      if (request.method === 'POST' && url.pathname === '/api/sync') {
        if (!request.headers['content-type']?.startsWith('application/json')) { json(response, 415, { code: 'json_required', message: 'Envie JSON.' }); return; }
        let size = 0; for await (const chunk of request) { size += chunk.length; if (size > 1024) throw safeError('invalid_filter', 'Corpo da solicitação muito grande.'); }
        void sync(); json(response, 202, status); return;
      }
      if (request.method === 'GET' && url.pathname === '/api/sessions') {
        const sessions = filterSessions(snapshot?.sessions || [], url.searchParams);
        json(response, 200, { sessions, total: sessions.length, complete: status.complete, snapshotAt: snapshot?.at || null, diagnostics: snapshot?.diagnostics || {}, summary: { durationSeconds: sessions.reduce((sum, session) => sum + session.durationSeconds, 0), runningCount: sessions.filter(session => session.running).length, closedCount: sessions.filter(session => !session.running).length } }); return;
      }
      const files = { '/': ['index.html', 'text/html'], '/index.html': ['index.html', 'text/html'], '/app.js': ['app.js', 'text/javascript'], '/styles.css': ['styles.css', 'text/css'], '/comparacao': ['compare.html', 'text/html'], '/compare.js': ['compare.js', 'text/javascript'], '/compare.css': ['compare.css', 'text/css'] };
      if (request.method === 'GET' && files[url.pathname]) { const [file, type] = files[url.pathname]; const content = await readFile(resolve(directory, file)); response.writeHead(200, { 'Content-Type': `${type}; charset=utf-8` }); response.end(content); return; }
      json(response, 404, { code: 'not_found', message: 'Recurso não encontrado.' });
    } catch (error) { json(response, error.safe && error.code === 'invalid_filter' ? 400 : 502, { code: error.safe ? error.code : 'request_failed', message: error.safe ? error.message : 'Não foi possível concluir a solicitação.' }); }
  });
  server.requestTimeout = 35000; server.headersTimeout = 10000;
  return { server, sync, getUsers, getStatus: () => ({ ...status }), getSnapshot: () => snapshot };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const index = process.argv.indexOf('--token-file');
  const tokenPath = index >= 0 ? process.argv[index + 1] : process.env.MONDAY_TOKEN_FILE;
  let token;
  try { if (!tokenPath) throw new Error(); token = (await readFile(tokenPath, 'utf8')).replace(/^\uFEFF/, '').trim(); if (!token || /\s/.test(token)) throw new Error(); }
  catch { console.error('Configure MONDAY_TOKEN_FILE ou --token-file com um arquivo contendo somente o token Monday.'); process.exit(1); }
  let vrToken;
  if (process.env.VR_TOKEN_FILE) {
    try { vrToken = (await readFile(process.env.VR_TOKEN_FILE, 'utf8')).replace(/^\uFEFF/, '').trim(); if (!vrToken || /\s/.test(vrToken)) throw new Error(); }
    catch { console.error('VR_TOKEN_FILE deve apontar para um arquivo contendo somente o token VR.'); process.exit(1); }
  }
  const { server } = createLab({ token, vrToken });
  server.on('error', () => { console.error('Não foi possível iniciar o laboratório na porta 4177.'); process.exitCode = 1; });
  server.listen(PORT, '127.0.0.1', () => console.log(`Laboratório local: http://127.0.0.1:${PORT}`));
}
