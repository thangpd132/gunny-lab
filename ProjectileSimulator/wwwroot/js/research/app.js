// =============================================================================
// app.js – UI wiring for the simulator tab: sliders, buttons, results, history.
// Physics is computed by the backend (simulator.js → /api/simulation/*).
// =============================================================================
import { SimCanvas } from './canvas.js';
import { Api, ShotAnimator, LatestRunner } from './simulator.js';
import { initReverse } from './reverse.js';
import { initGunny } from './gunny.js';

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

// ------------------------------------------------------------------ state

/** UI defaults. Physics defaults are overwritten by GET /api/simulation/defaults at start-up. */
const DEFAULTS = {
  distance: 5, angle: 45, power: 50, wind: 0, gravity: 9.81,
  windEnabled: true, windMode: 'WindAcceleration',
  dragEnabled: false, dragType: 'Quadratic', dragCoefficient: 0.47, airDensity: 1.225, crossSectionArea: 0.07, mass: 1,
  integrator: 'SemiImplicitEuler', deltaTime: 0.016, maxTime: 30, playbackSpeed: 0.6,
  barrelLength: 0.3, targetWidth: 0.4, targetHeight: 0.6,
  'cal.velocityMultiplier': 1, 'cal.powerMultiplier': 1, 'cal.powerExponent': 1, 'cal.gravityMultiplier': 1,
  'cal.windMultiplier': 1, 'cal.angleOffset': 0, 'cal.launchHeight': 0.4, 'cal.powerToVelocity': 0.14,
  'cal.windAccelerationScale': 0.1, 'cal.windDriftScale': 0.05,
};

const params = { ...DEFAULTS };
const PALETTE = ['#ff7a3d', '#39d0ff', '#a3e635', '#f472b6', '#facc15', '#a78bfa', '#2dd4bf', '#fb7185', '#60a5fa', '#fdba74'];
const MODEL_COLORS = { Basic: '#4fc3f7', Wind: '#81c784', Drag: '#ffb74d', Custom: '#f06292' };
const HISTORY_KEY = 'gunnylab.history.v1';
const MAX_HISTORY = 300;

let history = [];          // [{id, n, time, color, visible, label, request, summary, points}]
let compareEntries = [];   // trajectories from Compare (drawn in model colours)
let extraLayers = [];      // trajectories added by the RE tab (experiments)
let preview = null;        // live preview result
let lastResult = null;
let historyCounter = 0;

// ------------------------------------------------------------------ helpers

function toast(msg, isError = false) {
  const t = $('#toast');
  t.textContent = msg;
  t.classList.toggle('error', isError);
  t.classList.add('show');
  clearTimeout(toast._t);
  toast._t = setTimeout(() => t.classList.remove('show'), isError ? 5000 : 2200);
}

const fmt = (v, d = 3) => (v == null || !isFinite(v) ? '—' : Number(v).toFixed(d));

function decimals(step) {
  const s = String(step);
  return s.includes('.') ? s.split('.')[1].length : 0;
}

/** Full SimulationRequest for the backend, built from the UI state. */
export function buildRequest(overrides = {}) {
  const cal = {};
  for (const [k, v] of Object.entries(params)) if (k.startsWith('cal.')) cal[k.slice(4)] = v;
  return {
    distance: params.distance,
    angle: params.angle,
    power: params.power,
    wind: params.wind,
    gravity: params.gravity,
    windEnabled: params.windEnabled,
    windMode: params.windMode,
    dragType: params.dragEnabled ? params.dragType : 'None',
    dragCoefficient: params.dragCoefficient,
    airDensity: params.airDensity,
    crossSectionArea: params.crossSectionArea,
    mass: params.mass,
    integrator: params.integrator,
    deltaTime: params.deltaTime,
    maxTime: params.maxTime,
    maxTrajectoryPoints: 3000,
    barrelLength: params.barrelLength,
    targetWidth: params.targetWidth,
    targetHeight: params.targetHeight,
    targetCollisionEnabled: true,
    groundLevel: 0,
    calibration: cal,
    ...overrides,
  };
}

// ------------------------------------------------------------------ sliders

const widgets = new Map(); // key → [{range, number, root}]

