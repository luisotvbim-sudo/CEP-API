const BASE = 'https://api.pontomais.com.br/external_api/v1';
const safeError = (code, message) => Object.assign(new Error(message), { code, safe: true });
const dayMs = 86400000;
function validDate(value) {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
}
export function validatePeriod({ employeeId, from, to }) {
  if (!/^\d+$/.test(String(employeeId || '')) || !validDate(from) || !validDate(to) || to < from || (Date.parse(to) - Date.parse(from)) / dayMs >= 31) throw safeError('invalid_filter', 'Selecione um colaborador e um período válido de até 31 dias.');
}
function dailyDate(value) {
  if (validDate(value)) return value;
  const match = typeof value === 'string' && value.match(/(?:^|,\s*)(\d{2})\/(\d{2})\/(\d{4})$/);
  const result = match && `${match[3]}-${match[2]}-${match[1]}`;
  return validDate(result) ? result : null;
}
function duration(value) {
  if (typeof value !== 'string') return null;
  const match = value.trim().match(/^(\d{1,4}):([0-5]\d)(?::([0-5]\d))?$/);
  return match ? Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3] || 0) : null;
}
function cards(values) {
  if (!Array.isArray(values)) return [];
  return values.map(value => typeof value === 'string' ? value : value?.csv_value).filter(value => typeof value === 'string' && /^([01]\d|2[0-3]):[0-5]\d$/.test(value));
}

export function normalizeWorkDays(payload, { employeeId, from, to }, fetchedAt = new Date().toISOString()) {
  validatePeriod({ employeeId, from, to });
  if (!Array.isArray(payload?.data)) throw safeError('vr_invalid_response', 'O VR não retornou um relatório de jornada reconhecido.');
  if (payload.meta?.next_page || payload.meta?.next || Number(payload.meta?.total_pages || 1) > 1 || payload.meta?.truncated === true) throw safeError('vr_report_incomplete', 'O VR indicou um relatório parcial ou paginado; reduza o período e tente novamente.');
  const daily = new Map();
  const diagnostics = { duplicateDays: 0, unrecognizedRows: 0, outOfRangeRows: 0, unrecognizedTimeCards: 0 };
  let groupCount = 0;
  function visitGroups(groups) {
    for (const group of groups) {
      if (Array.isArray(group)) { visitGroups(group); continue; }
      if (!group || typeof group !== 'object' || !Array.isArray(group.data)) { diagnostics.unrecognizedRows++; continue; }
      groupCount++;
      for (const row of group.data) {
        const date = dailyDate(row?.date);
        if (!date) { diagnostics.unrecognizedRows++; continue; }
        if (date < from || date > to) { diagnostics.outOfRangeRows++; continue; }
        if (daily.has(date)) { diagnostics.duplicateDays++; daily.set(date, { date, totalSeconds: null, totalText: null, timeCards: [], timeCardsComplete: false, state: 'unrecognized' }); continue; }
        const totalSeconds = duration(row.total_time);
        const timeCards = cards(row.time_cards);
        if (Array.isArray(row.time_cards)) diagnostics.unrecognizedTimeCards += row.time_cards.length - timeCards.length;
        const timeCardsComplete = Array.isArray(row.time_cards) && timeCards.length === row.time_cards.length;
        daily.set(date, { date, totalSeconds, totalText: totalSeconds === null ? null : row.total_time.trim(), timeCards, timeCardsComplete, state: totalSeconds === null ? 'unrecognized' : 'reported' });
      }
    }
  }
  visitGroups(payload.data);
  if (groupCount > 1) throw safeError('vr_ambiguous_report', 'O VR retornou mais de um grupo para o colaborador selecionado.');
  const rows = [];
  for (let day = Date.parse(from); day <= Date.parse(to); day += dayMs) {
    const date = new Date(day).toISOString().slice(0, 10);
    rows.push(daily.get(date) || { date, totalSeconds: null, totalText: null, timeCards: [], timeCardsComplete: false, state: 'missing' });
  }
  const reportedDays = rows.filter(row => row.state === 'reported').length;
  const missingDays = rows.filter(row => row.state === 'missing').length;
  return { employeeId: String(employeeId), from, to, rows, fetchedAt, coverage: { requestedDays: rows.length, reportedDays, missingDays, unrecognizedDays: rows.length - reportedDays - missingDays, complete: reportedDays === rows.length && diagnostics.unrecognizedRows === 0 && diagnostics.outOfRangeRows === 0 && diagnostics.unrecognizedTimeCards === 0 && diagnostics.duplicateDays === 0 }, diagnostics, source: 'VR/Pontomais work_days total_time', scope: 'Relatório do colaborador solicitado; total_time ainda não homologado para conciliação.' };
}

