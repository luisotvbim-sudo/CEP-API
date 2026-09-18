import { validatePeriod } from './vr.mjs';
import { buildShiftComparison } from './shift-comparison.mjs';

const dayMs = 86400000;
const dateFormatter = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' });
const localDate = value => dateFormatter.format(new Date(value));
const validInstant = value => value != null && Number.isFinite(Date.parse(value));
const safeError = (code, message) => Object.assign(new Error(message), { code, safe: true });

/** Arithmetic exploration only. Identity matching, source semantics and jornada rules are not homologated. */
export function buildComparison({ mondaySnapshot, mondayStatus, vrReport, userId, employeeId, from, to, now = Date.now() }) {
  validatePeriod({ employeeId, from, to });
  if (!/^\d+$/.test(String(userId || ''))) throw safeError('invalid_filter', 'Selecione um perfil válido do Monday.');
  if (vrReport && (String(vrReport.employeeId) !== String(employeeId) || vrReport.from !== from || vrReport.to !== to)) throw safeError('comparison_source_mismatch', 'O relatório de ponto não corresponde ao colaborador ou período solicitado.');
  const nowInstant = now instanceof Date ? now.getTime() : typeof now === 'string' ? Date.parse(now) : now;
  if (!Number.isFinite(nowInstant)) throw safeError('invalid_filter', 'Data de referência inválida.');
  const today = localDate(nowInstant);
  const mondayComplete = Boolean(mondaySnapshot && Array.isArray(mondaySnapshot.sessions) && mondayStatus?.complete === true && !mondayStatus?.syncing);
  const warnings = [{ code: 'exploratory_comparison', message: 'Comparação de referência para testes. Identidade, total_time do VR e regras de jornada ainda precisam de homologação; não indica conciliação aprovada.' }];
  if (!mondayComplete) warnings.push({ code: 'monday_incomplete', message: 'A coleta Monday não está concluída. Os totais Monday e as diferenças foram suspensos.' });
  const diagnostics = mondaySnapshot?.diagnostics || {};
  const globalDiagnosticKeys = ['missingUser', 'invalidDates', 'unclassifiedSessions', 'runningColumnsWithoutSession'];
  const globalReview = globalDiagnosticKeys.some(key => Number(diagnostics[key]) > 0);
  const unknownAttribution = ['missingUser', 'invalidDates'].some(key => Number(diagnostics[key]) > 0);
  if (globalReview) warnings.push({ code: 'monday_global_diagnostics', message: 'A coleta contém sessões sem classificação, datas válidas ou autoria identificada. Os diagnósticos globais não permitem localizar todos os dias afetados; revise os valores antes de usá-los.' });
  if (vrReport?.coverage?.complete === false) warnings.push({ code: 'vr_partial_coverage', message: 'A cobertura VR contém lacunas ou dados não reconhecidos. Apenas os dias com total diário reconhecido podem ter diferença calculada.' });
  const rows = [];
  const vrDays = new Map();
  for (const row of vrReport?.rows || []) {
    if (vrDays.has(row.date)) vrDays.set(row.date, null);
    else vrDays.set(row.date, row);
  }
  for (let date = Date.parse(from); date <= Date.parse(to); date += dayMs) {
    const key = new Date(date).toISOString().slice(0, 10);
    const vr = vrDays.get(key);
    const row = { date: key, mondaySeconds: mondayComplete ? 0 : null, vrSeconds: vr?.state === 'reported' && Number.isFinite(vr.totalSeconds) && vr.totalSeconds >= 0 ? vr.totalSeconds : null, differenceSeconds: null, comparable: false, state: 'missing', reasons: [], sessions: [], timeCards: Array.isArray(vr?.timeCards) ? [...vr.timeCards] : [] };
    if (!mondayComplete) row.reasons.push('monday_incomplete');
    if (row.vrSeconds === null) row.reasons.push('vr_missing_or_unrecognized');
    if (key >= today) row.reasons.push('current_or_future_day');
    rows.push(row);
  }
  const seen = new Set();
  for (const session of mondaySnapshot?.sessions || []) {
    if (String(session.userId) !== String(userId)) continue;
    const identity = `${session.itemId}:${session.columnId}:${session.sessionId}`;
    if (seen.has(identity)) continue;
    seen.add(identity);
    if (!validInstant(session.startedAt)) continue;
    const startDate = localDate(session.startedAt);
    const hasEnd = validInstant(session.endedAt);
    const open = session.running === true || !hasEnd;
    const endInstant = hasEnd ? Date.parse(session.endedAt) : nowInstant;
    const endDate = localDate(Math.max(Date.parse(session.startedAt), !open && endInstant > Date.parse(session.startedAt) ? endInstant - 1 : endInstant));
    const crossesMidnight = startDate !== endDate;
    for (const row of rows) {
      if (row.date < startDate || row.date > endDate) continue;
      row.sessions.push({ ...session });
      if (open) row.reasons.push('open_session');
      if (crossesMidnight) row.reasons.push('cross_midnight_session');
      if (endInstant < Date.parse(session.startedAt)) row.reasons.push('invalid_session_duration');
      if (!open && !crossesMidnight && endInstant >= Date.parse(session.startedAt) && mondayComplete) row.mondaySeconds += Math.floor((endInstant - Date.parse(session.startedAt)) / 1000);
    }
  }
  for (const row of rows) {
    if (unknownAttribution && !row.sessions.length) { row.mondaySeconds = null; row.reasons.push('unknown_session_attribution'); }
    if (row.reasons.some(reason => ['open_session', 'cross_midnight_session', 'invalid_session_duration'].includes(reason))) row.mondaySeconds = null;
    row.reasons = [...new Set(row.reasons)];
    row.comparable = row.mondaySeconds !== null && row.vrSeconds !== null && !row.reasons.includes('current_or_future_day');
    if (row.comparable) {
      row.differenceSeconds = row.mondaySeconds - row.vrSeconds;
      row.state = row.mondaySeconds === 0 && row.vrSeconds === 0 ? 'no_records' : globalReview ? 'review' : 'comparable';
      if (row.state === 'no_records') { row.differenceSeconds = null; row.comparable = false; row.reasons.push('no_records_in_both_sources'); }
      if (globalReview) row.reasons.push('monday_global_diagnostics');
    } else row.state = row.reasons.some(reason => ['current_or_future_day', 'open_session'].includes(reason)) ? 'provisional' : row.reasons.some(reason => ['cross_midnight_session', 'invalid_session_duration', 'unknown_session_attribution'].includes(reason)) ? 'review' : 'missing';
  }
  const compared = rows.filter(row => row.differenceSeconds !== null);
  for (const row of rows) row.shiftComparison = buildShiftComparison({ date: row.date, timeCards: row.timeCards, timeCardsComplete: vrDays.get(row.date)?.timeCardsComplete, vrSeconds: row.vrSeconds, sessions: mondaySnapshot?.sessions || [], userId, mondayComplete, diagnostics, now: nowInstant });
  const sum = selector => compared.length ? compared.reduce((total, row) => total + selector(row), 0) : null;
  return {
    userId: String(userId), employeeId: String(employeeId), from, to, exploratory: true,
    rows,
    summary: { comparedDays: compared.length, excludedDays: rows.length - compared.length, mondaySeconds: sum(row => row.mondaySeconds), vrSeconds: sum(row => row.vrSeconds), differenceSeconds: sum(row => row.differenceSeconds), lessSeconds: sum(row => Math.max(0, -row.differenceSeconds)), moreSeconds: sum(row => Math.max(0, row.differenceSeconds)), absoluteDifferenceSeconds: sum(row => Math.abs(row.differenceSeconds)) },
    sources: { mondaySnapshotAt: mondaySnapshot?.at || null, vrFetchedAt: vrReport?.fetchedAt || null, mondayComplete, comparedAt: new Date(nowInstant).toISOString(), timezone: 'America/Sao_Paulo', mondayDiagnostics: diagnostics },
    warnings,
  };
}