function buildSliders() {
  for (const el of $$('.slider')) {
    const key = el.dataset.key;
    const min = +el.dataset.min, max = +el.dataset.max, step = +el.dataset.step;
    const dec = decimals(el.dataset.step);
    el.innerHTML = `
      <div class="top">
        <span class="name">${el.dataset.label}${el.dataset.hint ? `<span class="hint">${el.dataset.hint}</span>` : ''}</span>
        <span class="val"><input type="number" step="${step}" aria-label="${el.dataset.label} value"><span class="unit">${el.dataset.unit ?? ''}</span></span>
      </div>
      <input type="range" min="${min}" max="${max}" step="${step}" aria-label="${el.dataset.label}">`;
    const range = $('input[type=range]', el);
    const number = $('input[type=number]', el);
    const w = { range, number, root: el, dec, min, max };
    if (!widgets.has(key)) widgets.set(key, []);
    widgets.get(key).push(w);

    range.addEventListener('input', () => setParam(key, +range.value, w));
    // Number box: any value is accepted (sliders only bound the drag range), validated by the backend.
    number.addEventListener('change', () => {
      const v = parseFloat(number.value);
      if (!isFinite(v)) { syncWidgets(key); return; }
      setParam(key, v, null);
    });
    number.addEventListener('keydown', (e) => { if (e.key === 'Enter') number.blur(); });
  }
}

function syncWidgets(key) {
  const v = params[key];
  for (const w of widgets.get(key) ?? []) {
    w.range.value = String(Math.min(w.max, Math.max(w.min, v)));
    w.number.value = Number(v).toFixed(w.dec);
    w.root.classList.toggle('changed', Math.abs(v - DEFAULTS[key]) > 1e-12);
  }
}

function syncAll() {
  for (const key of widgets.keys()) syncWidgets(key);
  $('#windEnabled').checked = params.windEnabled;
  $('#windMode').value = params.windMode;
  $('#dragEnabled').checked = params.dragEnabled;
  $('#dragType').value = params.dragType;
  $('#integrator').value = params.integrator;
  updateDisabledStates();
}

function setParam(key, value, sourceWidget) {
  params[key] = value;
  // keep every widget bound to the same key in sync (e.g. drag coefficient appears on both tabs)
  for (const w of widgets.get(key) ?? []) {
    if (w === sourceWidget) w.number.value = Number(value).toFixed(w.dec);
  }
  syncWidgets(key);
  onParamsChanged(key);
}

/** Apply several parameters at once (used by Reset and by the optimizer's "Apply"). */
export function applyParams(patch) {
  Object.assign(params, patch);
  syncAll();
  onParamsChanged('*');
}

function updateDisabledStates() {
  $('#windMode').disabled = !params.windEnabled;
  $('#dragType').disabled = !params.dragEnabled;
}

function onParamsChanged(key) {
  if (key === 'playbackSpeed') { animator.setSpeed(params.playbackSpeed); return; }
  updateScene();
  if ($('#livePreview').checked) previewRunner.schedule();
  else if (preview) { preview = null; refreshTrajectories(); }
}

function updateScene() {
  sim.setScene({
    distance: params.distance,
    angle: params.angle + params['cal.angleOffset'],
    power: params.power,
    wind: params.wind,
    windEnabled: params.windEnabled,
    windMode: params.windMode,
    launchHeight: params['cal.launchHeight'],
    barrelLength: params.barrelLength,
    targetWidth: params.targetWidth,
    targetHeight: params.targetHeight,
  });
}

// ------------------------------------------------------------------ canvas + animation

const sim = new SimCanvas($('#simCanvas'), {
  onTargetDrag: (x) => {
    const d = Math.round(Math.min(10, Math.max(1, x)) * 10) / 10;
    if (d !== params.distance) setParam('distance', d, null);
  },
  onHover: (p) => {
    $('#hud').textContent = p ? `x = ${p.x.toFixed(2)} u   y = ${p.y.toFixed(2)} u   (${sim.view.scale.toFixed(1)} px/u)` : '';
  },
});

