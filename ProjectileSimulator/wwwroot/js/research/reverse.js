// =============================================================================
// reverse.js – Reverse Engineering Mode:
//   * experiment table (user-entered observations from the real game)
//   * error evaluation (predicted vs actual, MAE, RMSE)
//   * parameter optimisation (grid search / Levenberg–Marquardt on the backend)
// No experimental data is ever invented here; the "synthetic" generator is explicitly
// labelled and only meant to test that the optimizer recovers known parameters.
// =============================================================================
import { Api } from './simulator.js';

const $ = (s, r = document) => r.querySelector(s);
const EXP_KEY = 'gunnylab.experiments.v1';
const PARAM_KEY = 'gunnylab.optparams.v1';
const COLUMNS = [
  { key: 'label', title: 'Nhãn', type: 'text' },
  { key: 'distance', title: 'K.cách', step: 0.1 },
  { key: 'angle', title: 'Góc', step: 1 },
  { key: 'power', title: 'Lực', step: 1 },
  { key: 'wind', title: 'Gió', step: 0.5 },
  { key: 'observedLandingX', title: 'X đo', step: 0.01 },
  { key: 'observedLandingY', title: 'Y đo', step: 0.01 },
  { key: 'observedFlightTime', title: 't đo (s)', step: 0.01, optional: true },
];
const PARAM_LABELS = {
  VelocityMultiplier: 'Vận tốc ×', PowerMultiplier: 'Lực ×', PowerExponent: 'Số mũ lực',
  GravityMultiplier: 'Trọng lực ×', WindMultiplier: 'Gió ×', DragCoefficient: 'Hệ số cản',
  AngleOffset: 'Lệch góc', LaunchHeight: 'Độ cao bắn',
};
/** Optimizer parameter name → UI param key. */
const PARAM_KEYS = {
  VelocityMultiplier: 'cal.velocityMultiplier', PowerMultiplier: 'cal.powerMultiplier', PowerExponent: 'cal.powerExponent',
  GravityMultiplier: 'cal.gravityMultiplier', WindMultiplier: 'cal.windMultiplier', DragCoefficient: 'dragCoefficient',
  AngleOffset: 'cal.angleOffset', LaunchHeight: 'cal.launchHeight',
};

let ctx;
let experiments = [];
let paramRanges = [];
let lastEval = null;
let lastOpt = null;

export function initReverse(context) {
  ctx = context;
  experiments = load(EXP_KEY, []);
  renderExperiments();
  loadParamRanges();

  $('#btnExpAdd').addEventListener('click', () => addRow({}));
  $('#btnExpAddCurrent').addEventListener('click', () => {
    const p = ctx.getParams();
    addRow({ distance: p.distance, angle: p.angle, power: p.power, wind: p.wind });
    ctx.toast('Đã thêm dòng – hãy nhập vị trí rơi bạn quan sát được trong game');
  });
  $('#btnExpClear').addEventListener('click', () => {
    if (experiments.length && !confirm(`Xoá toàn bộ ${experiments.length} dòng dữ liệu?`)) return;
    experiments = []; saveExperiments(); renderExperiments(); clearEval();
  });
  $('#btnExpExport').addEventListener('click', exportCsv);
  $('#btnExpImport').addEventListener('click', () => $('#importDialog').showModal());
  $('#btnImportFile').addEventListener('click', () => $('#expFile').click());
  $('#expFile').addEventListener('change', async (e) => {
    const f = e.target.files[0];
    if (f) $('#importText').value = await f.text();
    e.target.value = '';
  });
  $('#importDialog').addEventListener('close', () => {
    if ($('#importDialog').returnValue !== 'ok') return;
    try {
      const rows = parseCsv($('#importText').value);
      if (!rows.length) throw new Error('không tìm thấy dòng dữ liệu');
      if ($('#importReplace').checked) experiments = [];
      experiments.push(...rows);
      saveExperiments(); renderExperiments();
      ctx.toast(`Đã nhập ${rows.length} dòng`);
    } catch (err) { ctx.toast('Nhập thất bại: ' + err.message, true); }
  });
  $('#btnExpSynthetic').addEventListener('click', () => $('#synthDialog').showModal());
  $('#synthDialog').addEventListener('close', () => { if ($('#synthDialog').returnValue === 'ok') generateSynthetic(); });

  $('#expTable').addEventListener('change', onExpEdit);
  $('#expTable').addEventListener('click', onExpClick);
  $('#btnEvaluate').addEventListener('click', evaluate);
  $('#btnOptimize').addEventListener('click', optimize);
  $('#paramTable').addEventListener('change', onParamEdit);
  $('#showObservations').addEventListener('change', updateObservationLayer);
  $('#btnCompareModels').addEventListener('click', () => {
    const models = [...document.querySelectorAll('#modelChecks input:checked')].map(i => i.value);
    ctx.runCompare(models);
  });
  updateObservationLayer();
}

