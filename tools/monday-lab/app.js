'use strict';
const $ = id => document.getElementById(id);
let users = [], selectedUser = null, latestStatus = null, requestVersion = 0, lastSnapshot = null;
let currentSessions = [], renderedSessions = 0;
let vrEmployees = [], vrRequestVersion = 0;
const dateFormatter = new Intl.DateTimeFormat('pt-BR', {timeZone:'America/Sao_Paulo',day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit'});
function date(value) { if (!value) return '—'; const parsed = new Date(value); return Number.isNaN(parsed.getTime()) ? '—' : dateFormatter.format(parsed); }
function duration(value) { const seconds = Math.max(0, Math.floor(Number(value) || 0)); return `${Math.floor(seconds / 3600)}h ${String(Math.floor(seconds % 3600 / 60)).padStart(2,'0')}m`; }
function el(tag, text, className) { const node = document.createElement(tag); if (text != null) node.textContent = text; if (className) node.className = className; return node; }
async function api(path, options) { let response; try { response = await fetch(path, options); } catch { throw new Error('Sem conexão com o servidor local. Verifique se o laboratório está em execução e tente novamente.'); } const body = await response.json(); if (!response.ok) throw new Error(body.message || body.error?.message || body.error || 'Não foi possível concluir a consulta.'); return body; }
function showError(message) { $('global-error').textContent = message; $('global-error').hidden = !message; }
function diagnosticText(diagnostics) {
  if (!diagnostics) return '';
  const labels = {invalidDates:'sessões com datas inválidas',unclassifiedSessions:'sessões com situação indeterminada',missingUser:'sessões sem iniciador identificado',runningColumnsWithoutSession:'relógios abertos sem sessão identificada',deletedSessions:'sessões excluídas na origem, desconsideradas',duplicateSessions:'registros duplicados, desconsiderados'};
  return Object.entries(labels).filter(([key]) => Number(diagnostics[key]) > 0).map(([key,label]) => `${diagnostics[key]} ${label}`).join(' · ');
}
function resultState(title, message, icon = '◷') { $('table-wrap').hidden = true; $('results-state').hidden = false; $('results-state').replaceChildren(el('span',icon,'empty-icon'),el('h3',title),el('p',message)); }
function resetMetrics() { for (const id of ['metric-total','metric-duration','metric-running','table-count']) $(id).textContent = '—'; }
function renderPeople() {
  const term = $('person-search').value.trim().toLocaleLowerCase('pt-BR');
  const visible = users.filter(user => `${user.name || ''} ${user.email || ''}`.toLocaleLowerCase('pt-BR').includes(term));
  $('user-count').textContent = String(users.length);
  $('users-state').hidden = visible.length > 0;
  $('users-state').textContent = users.length ? 'Nenhum perfil encontrado.' : 'Nenhum perfil ativo disponível.';
  const fragment = document.createDocumentFragment();
  for (const user of visible) {
    const selected = String(user.id) === String(selectedUser?.id);
    const button = el('button',null,`person${selected ? ' selected' : ''}`); button.type = 'button'; button.setAttribute('aria-pressed',String(selected));
    const initials = String(user.name || '?').trim().split(/\s+/).slice(0,2).map(part => part[0]).join('').toUpperCase();
    const text = el('span',null,'person-text'); text.append(el('span',user.name || 'Sem nome','person-name'),el('span',user.email || `Monday ID ${user.id}`,'person-email'));
    button.append(el('span',initials,'avatar'),text); button.addEventListener('click',() => { selectedUser = user; renderPeople(); $('results-title').textContent = user.name || 'Perfil selecionado'; $('selected-detail').textContent = `Monday ID ${user.id} · Autoria pelo iniciador do relógio`; suggestVrEmployee(); loadSessions(); }); fragment.append(button);
  }
  $('people-list').replaceChildren(fragment);
}
async function loadUsers() { try { const data = await api('/api/users'); users = data.users || []; renderPeople(); } catch (error) { $('users-state').hidden = false; $('users-state').textContent = 'Não foi possível carregar os perfis.'; showError(error.message); } }
function mondayUrl(value) { try { const url = new URL(value); return url.protocol === 'https:' && (url.hostname === 'monday.com' || url.hostname.endsWith('.monday.com')) ? url.href : null; } catch { return null; } }
async function loadSessions() {
  if (!selectedUser) return;
  const version = ++requestVersion;
  resetMetrics(); $('result-notice').hidden = true;
  const from = $('date-from').value, to = $('date-to').value;
  if (from && to && from > to) { resultState('Confira o período','A data inicial deve ser anterior ou igual à data final.','!'); return; }
  resultState('Consultando sessões…','Buscando os apontamentos da pessoa selecionada.');
  const query = new URLSearchParams({userId:selectedUser.id,state:document.querySelector('input[name="state"]:checked').value});
  if (from) query.set('from',from); if (to) query.set('to',to);
  try {
    const data = await api(`/api/sessions?${query}`); if (version !== requestVersion) return;
    if (!data.snapshotAt) {
      resetMetrics(); $('snapshot-badge').textContent = latestStatus?.syncing ? 'Coleta em andamento' : 'Aguardando coleta'; $('snapshot-badge').className = 'status-badge warning';
      resultState(latestStatus?.syncing ? 'Coleta em andamento' : 'Aguardando a primeira coleta','Os totais estarão disponíveis após a conclusão da sincronização. Ausência de dados ainda não significa zero horas.');
      return;
    }
    const sessions = data.sessions || [];
    $('metric-total').textContent = String(data.total ?? sessions.length); $('metric-duration').textContent = duration(data.summary?.durationSeconds); $('metric-running').textContent = String(data.summary?.runningCount ?? sessions.filter(s => s.running).length);
    $('table-count').textContent = `${sessions.length} ${sessions.length === 1 ? 'sessão' : 'sessões'}`;
    $('snapshot-badge').textContent = data.complete ? 'Coleta completa' : 'Coleta incompleta'; $('snapshot-badge').className = `status-badge ${data.complete ? 'success' : 'warning'}`;
    const diagnostic = diagnosticText(data.diagnostics);
    if (!data.complete || diagnostic) { $('result-notice').hidden = false; $('result-notice').textContent = [!data.complete ? 'Coleta incompleta: os resultados podem não representar todos os apontamentos.' : '',diagnostic ? `Qualidade da coleta: ${diagnostic}. Alguns registros podem não aparecer nos filtros por pessoa, situação ou período.` : '', 'Não interprete ausência de sessões como zero horas.'].filter(Boolean).join(' '); }
    if (!sessions.length) { resultState(data.complete ? 'Nenhuma sessão neste filtro' : 'Nenhuma sessão disponível',data.complete ? 'Experimente outro período ou situação. A consulta considera quem iniciou cada relógio.' : 'Sincronize os dados e confira a conclusão da coleta antes de avaliar as horas.'); return; }
    currentSessions = sessions; renderedSessions = 0; $('sessions-body').replaceChildren(); renderMoreSessions(); $('results-state').hidden = true; $('table-wrap').hidden = false;
  } catch (error) { if (version !== requestVersion) return; $('snapshot-badge').textContent = 'Consulta falhou'; $('snapshot-badge').className = 'status-badge warning'; resultState('Não foi possível consultar',error.message,'!'); }
}
function renderMoreSessions() {
    const fragment = document.createDocumentFragment();
    const batch = currentSessions.slice(renderedSessions,renderedSessions + 100);
    for (const session of batch) {
      const row = el('tr'); const activity = el('td'); const url = mondayUrl(session.itemUrl);
      const title = el(url ? 'a' : 'span',session.itemName || `Item ${session.itemId}`); if (url) { title.href = url; title.target = '_blank'; title.rel = 'noopener noreferrer'; title.setAttribute('aria-label',`${session.itemName || 'Atividade'} — abrir no Monday`); }
      activity.append(title,el('span',`Item ${session.itemId} · Sessão ${session.sessionId ?? '—'}`,'cell-sub'));
      const state = el('td'); state.append(el('span',session.running ? 'Em andamento' : session.endedAt ? 'Encerrada' : 'Indeterminada',`pill${session.running ? ' running' : ''}`)); if (session.manual) { state.append(el('br'),el('span','Manual','pill manual')); }
      row.append(activity,el('td',date(session.startedAt)),el('td',session.running ? 'Em andamento' : date(session.endedAt)),el('td',duration(session.durationSeconds)),state); fragment.append(row);
    }
    $('sessions-body').append(fragment); renderedSessions += batch.length; $('load-more').hidden = renderedSessions >= currentSessions.length;
    $('table-count').textContent = `${renderedSessions} de ${currentSessions.length} sessões`;
}
function renderStatus(status) {
  latestStatus = status; $('sync-button').disabled = !!status.syncing; $('sync-button').textContent = status.syncing ? 'Sincronizando…' : 'Sincronizar Monday ↗';
  $('sync-message').textContent = status.syncing ? `Coletando itens e sessões… ${status.itemsRead || 0} itens lidos em ${status.pagesRead || 0} páginas.` : status.complete ? 'Coleta completa dos itens ativos e subitens deste quadro. Não inclui itens arquivados ou excluídos.' : status.hasSnapshot ? 'A última tentativa ficou incompleta. Há dados de uma coleta anterior disponíveis.' : 'Ainda não há uma coleta completa. Sincronize para começar a explorar.';
  $('sync-details').textContent = `Última coleta concluída: ${date(status.lastSuccessAt)} · ${status.sessionCount || 0} sessões · ${status.runningCount || 0} em andamento`;
  const diagnostic = diagnosticText(status.diagnostics); $('sync-diagnostics').hidden = !diagnostic; $('sync-diagnostics').textContent = diagnostic ? `Qualidade dos dados: ${diagnostic}. Coleta concluída não garante que todas as sessões possam ser atribuídas ou filtradas.` : '';
  if (status.errors?.length) { showError(status.errors.map(error => typeof error === 'string' ? error : error.message || error.code).join(' · ')); }
  else showError('');
}
async function pollStatus() {
  try { const previous = latestStatus; const status = await api('/api/status'); renderStatus(status); if (selectedUser && ((previous?.syncing && !status.syncing) || (status.lastSuccessAt && status.lastSuccessAt !== lastSnapshot))) await loadSessions(); lastSnapshot = status.lastSuccessAt; }
  catch (error) { $('sync-message').textContent = 'Servidor local indisponível. Verifique se o laboratório está em execução.'; $('sync-button').disabled = false; showError(error.message); }
  finally { setTimeout(pollStatus,latestStatus?.syncing ? 1500 : 8000); }
}
$('person-search').addEventListener('input',renderPeople);
$('load-more').addEventListener('click',renderMoreSessions);
$('filters').addEventListener('submit',event => event.preventDefault());
$('filters').addEventListener('change',loadSessions);
$('clear-filters').addEventListener('click',() => { $('date-from').value = ''; $('date-to').value = ''; document.querySelector('input[name="state"][value="all"]').checked = true; loadSessions(); });
$('sync-button').addEventListener('click',async () => { $('sync-button').disabled = true; showError(''); try { renderStatus(await api('/api/sync',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})); } catch (error) { showError(error.message); $('sync-button').disabled = false; } });
function normalized(value) { return String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g,'').trim().replace(/\s+/g,' ').toLocaleLowerCase('pt-BR'); }
function resetVr(message = 'Consulta pendente. Confira o funcionário e o período, depois clique em Consultar ponto.') {
  vrRequestVersion++; $('vr-results').hidden = true; $('vr-body').replaceChildren(); $('vr-state').hidden = false; $('vr-state').textContent = message; $('vr-state').className = 'vr-state'; $('vr-freshness').textContent = 'Ainda não consultado'; $('vr-query').disabled = !$('vr-employee').value;
}
function suggestVrEmployee() {
  resetVr(); $('vr-employee').value = '';
  const email = String(selectedUser?.email || '').trim().toLowerCase();
  const emailMatches = email ? vrEmployees.filter(person => String(person.email || '').trim().toLowerCase() === email) : [];
  const name = normalized(selectedUser?.name);
  const nameMatches = name ? vrEmployees.filter(person => normalized(person.name) === name) : [];
  const candidates = emailMatches.length ? emailMatches : nameMatches;
  if (candidates.length === 1) { $('vr-employee').value = String(candidates[0].id); $('vr-match').textContent = `Sugestão por ${emailMatches.length ? 'e-mail' : 'nome completo'}: ${candidates[0].name}. Confira e clique em Consultar ponto para confirmar esta consulta.`; }
  else $('vr-match').textContent = candidates.length > 1 ? 'Mais de um funcionário corresponde ao perfil. Escolha o funcionário correto antes de consultar.' : 'Sem sugestão única. Escolha o funcionário do VR para esta consulta; nenhum vínculo será salvo.';
  $('vr-query').disabled = !$('vr-employee').value;
}
async function loadVrEmployees() {
  try { const data = await api('/api/vr/employees'); vrEmployees = data.employees || []; const fragment = document.createDocumentFragment(); const placeholder = el('option','Selecione um funcionário'); placeholder.value = ''; fragment.append(placeholder); for (const person of vrEmployees) { const option = el('option',`${person.name || 'Sem nome'}${person.email ? ` · ${person.email}` : ''} · ID ${person.id}`); option.value = String(person.id); fragment.append(option); } $('vr-employee').replaceChildren(fragment); $('vr-employee').disabled = !vrEmployees.length; if (selectedUser) suggestVrEmployee(); else resetVr(vrEmployees.length ? undefined : 'Nenhum funcionário disponível no VR.'); }
  catch (error) { $('vr-employee').replaceChildren(el('option','Funcionários indisponíveis')); $('vr-state').textContent = error.message; $('vr-state').className = 'vr-state vr-error'; }
}
function vrDay(value) { return /^\d{4}-\d{2}-\d{2}$/.test(String(value)) ? value.split('-').reverse().join('/') : '—'; }
function vrTimeCards(cards) { if (!Array.isArray(cards) || !cards.length) return '—'; return cards.map(card => typeof card === 'string' ? card : card?.time || card?.timeText || card?.hour || card?.timestamp || 'Horário indisponível').join(' · '); }
async function loadVrDays(event) {
  event.preventDefault(); resetVr(); const version = vrRequestVersion;
  const employeeId = $('vr-employee').value, from = $('vr-from').value, to = $('vr-to').value;
  const count = (Date.parse(to) - Date.parse(from)) / 86400000 + 1;
  if (!employeeId || !from || !to || !Number.isFinite(count) || count < 1 || count > 31) { $('vr-state').textContent = 'Selecione um funcionário e um período válido de até 31 dias.'; return; }
  const employee = vrEmployees.find(person => String(person.id) === employeeId);
  $('vr-query').disabled = true; $('vr-state').textContent = 'Consultando os dias e as batidas no VR…'; $('vr-freshness').textContent = 'Carregando…';
  try {
    const data = await api(`/api/vr/work-days?${new URLSearchParams({employeeId,from,to})}`); if (version !== vrRequestVersion) return;
    const rows = data.rows || []; const known = rows.filter(row => row.totalSeconds !== null && row.totalSeconds !== undefined && Number.isFinite(Number(row.totalSeconds)));
    const complete = !!data.coverage?.complete && known.length === rows.length;
    $('vr-total-label').textContent = complete ? 'Total informado pelo VR' : 'Subtotal dos dias com total disponível';
    $('vr-total').textContent = known.length ? duration(known.reduce((sum,row) => sum + Number(row.totalSeconds),0)) : '—';
    $('vr-coverage').textContent = `${employee?.name || 'Funcionário selecionado'} · ${known.length} de ${data.coverage?.requestedDays ?? count} dias com total disponível. ${complete ? '' : 'Dias ausentes ou não reconhecidos não representam zero horas.'}`;
    const vrDiagnostics = data.diagnostics || {};
    const vrWarnings = [['duplicateDays','dias repetidos'],['unrecognizedRows','linhas não reconhecidas'],['outOfRangeRows','linhas fora do período'],['unrecognizedTimeCards','batidas com formato não reconhecido']].filter(([key]) => Number(vrDiagnostics[key]) > 0).map(([key,label]) => `${vrDiagnostics[key]} ${label}`);
    if (vrWarnings.length) $('vr-coverage').textContent += ` Atenção: ${vrWarnings.join(' · ')}.`;
    $('vr-freshness').textContent = `Consultado em ${date(data.fetchedAt)}`;
    const fragment = document.createDocumentFragment();
    for (const day of rows) { const row = el('tr'); const totalKnown = day.totalSeconds !== null && day.totalSeconds !== undefined && Number.isFinite(Number(day.totalSeconds)); row.append(el('td',vrDay(day.date)),el('td',totalKnown ? day.totalText || duration(day.totalSeconds) : '—'),el('td',vrTimeCards(day.timeCards)),el('td',day.state === 'reported' ? 'Informado' : day.state === 'missing' ? 'Não retornado pelo VR' : 'Total não reconhecido')); fragment.append(row); }
    $('vr-body').replaceChildren(fragment); $('vr-results').hidden = !rows.length; $('vr-state').hidden = rows.length > 0; $('vr-state').textContent = 'O VR não retornou dias para esta consulta. Ausência de dados não significa zero horas.';
  } catch (error) { if (version !== vrRequestVersion) return; $('vr-state').textContent = error.message; $('vr-state').className = 'vr-state vr-error'; $('vr-freshness').textContent = 'Consulta falhou'; }
  finally { if (version === vrRequestVersion) $('vr-query').disabled = !$('vr-employee').value; }
}
const saoPauloToday = new Intl.DateTimeFormat('en-CA',{timeZone:'America/Sao_Paulo',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
const yesterday = new Date(`${saoPauloToday}T12:00:00Z`); yesterday.setUTCDate(yesterday.getUTCDate()-1);
const vrEnd = yesterday.toISOString().slice(0,10); $('vr-to').value = vrEnd; $('vr-from').value = `${vrEnd.slice(0,7)}-01`;
$('vr-employee').addEventListener('change',() => { resetVr(); $('vr-match').textContent = 'Funcionário selecionado manualmente. Confira a escolha antes de consultar; nenhum vínculo será salvo.'; });
for (const id of ['vr-from','vr-to']) $(id).addEventListener('change',() => resetVr());
$('vr-form').addEventListener('submit',loadVrDays);
loadUsers(); pollStatus(); loadVrEmployees();