const animator = new ShotAnimator({
  onFrame: (f) => {
    const trail = f.points.slice(0, f.index + 1);
    sim.setProjectile({ x: f.x, y: f.y, vx: f.vx, vy: f.vy, t: f.t, trail, color: '#ff7a3d' });
  },
  onFinish: (result) => {
    sim.setProjectile(null);
    sim.addImpactEffect(result.landingX, result.landingY, result.hitTarget);
    commit(result, 'Fire');
  },
  onStateChange: (s) => updateAnimButtons(s),
});

function updateAnimButtons(state) {
  $('#btnPause').disabled = state !== 'playing';
  $('#btnResume').disabled = state !== 'paused';
}

const previewRunner = new LatestRunner(async (_, signal) => {
  try {
    const r = await Api.run(buildRequest(), signal);
    preview = r;
    setApiStatus(true);
    refreshTrajectories();
  } catch (e) {
    if (e.name === 'AbortError') return;
    preview = null;
    refreshTrajectories();
    if (e.status === 400) toast(e.message, true); else setApiStatus(false, e.message);
  }
}, 20);

/** Push every visible trajectory to the canvas. */
function refreshTrajectories() {
  const list = [];
  for (const h of history) {
    if (h.visible && h.points) list.push({ id: h.id, points: h.points, result: h.summary, color: h.color, width: 2, alpha: 0.95 });
  }
  for (const c of compareEntries) list.push(c);
  for (const e of extraLayers) list.push(e);
  sim.setScene({ trajectories: list, preview: preview ? { points: preview.trajectory, result: preview } : null });
  renderLegend(list);
}