// ------------------------------------------------------------------ persistence

function load(key, fallback) {
  try { const v = JSON.parse(localStorage.getItem(key)); return v ?? fallback; } catch { return fallback; }
}
function save(key, v) { try { localStorage.setItem(key, JSON.stringify(v)); } catch { /* ignore */ } }
function saveExperiments() { save(EXP_KEY, experiments); $('#expCount').textContent = experiments.length ? `(${experiments.length})` : ''; }

// ------------------------------------------------------------------ experiment table

function addRow(partial) {
  experiments.push({
    label: '', distance: 5, angle: 45, power: 50, wind: 0, observedLandingX: 0, observedLandingY: 0, observedFlightTime: null,
    ...partial,
  });
  saveExperiments(); renderExperiments();
  const inputs = $('#expTable').querySelectorAll('tbody tr:last-child input');
  inputs[5]?.focus();
}

function renderExperiments() {
  $('#expCount').textContent = experiments.length ? `(${experiments.length})` : '';
  const head = `<thead><tr><th>#</th>${COLUMNS.map(c => `<th class="${c.type === 'text' ? 'l' : ''}">${c.title}</th>`).join('')}<th></th></tr></thead>`;
  if (!experiments.length) {
    $('#expTable').innerHTML = head + `<tbody><tr><td colspan="${COLUMNS.length + 2}" class="l muted">Chưa có dữ liệu. Hãy thêm các phát bắn bạn đo được trong game.</td></tr></tbody>`;
    updateObservationLayer();
    return;
  }
  const body = experiments.map((e, i) => `<tr data-i="${i}"><td>${i + 1}</td>${COLUMNS.map(c => {
    const v = e[c.key];
    if (c.type === 'text') return `<td class="l"><input type="text" data-k="${c.key}" value="${esc(v ?? '')}"></td>`;
    return `<td><input type="number" data-k="${c.key}" step="${c.step}" value="${v == null ? '' : v}" ${c.optional ? 'placeholder="—"' : ''}></td>`;
  }).join('')}<td class="l"><button class="icon-btn" data-act="load" title="Nạp phát bắn này vào trình mô phỏng">▶</button><button class="icon-btn danger" data-act="del" title="Xoá dòng">✕</button></td></tr>`).join('');
  $('#expTable').innerHTML = head + `<tbody>${body}</tbody>`;
  updateObservationLayer();
}

function onExpEdit(e) {
  const tr = e.target.closest('tr[data-i]');
  if (!tr) return;
  const row = experiments[+tr.dataset.i];
  const k = e.target.dataset.k;
  const col = COLUMNS.find(c => c.key === k);
  if (col.type === 'text') row[k] = e.target.value;
  else {
    const v = parseFloat(e.target.value);
    if (e.target.value.trim() === '' && col.optional) row[k] = null;
    else if (isFinite(v)) row[k] = v;
    else { e.target.value = row[k] ?? ''; return; }
  }
  saveExperiments();
  updateObservationLayer();
}

function onExpClick(e) {
  const tr = e.target.closest('tr[data-i]');
  const act = e.target.dataset.act;
  if (!tr || !act) return;
  const i = +tr.dataset.i;
  if (act === 'del') {
    experiments.splice(i, 1);
    saveExperiments(); renderExperiments(); clearEval();
  } else if (act === 'load') {
    const x = experiments[i];
    ctx.applyParams({ distance: x.distance, angle: x.angle, power: x.power, wind: x.wind });
    ctx.toast(`Đã nạp thí nghiệm #${i + 1}`);
  }
}