export function createVrClient({ token, fetchImpl = fetch } = {}) {
  let employeePromise = null; let employeeCache = null; let employeeFetchedAt = 0;
  const inFlight = new Map(); let activeReports = 0;
  async function request(path, report) {
    if (!token) throw safeError('vr_unavailable', 'Configure o arquivo do token VR no servidor para consultar o ponto.');
    let response;
    try { response = await fetchImpl(BASE + path, { method: report ? 'POST' : 'GET', redirect: 'error', signal: AbortSignal.timeout(45000), headers: { 'access-token': token, Accept: 'application/json', ...(report ? { 'Content-Type': 'application/json' } : {}) }, ...(report ? { body: JSON.stringify({ report }) } : {}) }); }
    catch { throw safeError('vr_unreachable', 'Não foi possível consultar o VR no prazo.'); }
    if (!response.ok) throw safeError('vr_http_error', `O VR respondeu HTTP ${response.status}. Tente novamente mais tarde.`);
    let data; try { data = await response.json(); } catch { throw safeError('vr_invalid_response', 'O VR retornou uma resposta inválida.'); }
    if (data?.errors || data?.error) throw safeError('vr_report_error', 'O VR não concluiu a consulta solicitada.');
    return data;
  }
  async function getEmployees() {
    if (employeeCache && Date.now() - employeeFetchedAt < 5 * 60 * 1000) return employeeCache;
    if (employeePromise) return employeePromise;
    employeePromise = (async () => {
      const data = await request('/employees?attributes=id,first_name,last_name,email&incluirAnexos=false');
      if (!Array.isArray(data.employees) || data.employees.length > 1000) throw safeError('vr_invalid_employees', 'A lista de colaboradores do VR não pôde ser validada.');
      if (data.meta?.next_page || data.meta?.next || Number(data.meta?.total_pages || 1) > 1 || Number(data.meta?.total_count || data.employees.length) > data.employees.length) throw safeError('vr_employees_incomplete', 'O VR retornou uma lista paginada de colaboradores; a cobertura ainda precisa ser validada.');
      const employees = data.employees.filter(employee => /^\d+$/.test(String(employee.id))).map(employee => ({ id: String(employee.id), name: [employee.first_name, employee.last_name].filter(value => typeof value === 'string').join(' ').trim() || (typeof employee.name === 'string' ? employee.name : ''), email: typeof employee.email === 'string' ? employee.email : null }));
      employeeCache = { employees, fetchedAt: new Date().toISOString(), coverage: 'Lista retornada pelo endpoint de colaboradores; pode incluir inativos.' }; employeeFetchedAt = Date.now();
      return employeeCache;
    })().finally(() => { employeePromise = null; });
    return employeePromise;
  }
  async function getWorkDays(filters) {
    validatePeriod(filters);
    const { employees } = await getEmployees();
    if (!employees.some(employee => employee.id === String(filters.employeeId))) throw safeError('invalid_filter', 'Selecione um colaborador da lista retornada pelo VR.');
    const key = `${filters.employeeId}:${filters.from}:${filters.to}`;
    if (inFlight.has(key)) return inFlight.get(key);
    if (activeReports >= 2) throw safeError('vr_busy', 'Há consultas de ponto em andamento. Aguarde e tente novamente.');
    activeReports++;
    const operation = (async () => {
      const data = await request('/reports/work_days', { start_date: filters.from, end_date: filters.to, employee_id: Number(filters.employeeId), group_by: 'employee', columns: 'date,total_time,time_cards', format: 'json' });
      return normalizeWorkDays(data, filters);
    })().finally(() => { activeReports--; inFlight.delete(key); });
    inFlight.set(key, operation); return operation;
  }
  return { getEmployees, getWorkDays };
}