function renderLegend(list) {
  const items = list.filter(t => t.label || t.id != null).slice(-12);
  const html = items.map(t => {
    const h = t.id != null ? history.find(x => x.id === t.id) : null;
    const name = t.label ?? (h ? `#${h.n} θ${fmt(h.request.angle, 0)}° P${fmt(h.request.power, 0)} W${fmt(h.request.wind, 1)}` : '');
    return `<div class="item"><span class="sw ${t.dash ? 'dash' : ''}" style="--c:${t.color}"></span>${escapeHtml(name)}</div>`;
  });
  if (preview) html.push(`<div class="item"><span class="sw dash" style="--c:#e6f1ff"></span>xem trước</div>`);
  $('#legend').innerHTML = html.join('');
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// ------------------------------------------------------------------ actions

async function simulateOnce() {
  const req = buildRequest();
  const r = await Api.run(req);
  setApiStatus(true);
  return { req, r };
}

async function onSimulate() {
  try {
    animator.stop();
    sim.setProjectile(null);
    const { req, r } = await simulateOnce();
    commit(r, 'Simulate', req);
  } catch (e) { handleError(e); }
}

async function onFire() {
  try {
    const { req, r } = await simulateOnce();
    firedRequests.set(r, req);
    animator.start(r, params.playbackSpeed);
  } catch (e) { handleError(e); }
}
const firedRequests = new WeakMap(); // result → request that produced it

function commit(result, source, request = firedRequests.get(result) ?? buildRequest()) {
  const { trajectory, ...summary } = result;
  const entry = {
    id: ++historyCounter,
    n: historyCounter,
    time: new Date().toISOString(),
    color: PALETTE[(historyCounter - 1) % PALETTE.length],
    visible: true,
    source,
    request,
    summary,
    points: trajectory,
  };
  history.push(entry);
  if (history.length > MAX_HISTORY) history.splice(0, history.length - MAX_HISTORY);
  lastResult = result;
  renderResult(result);
  renderHistory();
  saveHistory();
  refreshTrajectories();
  sim.autoFrame();
}

function onClearTrajectory() {
  animator.stop();
  sim.setProjectile(null);
  for (const h of history) h.visible = false;
  compareEntries = [];
  extraLayers = [];
  preview = null;
  $('#compareCard').classList.add('hidden');
  renderHistory();
  saveHistory();
  refreshTrajectories();
  toast('Đã xoá đường đạn (giữ lịch sử)');
}

function onReset() {
  animator.stop();
  sim.setProjectile(null);
  applyParams({ ...DEFAULTS });
  $('#livePreview').checked = true;
  sim.resetView();
  toast('Đã đặt lại mọi thông số về mặc định');
}

/** Run model presets (A–D) on the current parameters and overlay them. */
export async function runCompare(models) {
  if (!models.length) { toast('Hãy chọn ít nhất một mô hình', true); return; }
  try {
    const res = await Api.compare({ base: buildRequest(), models });
    compareEntries = res.entries.map(e => ({
      points: e.result.trajectory,
      result: e.result,
      color: MODEL_COLORS[e.model] ?? '#fff',
      label: e.label,
      width: 2.2,
      dash: e.model === 'Custom' ? [] : [8, 4],
    }));
    refreshTrajectories();
    sim.autoFrame();
    renderCompareTable(res.entries);
  } catch (e) { handleError(e); }
}

function renderCompareTable(entries) {
  const base = entries[0]?.result;
  const rows = entries.map(e => {
    const r = e.result;
    const c = MODEL_COLORS[e.model] ?? '#fff';
    const d = base ? r.landingX - base.landingX : 0;
    return `<tr>
      <td class="l"><span class="sw" style="--c:${c}"></span> ${escapeHtml(e.label)}</td>
      <td>${fmt(r.initialSpeed)}</td><td>${fmt(r.flightTime)}</td><td>${fmt(r.maxHeight)}</td>
      <td>${fmt(r.landingX)}</td><td>${fmt(r.horizontalRange)}</td><td>${d >= 0 ? '+' : ''}${fmt(d)}</td>
      <td>${r.steps}</td><td class="l ${r.hitTarget ? 'good' : 'bad'}">${r.collisionStatus}</td>
      <td class="l muted">${escapeHtml(r.activeModels.join(' + '))}</td></tr>`;
  }).join('');
  $('#compareTable').innerHTML = `<thead><tr><th class="l">Mô hình</th><th>V0</th><th>T bay</th><th>H max</th>
    <th>X rơi</th><th>Tầm xa</th><th>Δx so với đầu</th><th>Số bước</th><th class="l">Trạng thái</th><th class="l">Lực tác dụng</th></tr></thead><tbody>${rows}</tbody>`;
  $('#compareCard').classList.remove('hidden');
}

// ------------------------------------------------------------------ results

function renderResult(r) {
  const stat = (k, v, unit = '') => `<div class="stat"><div class="k">${k}</div><div class="v">${v}${unit ? ` <span class="muted">${unit}</span>` : ''}</div></div>`;
  $('#stats').innerHTML = [
    stat('Góc', `${fmt(r.angle, 1)}° → ${fmt(r.effectiveAngle, 1)}°`),
    stat('Lực', fmt(r.power, 1)),
    stat('Vận tốc đầu V0', fmt(r.initialSpeed), 'u/s'),
    stat('Khoảng cách', fmt(r.distance, 2), 'u'),
    stat('Gió', fmt(r.wind, 1)),
    stat('Trọng lực (hiệu dụng)', fmt(r.effectiveGravity), 'u/s²'),
    stat('Thời gian bay', fmt(r.flightTime), 's'),
    stat('Độ cao cực đại', fmt(r.maxHeight), 'u'),
    stat('Điểm rơi X', fmt(r.landingX), 'u'),
    stat('Điểm rơi Y', fmt(r.landingY), 'u'),
    stat('Tầm xa ngang', fmt(r.horizontalRange), 'u'),
    stat('Độ lệch mục tiêu', (r.missDistance >= 0 ? '+' : '') + fmt(r.missDistance), 'u'),
    stat('Số bước mô phỏng', r.steps.toLocaleString()),
    stat('Δt / tích phân', `${r.deltaTime}s ${r.integrator.replace('SemiImplicitEuler', 'SI-Euler').replace('RungeKutta4', 'RK4').replace('ExplicitEuler', 'Euler')}`),
    stat('Vận tốc chạm', fmt(r.impactSpeed), 'u/s'),
    stat('Tầm xa giải tích', r.analyticRange != null ? fmt(r.analyticRange) : 'n/a', r.analyticRange != null ? 'u' : ''),
  ].join('');
  const b = $('#statusBadge');
  b.textContent = r.collisionStatus;
  b.className = 'badge ' + (r.status === 'HitTarget' ? 'hit' : r.status === 'HitGround' ? 'miss' : 'other');
  $('#modelsLine').textContent = `lực: ${r.activeModels.join(' + ')} · tính trong ${fmt(r.computeTimeMs, 2)} ms`;
}

// ------------------------------------------------------------------ history

function renderHistory() {
  $('#historyCount').textContent = history.length ? `(${history.length})` : '';
  if (!history.length) {
    $('#historyTable').innerHTML = '<tbody><tr><td class="l muted">Chưa có phát bắn nào — bấm Mô phỏng hoặc Bắn.</td></tr></tbody>';
    return;
  }
  const rows = history.slice().reverse().map(h => {
    const r = h.summary, q = h.request;
    return `<tr data-id="${h.id}">
      <td class="l"><input type="checkbox" class="hist-vis" ${h.visible ? 'checked' : ''} title="Hiện trên canvas"> <span class="sw" style="--c:${h.color}"></span> ${h.n}</td>
      <td class="l">${h.source}</td>
      <td>${fmt(q.angle, 1)}</td><td>${fmt(q.power, 1)}</td><td>${fmt(q.distance, 1)}</td><td>${fmt(q.wind, 1)}</td>
      <td>${fmt(r.flightTime)}</td><td>${fmt(r.maxHeight)}</td><td>${fmt(r.landingX)}</td><td>${fmt(r.landingY)}</td>
      <td>${fmt(r.horizontalRange)}</td><td>${r.steps}</td>
      <td class="l ${r.hitTarget ? 'good' : 'bad'}">${r.collisionStatus}</td>
      <td class="l"><button class="icon-btn" data-act="load" title="Nạp lại thông số">↺</button><button class="icon-btn danger" data-act="del" title="Xoá">✕</button></td>
    </tr>`;
  }).join('');
  $('#historyTable').innerHTML = `<thead><tr><th class="l">#</th><th class="l">Nguồn</th><th>Góc</th><th>Lực</th><th>K.cách</th><th>Gió</th>
    <th>T bay</th><th>H max</th><th>X rơi</th><th>Y rơi</th><th>Tầm xa</th><th>Số bước</th><th class="l">Va chạm</th><th></th></tr></thead><tbody>${rows}</tbody>`;
}

async function ensurePoints(h) {
  if (h.points) return;
  const r = await Api.run(h.request);
  h.points = r.trajectory;
}

function onHistoryClick(e) {
  const tr = e.target.closest('tr[data-id]');
  if (!tr) return;
  const h = history.find(x => x.id === +tr.dataset.id);
  if (!h) return;
  if (e.target.classList.contains('hist-vis')) {
    h.visible = e.target.checked;
    (h.visible ? ensurePoints(h) : Promise.resolve()).then(() => { refreshTrajectories(); saveHistory(); }).catch(handleError);
    return;
  }
  const act = e.target.dataset.act;
  if (act === 'del') {
    history = history.filter(x => x !== h);
    renderHistory(); refreshTrajectories(); saveHistory();
  } else if (act === 'load') {
    applyParams(requestToParams(h.request));
    toast(`Đã nạp thông số phát #${h.n}`);
  }
}

function requestToParams(q) {
  const p = {
    distance: q.distance, angle: q.angle, power: q.power, wind: q.wind, gravity: q.gravity,
    windEnabled: q.windEnabled, windMode: q.windMode,
    dragEnabled: q.dragType !== 'None', dragType: q.dragType !== 'None' ? q.dragType : params.dragType,
    dragCoefficient: q.dragCoefficient, airDensity: q.airDensity, crossSectionArea: q.crossSectionArea, mass: q.mass,
    integrator: q.integrator, deltaTime: q.deltaTime, maxTime: q.maxTime,
    barrelLength: q.barrelLength, targetWidth: q.targetWidth, targetHeight: q.targetHeight,
  };
  for (const [k, v] of Object.entries(q.calibration ?? {})) p['cal.' + k] = v;
  return p;
}

function saveHistory() {
  try {
    const slim = history.map(({ points, ...rest }) => rest); // trajectories are recomputed on demand
    localStorage.setItem(HISTORY_KEY, JSON.stringify(slim));
  } catch { /* storage full or blocked – history stays in memory */ }
}

function loadHistory() {
  try {
    const raw = localStorage.getItem(HISTORY_KEY);
    if (!raw) return;
    const list = JSON.parse(raw);
    if (!Array.isArray(list)) return;
    history = list.map(h => ({ ...h, points: null }));
    historyCounter = history.reduce((m, h) => Math.max(m, h.id), 0);
    Promise.all(history.filter(h => h.visible).map(ensurePoints)).then(refreshTrajectories).catch(() => { });
  } catch { history = []; }
}

function exportHistoryCsv() {
  if (!history.length) { toast('Lịch sử trống', true); return; }
  const cols = ['n', 'time', 'source', 'angle', 'effectiveAngle', 'power', 'distance', 'wind', 'windEnabled', 'windMode', 'gravity',
    'dragType', 'dragCoefficient', 'integrator', 'deltaTime', 'initialSpeed', 'flightTime', 'maxHeight', 'landingX', 'landingY',
    'horizontalRange', 'missDistance', 'steps', 'status',
    'velocityMultiplier', 'powerMultiplier', 'powerExponent', 'gravityMultiplier', 'windMultiplier', 'angleOffset', 'launchHeight'];
  const lines = [cols.join(',')];
  for (const h of history) {
    const q = h.request, r = h.summary, c = q.calibration ?? {};
    const row = [h.n, h.time, h.source, q.angle, r.effectiveAngle, q.power, q.distance, q.wind, q.windEnabled, q.windMode, q.gravity,
      q.dragType, q.dragCoefficient, q.integrator, q.deltaTime, r.initialSpeed, r.flightTime, r.maxHeight, r.landingX, r.landingY,
      r.horizontalRange, r.missDistance, r.steps, r.status,
      c.velocityMultiplier, c.powerMultiplier, c.powerExponent, c.gravityMultiplier, c.windMultiplier, c.angleOffset, c.launchHeight];
    lines.push(row.map(csvCell).join(','));
  }
  download(`shot-history-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.csv`, lines.join('\n'));
}

export function csvCell(v) {
  if (v == null) return '';
  const s = typeof v === 'number' ? (Number.isInteger(v) ? String(v) : v.toPrecision(10).replace(/\.?0+$/, '')) : String(v);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

export function download(name, text) {
  const blob = new Blob([text], { type: 'text/csv;charset=utf-8' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

// ------------------------------------------------------------------ misc

function setApiStatus(ok, msg) {
  const el = $('#apiStatus');
  el.classList.toggle('ok', ok);
  el.classList.toggle('err', !ok);
  $('.txt', el).textContent = ok ? 'máy chủ vật lý: trực tuyến' : 'lỗi máy chủ';
  el.title = msg || '';
}

function handleError(e) {
  console.error(e);
  if (e.status === 400) toast('Dữ liệu không hợp lệ: ' + e.message, true);
  else { setApiStatus(false, e.message); toast('Lỗi máy chủ: ' + e.message, true); }
}

function switchTab(name) {
  for (const t of $$('.tab')) t.classList.toggle('active', t.dataset.tab === name);
  for (const p of $$('.tab-page')) p.classList.toggle('hidden', p.id !== 'page-' + name);
  document.body.classList.toggle('tab-gunny', name === 'gunny');
  document.dispatchEvent(new CustomEvent('research-tab', { detail: name }));
}

// ------------------------------------------------------------------ boot

async function boot() {
  buildSliders();

  // Physics defaults come from the backend so the UI and the engine never disagree.
  try {
    const d = await Api.defaults();
    Object.assign(DEFAULTS, requestToParams(d), { dragEnabled: d.dragType !== 'None', dragType: d.dragType !== 'None' ? d.dragType : 'Quadratic' });
    setApiStatus(true);
  } catch (e) {
    setApiStatus(false, e.message);
    toast('Không kết nối được máy chủ vật lý – `dotnet run` đã chạy chưa?', true);
  }
  Object.assign(params, DEFAULTS);
  syncAll();
  updateScene();

  // selects / checkboxes
  $('#windEnabled').addEventListener('change', e => { params.windEnabled = e.target.checked; updateDisabledStates(); onParamsChanged('windEnabled'); });
  $('#windMode').addEventListener('change', e => { params.windMode = e.target.value; onParamsChanged('windMode'); });
  $('#dragEnabled').addEventListener('change', e => { params.dragEnabled = e.target.checked; updateDisabledStates(); onParamsChanged('dragEnabled'); });
  $('#dragType').addEventListener('change', e => { params.dragType = e.target.value; onParamsChanged('dragType'); });
  $('#integrator').addEventListener('change', e => { params.integrator = e.target.value; onParamsChanged('integrator'); });
  $('#livePreview').addEventListener('change', () => onParamsChanged('livePreview'));

  // buttons
  $('#btnSimulate').addEventListener('click', onSimulate);
  $('#btnFire').addEventListener('click', onFire);
  $('#btnPause').addEventListener('click', () => animator.pause());
  $('#btnResume').addEventListener('click', () => animator.resume());
  $('#btnReset').addEventListener('click', onReset);
  $('#btnClear').addEventListener('click', onClearTrajectory);
  $('#btnCompare').addEventListener('click', () => runCompare(['Basic', 'Wind', 'Drag', 'Custom']));
  $('#btnCloseCompare').addEventListener('click', () => { $('#compareCard').classList.add('hidden'); compareEntries = []; refreshTrajectories(); });

  $('#btnExportCsv').addEventListener('click', exportHistoryCsv);
  $('#btnClearHistory').addEventListener('click', () => {
    if (history.length && !confirm(`Xoá toàn bộ ${history.length} phát bắn trong lịch sử?`)) return;
    history = []; historyCounter = 0; renderHistory(); refreshTrajectories(); saveHistory();
  });
  $('#btnHistoryShowAll').addEventListener('click', async () => {
    for (const h of history) h.visible = true;
    try { await Promise.all(history.map(ensurePoints)); } catch (e) { handleError(e); }
    renderHistory(); refreshTrajectories(); saveHistory();
  });
  $('#btnHistoryHideAll').addEventListener('click', () => { for (const h of history) h.visible = false; renderHistory(); refreshTrajectories(); saveHistory(); });
  $('#historyTable').addEventListener('click', onHistoryClick);

  // canvas toolbar
  $('#btnFitView').addEventListener('click', () => sim.fitView());
  $('#btnResetView').addEventListener('click', () => sim.resetView());
  $('#chkGrid').addEventListener('change', e => sim.setOptions({ grid: e.target.checked }));
  $('#chkLabels').addEventListener('change', e => sim.setOptions({ labels: e.target.checked }));
  $('#chkAim').addEventListener('change', e => sim.setOptions({ aim: e.target.checked }));

  for (const t of $$('.tab')) t.addEventListener('click', () => switchTab(t.dataset.tab));

  // keyboard: Space = fire, P = pause/resume, Enter = simulate (not while typing)
  document.addEventListener('keydown', (e) => {
    if (e.target.matches('input[type=number], input[type=text], textarea, select')) return;
    if (e.code === 'Space') { e.preventDefault(); onFire(); }
    else if (e.key === 'p' || e.key === 'P') animator.state === 'playing' ? animator.pause() : animator.resume();
    else if (e.key === 'Enter') onSimulate();
  });

  loadHistory();
  renderHistory();
  $('#stats').innerHTML = '<div class="muted">Bấm <b>Mô phỏng</b> (Enter) hoặc <b>Bắn</b> (Space).</div>';

  initReverse({
    buildRequest, applyParams, getParams: () => ({ ...params }), runCompare, toast, handleError, csvCell, download,
    setObservations: (obs) => sim.setScene({ observations: obs }),
    setExtraLayers: (layers) => { extraLayers = layers; refreshTrajectories(); sim.autoFrame(); },
    showEvalCard: (show) => $('#evalCard').classList.toggle('hidden', !show),
  });

  initGunny({
    getParams: () => ({ ...params }), toast, handleError, download,
    setObservations: (obs) => sim.setScene({ observations: obs }),
    setExtraLayers: (layers) => { extraLayers = layers; refreshTrajectories(); sim.autoFrame(); },
    setTargetDistance: (d) => sim.setScene({ distance: d }),
    clearPreview: () => { preview = null; $('#livePreview').checked = false; refreshTrajectories(); },
  });

  previewRunner.schedule();
}

boot();