function updateObservationLayer() {
  if (!$('#showObservations').checked) { ctx.setObservations([]); return; }
  const predicted = lastEval?.predictions;
  ctx.setObservations(experiments.map((e, i) => {
    const p = predicted && predicted.length === experiments.length ? predicted[i] : null;
    return { x: e.observedLandingX, y: e.observedLandingY, label: e.label || `#${i + 1}`, px: p?.predictedLandingX, py: p?.predictedLandingY };
  }));
}

// ------------------------------------------------------------------ CSV

function exportCsv() {
  if (!experiments.length) { ctx.toast('Không có dữ liệu để xuất', true); return; }
  const lines = [COLUMNS.map(c => c.key).join(',')];
  for (const e of experiments) lines.push(COLUMNS.map(c => ctx.csvCell(e[c.key])).join(','));
  ctx.download('experiments.csv', lines.join('\n'));
}

function parseCsv(text) {
  const rawLines = text.split(/\r?\n/).map(l => l.trim()).filter(l => l && !l.startsWith('#'));
  if (!rawLines.length) return [];
  const sep = [',', ';', '\t'].map(s => [s, rawLines[0].split(s).length]).sort((a, b) => b[1] - a[1])[0][0];
  const split = (l) => {
    const out = []; let cur = '', q = false;
    for (let i = 0; i < l.length; i++) {
      const ch = l[i];
      if (q) { if (ch === '"' && l[i + 1] === '"') { cur += '"'; i++; } else if (ch === '"') q = false; else cur += ch; }
      else if (ch === '"') q = true;
      else if (ch === sep) { out.push(cur.trim()); cur = ''; }
      else cur += ch;
    }
    out.push(cur.trim());
    return out;
  };
  const aliases = {
    label: 'label', name: 'label', distance: 'distance', dist: 'distance', angle: 'angle', power: 'power', wind: 'wind',
    observedlandingx: 'observedLandingX', obsx: 'observedLandingX', landingx: 'observedLandingX', x: 'observedLandingX',
    observedlandingy: 'observedLandingY', obsy: 'observedLandingY', landingy: 'observedLandingY', y: 'observedLandingY',
    observedflighttime: 'observedFlightTime', obst: 'observedFlightTime', flighttime: 'observedFlightTime', t: 'observedFlightTime',
  };
  const header = split(rawLines[0]).map(h => aliases[h.toLowerCase().replace(/[^a-z]/g, '')]);
  const hasHeader = header.some(Boolean);
  const keys = hasHeader ? header : COLUMNS.slice(1).map(c => c.key); // headerless: numeric columns in default order
  const rows = [];
  for (const line of rawLines.slice(hasHeader ? 1 : 0)) {
    const cells = split(line);
    const row = { label: '', distance: 5, angle: 0, power: 0, wind: 0, observedLandingX: 0, observedLandingY: 0, observedFlightTime: null };
    keys.forEach((k, i) => {
      if (!k || cells[i] == null) return;
      if (k === 'label') { row.label = cells[i]; return; }
      if (cells[i] === '') { if (k === 'observedFlightTime') row[k] = null; return; }
      const v = parseFloat(cells[i].replace(',', '.'));
      if (!isFinite(v)) throw new Error(`số không hợp lệ "${cells[i]}" ở dòng: ${line}`);
      row[k] = v;
    });
    rows.push(row);
  }
  return rows;
}

// ------------------------------------------------------------------ synthetic data (testing only)

