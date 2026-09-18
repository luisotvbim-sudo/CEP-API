const zone = 'America/Sao_Paulo';
const formatter = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
function wallParts(instant) { return Object.fromEntries(formatter.formatToParts(new Date(instant)).filter(part => part.type !== 'literal').map(part => [part.type, part.value])); }
function wallNumber(parts) { return Date.UTC(+parts.year, +parts.month - 1, +parts.day, +parts.hour, +parts.minute, +parts.second); }
export function saoPauloInstant(date, time) {
  const target = Date.parse(`${date}T${time}:00Z`);
  if (!Number.isFinite(target) || date < '1970-01-01') return null;
  const offsets = new Set([-36, 0, 36].map(hours => { const sample = target + hours * 3600000; return wallNumber(wallParts(sample)) - sample; }));
  const candidates = [...offsets].map(offset => target - offset).filter(instant => wallNumber(wallParts(instant)) === target);
  return candidates.length === 1 ? candidates[0] : null;
}
const overlap = (start, end, a, b) => Math.max(0, Math.min(end, b) - Math.max(start, a));
function measured(intervals) {
  const sorted = intervals.filter(([a, b]) => b > a).sort((a, b) => a[0] - b[0]);
  let sum = 0; let union = 0; let lastStart = null; let lastEnd = null;
  for (const [start, end] of sorted) {
    sum += end - start;
    if (lastStart === null) { lastStart = start; lastEnd = end; }
    else if (start <= lastEnd) lastEnd = Math.max(lastEnd, end);
    else { union += lastEnd - lastStart; lastStart = start; lastEnd = end; }
  }
  if (lastStart !== null) union += lastEnd - lastStart;
  return { mondaySeconds: sum / 1000, coverageSeconds: union / 1000, overlapSeconds: (sum - union) / 1000 };
}

export function buildShiftComparison({ date, timeCards, timeCardsComplete, vrSeconds, sessions = [], userId, mondayComplete, diagnostics = {}, now = Date.now() }) {
  const result = { state: 'unavailable', reasons: [], turns: [], outsideSeconds: null, pointSeconds: null, mondaySeconds: null, differenceSeconds: null, overlapSeconds: null, coverageSeconds: null };
  const nowInstant = typeof now === 'number' ? now : new Date(now).getTime();
  const todayParts = wallParts(nowInstant); const today = `${todayParts.year}-${todayParts.month}-${todayParts.day}`;
  const unavailable = reason => { result.reasons.push(reason); return result; };
  if (!/^\d+$/.test(String(userId || ''))) return unavailable('missing_user');
  if (timeCardsComplete === false) return unavailable('incomplete_time_cards');
  if (!Array.isArray(timeCards) || !timeCards.length) return unavailable('no_time_cards');
  if (timeCards.length % 2) return unavailable('odd_time_cards');
  if (timeCards.some(time => typeof time !== 'string' || !/^([01]\d|2[0-3]):[0-5]\d$/.test(time))) return unavailable('invalid_time_cards');
  if (timeCards.some((time, index) => index && time <= timeCards[index - 1])) return unavailable('unordered_or_overnight_time_cards');
  const nextDate = new Date(Date.parse(`${date}T12:00:00Z`) + 86400000).toISOString().slice(0, 10);
  const dayStart = saoPauloInstant(date, '00:00'); const dayEnd = saoPauloInstant(nextDate, '00:00');
  const instants = timeCards.map(time => saoPauloInstant(date, time));
  if (dayStart === null || dayEnd === null || instants.some(instant => instant === null)) return unavailable('ambiguous_timezone');
  for (let index = 0; index < timeCards.length; index += 2) result.turns.push({ index: index / 2 + 1, start: timeCards[index], end: timeCards[index + 1], pointSeconds: (instants[index + 1] - instants[index]) / 1000, mondaySeconds: null, differenceSeconds: null, overlapSeconds: null, coverageSeconds: null });
  result.pointSeconds = result.turns.reduce((sum, turn) => sum + turn.pointSeconds, 0);
  const globalReview = ['missingUser', 'invalidDates', 'unclassifiedSessions', 'runningColumnsWithoutSession'].some(key => Number(diagnostics[key]) > 0);
  if (globalReview) result.reasons.push('monday_global_diagnostics');
  if (Number.isFinite(vrSeconds) && vrSeconds !== result.pointSeconds) result.reasons.push('point_total_mismatch');
  if (!mondayComplete) result.reasons.push('monday_incomplete');
  if (date >= today) result.reasons.push('current_or_future_day');
  const intervals = []; const seen = new Set();
  for (const session of sessions) {
    if (String(session.userId) !== String(userId)) continue;
    const key = `${session.itemId}:${session.columnId}:${session.sessionId}`;
    if (seen.has(key)) continue; seen.add(key);
    const start = Date.parse(session.startedAt);
    const end = session.endedAt ? Date.parse(session.endedAt) : NaN;
    if (!Number.isFinite(start)) { result.reasons.push('invalid_session_dates'); continue; }
    if (session.running || !Number.isFinite(end)) {
      if (overlap(start, Math.max(start, nowInstant), dayStart, dayEnd) > 0) result.reasons.push('open_session');
      continue;
    }
    if (end < start) { if (start >= dayStart && start < dayEnd) result.reasons.push('invalid_session_dates'); continue; }
    if (overlap(start, end, dayStart, dayEnd) > 0) intervals.push([Math.max(start, dayStart), Math.min(end, dayEnd)]);
  }
  if (!intervals.length && ['missingUser', 'invalidDates'].some(key => Number(diagnostics[key]) > 0)) result.reasons.push('unknown_session_attribution');
  result.reasons = [...new Set(result.reasons)];
  if (result.reasons.some(reason => ['monday_incomplete', 'current_or_future_day', 'open_session', 'invalid_session_dates', 'unknown_session_attribution'].includes(reason))) {
    result.state = result.reasons.some(reason => ['current_or_future_day', 'open_session'].includes(reason)) ? 'provisional' : 'unavailable'; return result;
  }
  let inside = 0;
  for (let index = 0; index < result.turns.length; index++) {
    const turn = result.turns[index];
    const stats = measured(intervals.map(([start, end]) => [Math.max(start, instants[index * 2]), Math.min(end, instants[index * 2 + 1])]));
    Object.assign(turn, stats, { differenceSeconds: stats.mondaySeconds - turn.pointSeconds }); inside += stats.mondaySeconds;
  }
  result.mondaySeconds = inside;
  result.differenceSeconds = inside - result.pointSeconds;
  result.outsideSeconds = measured(intervals).mondaySeconds - inside;
  result.coverageSeconds = result.turns.reduce((sum, turn) => sum + turn.coverageSeconds, 0);
  result.overlapSeconds = result.turns.reduce((sum, turn) => sum + turn.overlapSeconds, 0);
  if (measured(intervals).overlapSeconds > 0) result.reasons.push('overlapping_sessions');
  result.state = result.reasons.length ? 'review' : 'comparable';
  return result;
}