async function generateSynthetic() {
  const n = Math.max(1, Math.min(50, parseInt($('#synthCount').value) || 8));
  const sigma = Math.max(0, parseFloat($('#synthNoise').value) || 0);
  const withTime = $('#synthTime').checked;
  const shots = Array.from({ length: n }, () => ({
    angle: Math.round(20 + Math.random() * 60),
    power: Math.round(30 + Math.random() * 65),
    wind: Math.round((Math.random() * 20 - 10) * 2) / 2,
  }));
  try {
    const res = await Api.compare({
      base: ctx.buildRequest(),
      models: [],
      scenarios: shots.map((s, i) => ({ label: `S${i}`, request: ctx.buildRequest({ ...s, targetCollisionEnabled: false }) })),
    });
    const gauss = () => Math.sqrt(-2 * Math.log(1 - Math.random())) * Math.cos(2 * Math.PI * Math.random());
    const start = experiments.length;
    res.entries.forEach((e, i) => {
      const r = e.result;
      experiments.push({
        label: `SYNTHETIC-${start + i + 1}`,
        distance: ctx.getParams().distance,
        angle: shots[i].angle, power: shots[i].power, wind: shots[i].wind,
        observedLandingX: round(r.landingX + sigma * gauss(), 4),
        observedLandingY: 0,
        observedFlightTime: withTime ? round(r.flightTime, 4) : null,
      });
    });
    saveExperiments(); renderExperiments();
    ctx.toast(`Đã sinh ${n} dòng TỔNG HỢP từ mô hình hiện tại (không phải dữ liệu game)`);
  } catch (e) { ctx.handleError(e); }
}

const round = (v, d) => Math.round(v * 10 ** d) / 10 ** d;

// ------------------------------------------------------------------ evaluation

function timeWeight() { return Math.max(0, parseFloat($('#timeWeight').value) || 0); }

async function evaluate() {
  if (!experiments.length) { ctx.toast('Hãy thêm ít nhất một thí nghiệm', true); return; }
  const btn = $('#btnEvaluate');
  btn.disabled = true;
  try {
    const base = ctx.buildRequest();
    lastEval = await Api.evaluate({ base, experiments, flightTimeWeight: timeWeight() });
    renderEval(lastEval);
    updateObservationLayer();
    await drawExperimentTrajectories(base);
  } catch (e) { ctx.handleError(e); }
  finally { btn.disabled = false; }
}

/** Faint predicted trajectories for each experiment (max 50). */
async function drawExperimentTrajectories(base) {
  const list = experiments.slice(0, 50);
  const res = await Api.compare({
    base, models: [],
    scenarios: list.map((e, i) => ({
      label: e.label || `#${i + 1}`,
      request: { ...base, distance: e.distance, angle: e.angle, power: e.power, wind: e.wind, targetCollisionEnabled: false,
        groundLevel: Math.min(0, e.observedLandingY, base.calibration.launchHeight), maxTrajectoryPoints: 800 },
    })),
  });
  ctx.setExtraLayers(res.entries.map(en => ({
    points: en.result.trajectory, result: en.result, color: '#e879f9', width: 1.2, alpha: 0.45, compact: true,
  })));
}

function clearEval() {
  lastEval = null;
  ctx.showEvalCard(false);
  ctx.setExtraLayers([]);
  updateObservationLayer();
}

function renderEval(ev) {
  ctx.showEvalCard(true);
  const m = ev.metrics;
  $('#evalMetrics').innerHTML = `MAE <b>${f(m.meanAbsoluteError, 4)}</b> · RMSE <b>${f(m.rootMeanSquareError, 4)}</b> · lớn nhất <b>${f(m.maxError, 4)}</b> · độ chệch X <b>${f(m.meanSignedErrorX, 4)}</b>${m.flightTimeRmse != null ? ` · t-RMSE <b>${f(m.flightTimeRmse, 4)} s</b>` : ''}`;
  const rows = ev.predictions.map(p => `<tr>
    <td>${p.index + 1}</td><td class="l">${esc(p.label ?? '')}</td>
    <td>${f(p.angle, 1)}</td><td>${f(p.power, 1)}</td><td>${f(p.wind, 1)}</td>
    <td>${f(p.predictedLandingX)}</td><td>${f(p.predictedLandingY)}</td>
    <td>${f(p.actualLandingX)}</td><td>${f(p.actualLandingY)}</td>
    <td class="${Math.abs(p.errorX) < 0.05 ? 'good' : ''}">${p.errorX >= 0 ? '+' : ''}${f(p.errorX)}</td>
    <td class="${p.errorDistance < 0.05 ? 'good' : p.errorDistance > 0.5 ? 'bad' : ''}">${f(p.errorDistance)}</td>
    <td>${f(p.flightTime)}</td><td>${p.actualFlightTime != null ? f(p.actualFlightTime) : '—'}</td>
    <td>${p.actualFlightTime != null ? (p.errorTime >= 0 ? '+' : '') + f(p.errorTime) : '—'}</td>
    <td class="l">${p.reachedObservedHeight ? '' : '<span class="bad" title="Quỹ đạo không lên tới độ cao quan sát – dùng điểm gần nhất">không tới Y</span>'}</td>
  </tr>`).join('');
  $('#evalTable').innerHTML = `<thead><tr><th>#</th><th class="l">Nhãn</th><th>Góc</th><th>Lực</th><th>Gió</th>
    <th>X dự đoán</th><th>Y dự đoán</th><th>X thực</th><th>Y thực</th><th>Sai số X</th><th>Sai số K.cách</th>
    <th>t dự đoán</th><th>t thực</th><th>Sai số t</th><th class="l"></th></tr></thead><tbody>${rows}</tbody>`;
}

// ------------------------------------------------------------------ optimisation

async function loadParamRanges() {
  const saved = load(PARAM_KEY, null);
  try {
    const defaults = await Api.parameters();
    paramRanges = defaults.map(d => ({ ...d, ...(saved?.find(s => s.name === d.name) ?? {}) }));
  } catch {
    paramRanges = saved ?? [];
  }
  renderParamTable();
}

function renderParamTable() {
  const rows = paramRanges.map((p, i) => `<tr data-i="${i}">
    <td class="l"><label class="check"><input type="checkbox" data-k="enabled" ${p.enabled ? 'checked' : ''}> ${PARAM_LABELS[p.name] ?? p.name}</label></td>
    <td><input type="number" data-k="min" step="any" value="${p.min}"></td>
    <td><input type="number" data-k="max" step="any" value="${p.max}"></td>
    <td><input type="number" data-k="steps" min="2" max="1000" step="1" value="${p.steps}"></td>
    <td class="muted">${f(ctx.getParams()[PARAM_KEYS[p.name]], 4)}</td></tr>`).join('');
  $('#paramTable').innerHTML = `<thead><tr><th class="l">Tối ưu</th><th>Min</th><th>Max</th><th>Số điểm</th><th>Hiện tại</th></tr></thead><tbody>${rows}</tbody>`;
}

function onParamEdit(e) {
  const tr = e.target.closest('tr[data-i]');
  if (!tr) return;
  const p = paramRanges[+tr.dataset.i];
  const k = e.target.dataset.k;
  if (k === 'enabled') p.enabled = e.target.checked;
  else {
    const v = parseFloat(e.target.value);
    if (!isFinite(v)) { e.target.value = p[k]; return; }
    p[k] = k === 'steps' ? Math.max(2, Math.round(v)) : v;
  }
  save(PARAM_KEY, paramRanges);
}

async function optimize() {
  if (!experiments.length) { ctx.toast('Hãy thêm dữ liệu thực nghiệm trước', true); return; }
  if (!paramRanges.some(p => p.enabled)) { ctx.toast('Hãy chọn ít nhất một tham số để tối ưu', true); return; }
  const btn = $('#btnOptimize');
  btn.disabled = true;
  const old = btn.textContent;
  btn.textContent = 'Đang tối ưu…';
  $('#optResult').innerHTML = '<div class="box muted">Đang chạy trên máy chủ…</div>';
  try {
    const req = {
      base: ctx.buildRequest(),
      experiments,
      method: $('#optMethod').value,
      parameters: paramRanges,
      maxIterations: parseInt($('#optIter').value) || 100,
      maxGridEvaluations: parseInt($('#optGrid').value) || 20000,
      flightTimeWeight: timeWeight(),
    };
    lastOpt = await Api.optimize(req);
    renderOptResult(lastOpt);
    lastEval = { predictions: lastOpt.predictions, metrics: lastOpt.bestMetrics };
    renderEval(lastEval);
    updateObservationLayer();
  } catch (e) {
    $('#optResult').innerHTML = '';
    ctx.handleError(e);
  } finally {
    btn.disabled = false;
    btn.textContent = old;
  }
}

function renderOptResult(r) {
  const before = r.initialMetrics, after = r.bestMetrics;
  const gain = before.rootMeanSquareError > 0 ? (1 - after.rootMeanSquareError / before.rootMeanSquareError) * 100 : 0;
  const rows = Object.entries(r.bestParameters).map(([k, v]) =>
    `<tr><td class="l">${PARAM_LABELS[k] ?? k}</td><td>${f(r.initialParameters[k], 4)}</td><td>→</td><td><b>${f(v, 4)}</b></td></tr>`).join('');
  $('#optResult').innerHTML = `
    <div class="box">
      <div>${r.method} · ${r.evaluations.toLocaleString()} lượt tính · ${r.iterations} vòng LM · ${f(r.elapsedMs, 0)} ms</div>
      <div>RMSE ${f(before.rootMeanSquareError, 4)} → <b>${f(after.rootMeanSquareError, 4)}</b> <span class="improve">(${gain >= 0 ? '−' : '+'}${Math.abs(gain).toFixed(1)}%)</span></div>
      <div>MAE  ${f(before.meanAbsoluteError, 4)} → <b>${f(after.meanAbsoluteError, 4)}</b></div>
      ${after.flightTimeRmse != null ? `<div>t-RMSE ${f(before.flightTimeRmse, 4)} → <b>${f(after.flightTimeRmse, 4)}</b> s</div>` : ''}
      <table class="data">${rows}</table>
      <canvas id="convChart" width="600" height="120" title="Hội tụ (√mục tiêu/N theo từng giai đoạn)"></canvas>
      ${r.notes.length ? `<ul>${r.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul>` : ''}
    </div>
    <button class="btn primary" id="btnApplyOpt" style="width:100%;margin-top:6px">Áp dụng tham số tốt nhất</button>`;
  drawConvergence($('#convChart'), r.rmseHistory);
  $('#btnApplyOpt').addEventListener('click', () => {
    const patch = {};
    for (const [k, v] of Object.entries(r.bestParameters)) patch[PARAM_KEYS[k]] = v;
    ctx.applyParams(patch);
    renderParamTable();
    ctx.toast('Đã áp dụng tham số tốt nhất cho Mô hình D');
    evaluate();
  });
}

function drawConvergence(c, hist) {
  if (!c || !hist?.length) return;
  const g = c.getContext('2d');
  const w = c.width, h = c.height, pad = 14;
  g.clearRect(0, 0, w, h);
  const max = Math.max(...hist), min = Math.min(...hist);
  const ly = (v) => {  // log scale makes LM convergence readable
    const a = Math.log10(Math.max(v, 1e-12)), lo = Math.log10(Math.max(min, 1e-12)), hi = Math.log10(Math.max(max, 1e-12));
    return hi - lo < 1e-9 ? h / 2 : h - pad - (a - lo) / (hi - lo) * (h - 2 * pad);
  };
  g.strokeStyle = '#39d0ff'; g.lineWidth = 2; g.beginPath();
  hist.forEach((v, i) => {
    const x = pad + (hist.length === 1 ? 0 : i / (hist.length - 1)) * (w - 2 * pad);
    i ? g.lineTo(x, ly(v)) : g.moveTo(x, ly(v));
  });
  g.stroke();
  g.fillStyle = '#7d8ba3'; g.font = '18px monospace';
  g.fillText(`đầu ${hist[0].toPrecision(3)}`, pad, 18);
  g.textAlign = 'right';
  g.fillText(`cuối ${hist[hist.length - 1].toPrecision(3)}`, w - pad, h - 4);
}

// ------------------------------------------------------------------ utils

function f(v, d = 3) { return v == null || !isFinite(v) ? '—' : Number(v).toFixed(d); }
function esc(s) { return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
