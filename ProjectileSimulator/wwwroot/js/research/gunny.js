// =============================================================================
// gunny.js – tab "Mô hình Gunny (V3)" trong trang Nghiên cứu công thức.
//   * Quy đổi cự ly simulator ↔ khoảng Gunny
//   * Danh sách mô hình (tên, mô tả, nguồn, điều kiện áp dụng, tham số)
//   * Dữ liệu bắn thực tế (lưu trên máy chủ, CSV)
//   * Tối ưu / đánh giá, tìm góc và lực, so sánh đường đạn
// Mọi tính toán do API /api/gunny/* thực hiện.
// =============================================================================

const $ = (s, r = document) => r.querySelector(s);
const MODEL_COLORS = { physics: '#4fc3f7', superHigh: '#f06292', angle65: '#81c784', lowAngle: '#ffb74d', custom: '#ba68c8' };
const STATUS = { Ready: ['ready', 'Sẵn sàng'], Fitted: ['fitted', 'Đã khớp dữ liệu'], InsufficientData: ['none', 'Chưa xác định'] };

let ctx;
let settings = null;
let models = [];
let experiments = [];

async function api(method, url, body, raw = false) {
  const res = await fetch(url, {
    method,
    headers: body == null ? undefined : { 'Content-Type': raw ? 'text/plain' : 'application/json' },
    body: body == null ? undefined : (raw ? body : JSON.stringify(body)),
  });
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`;
    try {
      const p = await res.json();
      const parts = [p.detail, ...Object.entries(p.errors ?? {}).map(([k, v]) => `${k}: ${[].concat(v).join(', ')}`)].filter(Boolean);
      if (parts.length) msg = parts.join(' · ');
    } catch { /* không phải JSON */ }
    const e = new Error(msg); e.status = 400; throw e;
  }
  if (res.status === 204) return null;
  const type = res.headers.get('content-type') ?? '';
  return type.includes('json') ? res.json() : res.text();
}

const f = (v, d = 3) => (v == null || !isFinite(v) ? '—' : Number(v).toFixed(d));
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const sw = (id) => `<span class="sw" style="--c:${MODEL_COLORS[id] ?? '#fff'}"></span>`;

export function initGunny(context) {
  ctx = context;
  bindCalibration();
  bindExperiments();
  $('#gModelsSave').addEventListener('click', saveModelParams);
  $('#gSettingsReset').addEventListener('click', async () => {
    if (!confirm('Khôi phục toàn bộ tham số mô hình và quy đổi cự ly về mặc định? (Dữ liệu bắn thực tế không bị xoá.)')) return;
    try { settings = await api('POST', '/api/gunny/settings/reset'); await refreshAll(); ctx.toast('Đã khôi phục mặc định'); } catch (e) { ctx.handleError(e); }
  });
  $('#gOptimize').addEventListener('click', optimize);
  $('#gAim').addEventListener('click', aim);
  $('#gCompare').addEventListener('click', compare);
  for (const b of document.querySelectorAll('[data-hide]')) b.addEventListener('click', () => $('#' + b.dataset.hide).classList.add('hidden'));
  document.addEventListener('research-tab', (e) => { if (e.detail === 'gunny') { refreshAll(); updateConversion(); } });
  refreshAll();
}

async function refreshAll() {
  try {
    [settings, models, experiments] = await Promise.all([
      api('GET', '/api/gunny/settings'), api('GET', '/api/gunny/models'), api('GET', '/api/gunny/experiments'),
    ]);
    fillCalibration();
    renderModels();
    renderModelChecks();
    renderExperiments();
  } catch (e) { ctx.handleError(e); }
}

// ------------------------------------------------------------------ quy đổi cự ly

function bindCalibration() {
  $('#gCalSave').addEventListener('click', saveCalibration);
  $('#gCalMode').addEventListener('change', () => { $('#gCalSpd').disabled = $('#gCalMode').value !== 'Manual'; });
  for (const id of ['gConvSim', 'gCalSpd', 'gCalDiv', 'gCalWidth']) $('#' + id).addEventListener('input', updateConversion);
}

function fillCalibration() {
  const c = settings.calibration;
  $('#gCalDiv').value = c.screenDivisions;
  $('#gCalWidth').value = c.referenceScreenWidthPx;
  $('#gCalMode').value = c.mode;
  $('#gCalSpd').value = +c.simUnitsPerDivision.toFixed(6);
  $('#gCalSpd').disabled = c.mode !== 'Manual';
  $('#gCalRA').value = c.referenceAngle;
  $('#gCalRP').value = c.referencePower;
  $('#gCalRD').value = c.referenceDistance;
  $('#gCalWind').value = c.windScale;
  $('#gWeapon').value = settings.weaponFilter ?? '';
  updateConversion();
}

function updateConversion() {
  if (!settings) return;
  const spd = parseFloat($('#gCalSpd').value) || settings.calibration.simUnitsPerDivision;
  const div = parseInt($('#gCalDiv').value) || settings.calibration.screenDivisions;
  const width = parseFloat($('#gCalWidth').value) || settings.calibration.referenceScreenWidthPx;
  const factor = 1 / spd, ppd = width / div;
  const sim = parseFloat($('#gConvSim').value) || 0;
  const cur = ctx.getParams().distance;
  $('#gCalInfo').innerHTML =
    `Hệ số quy đổi = <b>${f(factor, 4)}</b> khoảng Gunny / 1 u<br>1 khoảng Gunny = <b>${f(spd, 4)}</b> u = <b>${f(ppd, 1)}</b> px` +
    `<br>${settings.calibration.mode === 'ReferenceShot' ? `Đang neo: vật lý bắn ${settings.calibration.referenceAngle}°/lực ${settings.calibration.referencePower} rơi tại ${settings.calibration.referenceDistance} khoảng` : 'Đang dùng giá trị nhập tay'}`;
  $('#gConvOut').innerHTML =
    `${f(sim, 2)} u → <b>${f(sim * factor, 3)}</b> khoảng Gunny (${f(sim * factor * ppd, 0)} px)<br>` +
    `Khoảng cách hiện tại ở tab Mô phỏng: ${f(cur, 2)} u = <b>${f(cur * factor, 3)}</b> khoảng Gunny`;
}

async function saveCalibration() {
  const s = structuredClone(settings);
  const c = s.calibration;
  c.screenDivisions = parseInt($('#gCalDiv').value);
  c.referenceScreenWidthPx = parseFloat($('#gCalWidth').value);
  c.mode = $('#gCalMode').value;
  c.simUnitsPerDivision = parseFloat($('#gCalSpd').value);
  c.referenceAngle = parseFloat($('#gCalRA').value);
  c.referencePower = parseFloat($('#gCalRP').value);
  c.referenceDistance = parseFloat($('#gCalRD').value);
  c.windScale = parseFloat($('#gCalWind').value);
  try {
    settings = await api('PUT', '/api/gunny/settings', s);
    fillCalibration();
    models = await api('GET', '/api/gunny/models');
    renderModels();
    ctx.toast('Đã lưu quy đổi cự ly');
  } catch (e) { ctx.handleError(e); }
}

// ------------------------------------------------------------------ mô hình

/** Tên tham số trong API → vị trí trong settings. */
const PARAM_PATHS = {
  physics: {
    velocityMultiplier: ['physics', 'base', 'calibration', 'velocityMultiplier'], gravityMultiplier: ['physics', 'base', 'calibration', 'gravityMultiplier'],
    windMultiplier: ['physics', 'base', 'calibration', 'windMultiplier'], powerToVelocity: ['physics', 'base', 'calibration', 'powerToVelocity'],
    gravity: ['physics', 'base', 'gravity'], preferredAngle: ['physics', 'preferredAngle'],
  },
  superHigh: 'superHigh', angle65: 'angle65', lowAngle: 'lowAngle', custom: 'custom',
};

function setPath(obj, modelId, name, value) {
  const p = PARAM_PATHS[modelId];
  const path = typeof p === 'string' ? [p, name] : p?.[name];
  if (!path) return;
  let o = obj;
  for (const k of path.slice(0, -1)) o = o[k];
  o[path[path.length - 1]] = value;
}

function renderModels() {
  $('#gModels').innerHTML = models.map(m => {
    const [cls, label] = STATUS[m.status] ?? ['none', m.status];
    const params = m.parameters.map(p => `
      <label title="${esc(p.description)}">${esc(p.label)}${p.unit ? ` (${esc(p.unit)})` : ''}</label>
      <input type="number" step="any" data-model="${m.id}" data-param="${p.name}" value="${+p.value.toFixed(6)}" ${p.editable ? '' : 'disabled'}>`).join('');
    const techniques = m.id === 'lowAngle'
      ? `<label>Các góc kỹ thuật (°)</label><input type="text" id="gLowTech" value="${esc(settings.lowAngle.techniques.join(', '))}" style="width:90px">` : '';
    const subs = m.subModels.map(s => `<div class="sub">${esc(s.name)}: <span class="pill ${STATUS[s.status]?.[0]}">${STATUS[s.status]?.[1]}</span> · ${s.samples} lần bắn${s.loocvRmse != null ? ` · LOOCV RMSE ${f(s.loocvRmse)}` : ''}${s.formula ? `<br>${esc(s.formula)}` : ''}</div>`).join('');
    return `<div class="model-card">
      <div class="mc-head"><span class="mc-name">${sw(m.id)} ${esc(m.name)}</span><span class="pill ${cls}">${label}</span></div>
      <div class="mc-desc">${esc(m.description)}</div>
      <div class="mc-status">${esc(m.statusMessage)}</div>
      <details><summary>Điều kiện áp dụng, nguồn & giả định</summary>
        <p><b>Áp dụng:</b> ${esc(m.applicability)}</p>
        <b>Nguồn:</b><ul>${m.sources.map(s => `<li>${s.url ? `<a href="${esc(s.url)}" target="_blank" rel="noopener">${esc(s.title)}</a>` : esc(s.title)} — ${esc(s.note)}</li>`).join('')}</ul>
        <b>Giả định:</b><ul>${m.assumptions.map(a => `<li>${esc(a)}</li>`).join('')}</ul>
        <b>Thời gian bay:</b> ${m.supportsFlightTime ? 'có' : 'mô hình không tính'}
      </details>
      <div class="params">${params}${techniques}</div>
      ${subs}
    </div>`;
  }).join('');
}

async function saveModelParams() {
  const s = structuredClone(settings);
  for (const el of document.querySelectorAll('#gModels input[data-param]')) {
    const v = parseFloat(el.value);
    if (!isFinite(v)) { ctx.toast(`Giá trị không hợp lệ: ${el.dataset.param}`, true); return; }
    setPath(s, el.dataset.model, el.dataset.param, v);
  }
  const tech = $('#gLowTech')?.value;
  if (tech != null) s.lowAngle.techniques = tech.split(/[,;\s]+/).filter(Boolean).map(Number).filter(isFinite);
  s.weaponFilter = $('#gWeapon').value.trim() || null;
  try {
    settings = await api('PUT', '/api/gunny/settings', s);
    await refreshAll();
    ctx.toast('Đã lưu tham số mô hình');
  } catch (e) { ctx.handleError(e); }
}

function renderModelChecks() {
  for (const id of ['gOptModels', 'gAimModels']) {
    const box = $('#' + id);
    const prev = new Set([...box.querySelectorAll('input:checked')].map(i => i.value));
    box.innerHTML = models.map(m =>
      `<label><input type="checkbox" value="${m.id}" ${prev.size === 0 || prev.has(m.id) ? 'checked' : ''}><span class="sw" style="--c:${MODEL_COLORS[m.id]}"></span>${esc(m.name)}</label>`).join('');
  }
}

const checked = (id) => [...document.querySelectorAll(`#${id} input:checked`)].map(i => i.value);

// ------------------------------------------------------------------ dữ liệu thực nghiệm

const EXP_COLS = [
  { k: 'distance', t: 'Khoảng cách', step: 0.1 },
  { k: 'angle', t: 'Góc', step: 1 },
  { k: 'power', t: 'Lực', step: 1 },
  { k: 'wind', t: 'Gió', step: 0.1 },
  { k: 'windDirection', t: 'Hướng gió', type: 'dir' },
  { k: 'weapon', t: 'Vũ khí', type: 'text' },
  { k: 'actualLanding', t: 'Điểm rơi thực tế', step: 0.01, optional: true },
  { k: 'hitTarget', t: 'Trúng', type: 'hit' },
  { k: 'notes', t: 'Ghi chú', type: 'text', wide: true },
];

function bindExperiments() {
  $('#gExpAdd').addEventListener('click', async () => {
    const last = experiments[experiments.length - 1];
    const row = last ? { ...last, id: undefined, actualLanding: null, hitTarget: null, notes: null }
      : { distance: 5, angle: 65, power: 70, wind: 0, windDirection: 'Thuan', weapon: null, actualLanding: null, hitTarget: null, notes: null };
    try {
      experiments.push(await api('POST', '/api/gunny/experiments', row));
      renderExperiments();
      $('#gExpTable tbody tr:last-child input[data-k="actualLanding"]')?.focus();
    } catch (e) { ctx.handleError(e); }
  });
  $('#gExpClear').addEventListener('click', async () => {
    if (!experiments.length || !confirm(`Xoá toàn bộ ${experiments.length} lần bắn đã lưu? Không thể hoàn tác (hãy Xuất CSV trước nếu cần).`)) return;
    try { await api('DELETE', '/api/gunny/experiments'); experiments = []; renderExperiments(); refreshModelsOnly(); } catch (e) { ctx.handleError(e); }
  });
  $('#gExpExport').addEventListener('click', async () => {
    try { ctx.download('gunny-experiments.csv', await api('GET', '/api/gunny/experiments/export')); } catch (e) { ctx.handleError(e); }
  });
  $('#gExpImport').addEventListener('click', () => $('#gImportDialog').showModal());
  $('#gImportFileBtn').addEventListener('click', () => $('#gImportFile').click());
  $('#gImportFile').addEventListener('change', async (e) => {
    const file = e.target.files[0];
    if (file) $('#gImportText').value = await file.text();
    e.target.value = '';
  });
  $('#gImportDialog').addEventListener('close', async () => {
    if ($('#gImportDialog').returnValue !== 'ok') return;
    try {
      const r = await api('POST', `/api/gunny/experiments/import?replace=${$('#gImportReplace').checked}`, $('#gImportText').value, true);
      experiments = await api('GET', '/api/gunny/experiments');
      renderExperiments();
      refreshModelsOnly();
      ctx.toast(`Đã nhập ${r.imported} dòng${r.errors.length ? ` · ${r.errors.length} dòng lỗi: ${r.errors.slice(0, 3).join(' | ')}` : ''}`, r.errors.length > 0);
    } catch (e) { ctx.handleError(e); }
  });
  $('#gExpTable').addEventListener('change', onExpEdit);
  $('#gExpTable').addEventListener('click', async (e) => {
    const tr = e.target.closest('tr[data-id]');
    if (!tr || e.target.dataset.act !== 'del') return;
    try {
      await api('DELETE', `/api/gunny/experiments/${tr.dataset.id}`);
      experiments = experiments.filter(x => x.id !== tr.dataset.id);
      renderExperiments(); refreshModelsOnly();
    } catch (err) { ctx.handleError(err); }
  });
}

async function refreshModelsOnly() {
  try { models = await api('GET', '/api/gunny/models'); renderModels(); } catch { /* bỏ qua */ }
}

function renderExperiments() {
  $('#gExpCount').textContent = experiments.length ? `(${experiments.length})` : '';
  const head = `<thead><tr><th>#</th>${EXP_COLS.map(c => `<th class="${c.type === 'text' ? 'l' : ''}">${c.t}</th>`).join('')}<th></th></tr></thead>`;
  if (!experiments.length) {
    $('#gExpTable').innerHTML = head + `<tbody><tr><td colspan="${EXP_COLS.length + 2}" class="l muted">Chưa có dữ liệu. Bấm "+ Thêm lần bắn" hoặc "Nhập CSV".</td></tr></tbody>`;
    return;
  }
  const cell = (c, e) => {
    const v = e[c.k];
    if (c.type === 'dir') return `<select data-k="${c.k}"><option value="Thuan" ${v === 'Thuan' ? 'selected' : ''}>Thuận</option><option value="Nguoc" ${v === 'Nguoc' ? 'selected' : ''}>Ngược</option></select>`;
    if (c.type === 'hit') return `<select data-k="${c.k}"><option value="" ${v == null ? 'selected' : ''}>—</option><option value="true" ${v === true ? 'selected' : ''}>Có</option><option value="false" ${v === false ? 'selected' : ''}>Không</option></select>`;
    if (c.type === 'text') return `<input type="text" data-k="${c.k}" value="${esc(v ?? '')}" class="${c.wide ? 'wide-in' : ''}">`;
    return `<input type="number" data-k="${c.k}" step="${c.step}" value="${v ?? ''}" ${c.optional ? 'placeholder="—"' : ''}>`;
  };
  $('#gExpTable').innerHTML = head + '<tbody>' + experiments.map((e, i) =>
    `<tr data-id="${e.id}"><td>${i + 1}</td>${EXP_COLS.map(c => `<td class="${c.type === 'text' ? 'l' : ''}">${cell(c, e)}</td>`).join('')}
     <td class="l"><button class="icon-btn danger" data-act="del" title="Xoá lần bắn">✕</button></td></tr>`).join('') + '</tbody>';
}

async function onExpEdit(e) {
  const tr = e.target.closest('tr[data-id]');
  if (!tr) return;
  const row = experiments.find(x => x.id === tr.dataset.id);
  const c = EXP_COLS.find(x => x.k === e.target.dataset.k);
  const raw = e.target.value;
  const next = { ...row };
  if (c.type === 'hit') next[c.k] = raw === '' ? null : raw === 'true';
  else if (c.type === 'dir') next[c.k] = raw;
  else if (c.type === 'text') next[c.k] = raw.trim() || null;
  else if (raw.trim() === '' && c.optional) next[c.k] = null;
  else {
    const v = parseFloat(raw);
    if (!isFinite(v)) { e.target.value = row[c.k] ?? ''; ctx.toast('Giá trị không hợp lệ', true); return; }
    next[c.k] = v;
  }
  try {
    const saved = await api('PUT', `/api/gunny/experiments/${row.id}`, next);
    Object.assign(row, saved);
    tr.classList.remove('saved-flash'); void tr.offsetWidth; tr.classList.add('saved-flash');
    refreshModelsOnly();
  } catch (err) {
    e.target.value = row[c.k] ?? '';
    ctx.handleError(err);
  }
}

// ------------------------------------------------------------------ tối ưu

async function optimize() {
  const btn = $('#gOptimize');
  btn.disabled = true; btn.textContent = 'Đang tính…';
  try {
    const res = await api('POST', '/api/gunny/optimize', { models: checked('gOptModels') });
    renderOptimization(res);
  } catch (e) { ctx.handleError(e); }
  finally { btn.disabled = false; btn.textContent = 'Tối ưu / đánh giá'; }
}

function metricsLine(ev) {
  if (!ev || ev.evaluated === 0) return '<span class="muted">không có lần bắn để tính sai số</span>';
  return `n = <b>${ev.evaluated}</b> · MAE <b>${f(ev.mae)}</b> · RMSE <b>${f(ev.rmse)}</b> · lớn nhất <b>${f(ev.maxError)}</b>${ev.loocvRmse != null ? ` · LOOCV RMSE <b>${f(ev.loocvRmse)}</b>` : ''}`;
}

function deviationTable(ev) {
  if (!ev?.deviations?.length) return '';
  const rows = ev.deviations.map((d, i) => `<tr><td>${i + 1}</td><td>${f(d.distance, 2)}</td><td>${f(d.angle, 1)}</td><td>${f(d.power, 1)}</td><td>${f(d.signedWind, 1)}</td>
    <td>${f(d.actual)}</td><td>${f(d.predicted)}</td><td class="${d.error == null ? '' : Math.abs(d.error) < 0.1 ? 'good' : Math.abs(d.error) > 0.5 ? 'bad' : ''}">${d.error == null ? '—' : (d.error >= 0 ? '+' : '') + f(d.error)}</td>
    <td class="l muted">${esc(d.note ?? '')}</td></tr>`).join('');
  return `<details><summary>Độ lệch tại từng lần bắn (${ev.deviations.length})</summary><div class="table-scroll"><table class="data">
    <thead><tr><th>#</th><th>Khoảng cách</th><th>Góc</th><th>Lực</th><th>Gió (±)</th><th>Thực tế</th><th>Dự đoán</th><th>Sai số</th><th class="l">Ghi chú</th></tr></thead><tbody>${rows}</tbody></table></div></details>`;
}

function renderOptimization(results) {
  $('#gOptCard').classList.remove('hidden');
  $('#gOptResult').innerHTML = results.map(r => {
    const params = r.suggestedParameters
      ? `<div class="metrics">Tham số đề xuất: ${Object.entries(r.suggestedParameters).map(([k, v]) => `${esc(k)} = <b>${f(v, 4)}</b>`).join(' · ')}</div>` : '';
    return `<div class="opt-block">
      <h5>${sw(r.modelId)} ${esc(r.modelName)}${r.method ? ` <span class="muted">· ${esc(r.method)}</span>` : ''}</h5>
      <div class="msg">${esc(r.message)}</div>
      ${r.before.formula ? `<div class="msg">Công thức hiện tại: ${esc(r.before.formula)}</div>` : ''}
      <div class="metrics">Hiện tại: ${metricsLine(r.before)}</div>
      ${r.suggestedParameters ? `<div class="metrics">Sau tối ưu: ${metricsLine(r.after)}</div>` : ''}
      ${params}
      ${r.canApply ? `<button class="mini" data-apply="${r.modelId}">Áp dụng tham số này</button>` : ''}
      ${deviationTable(r.suggestedParameters ? r.after : r.before)}
    </div>`;
  }).join('') || '<div class="muted">Không có mô hình nào được chọn.</div>';
  for (const b of document.querySelectorAll('[data-apply]')) {
    b.addEventListener('click', async () => {
      const r = results.find(x => x.modelId === b.dataset.apply);
      try {
        settings = await api('POST', '/api/gunny/optimize/apply', { modelId: r.modelId, parameters: r.suggestedParameters });
        await refreshAll();
        ctx.toast(`Đã áp dụng tham số cho ${r.modelName}`);
        b.disabled = true; b.textContent = 'Đã áp dụng';
      } catch (e) { ctx.handleError(e); }
    });
  }
}

// ------------------------------------------------------------------ tìm góc & lực, so sánh

function aimRequest() {
  const target = parseFloat($('#gTarget').value);
  const wind = (parseFloat($('#gWind').value) || 0) * parseFloat($('#gWindDir').value);
  return { targetDistance: target, signedWind: wind, maxPerModel: parseInt($('#gMaxPer').value) || 5, models: checked('gAimModels') };
}

async function aim() {
  try {
    const r = await api('POST', '/api/gunny/aim', aimRequest());
    $('#gAimCard').classList.remove('hidden');
    const rows = r.solutions.map(s => `<tr>
      <td class="l">${sw(s.modelId)} ${esc(s.modelName)}</td><td>${f(s.angle, 1)}°</td><td>${f(s.power, 1)}</td><td>${(s.signedWind > 0 ? '+' : '') + f(s.signedWind, 1)}</td>
      <td>${f(s.predictedLanding)}</td><td>${(s.error >= 0 ? '+' : '') + f(s.error)}</td><td>${s.flightTime != null ? f(s.flightTime, 2) + ' s' : '—'}</td>
      <td class="l">${s.confirmed ? `<span class="pill confirmed">Đã xác nhận (${s.confirmCount})</span>` : '<span class="pill predicted">Chỉ dự đoán</span>'}</td>
      <td class="l muted">${esc(s.note ?? '')}</td></tr>`).join('');
    const msgs = r.perModel.filter(m => m.message).map(m => `<li>${sw(m.modelId)} <b>${esc(m.modelName)}</b>: ${esc(m.message)}</li>`).join('');
    $('#gAimResult').innerHTML = `<div class="metrics">Mục tiêu <b>${f(r.targetDistance, 2)}</b> khoảng · gió <b>${(r.signedWind > 0 ? '+' : '') + f(r.signedWind, 1)}</b> (thuận dương) · sắp xếp: đã xác nhận trước, rồi theo |sai số| tăng dần</div>
      ${rows ? `<div class="table-scroll"><table class="data"><thead><tr><th class="l">Mô hình</th><th>Góc</th><th>Lực</th><th>Gió</th><th>Điểm rơi dự đoán</th><th>Sai số</th><th>Thời gian bay</th><th class="l">Trạng thái</th><th class="l">Ghi chú</th></tr></thead><tbody>${rows}</tbody></table></div>` : '<p class="muted">Không mô hình nào đưa ra được phương án.</p>'}
      ${msgs ? `<ul class="note">${msgs}</ul>` : ''}`;
  } catch (e) { ctx.handleError(e); }
}

async function compare() {
  try {
    const r = await api('POST', '/api/gunny/compare', aimRequest());
    const factor = r.calibrationFactor;
    ctx.clearPreview();
    ctx.setTargetDistance(r.targetSimDistance);
    const layers = r.entries.filter(e => e.trajectory.length).map(e => ({
      points: e.trajectory, color: MODEL_COLORS[e.modelId] ?? '#fff', width: 2.2,
      label: `${e.modelName}: ${f(e.solution.angle, 1)}° / ${f(e.solution.power, 1)}`,
      result: { landingX: e.physicsLanding / factor, landingY: 0, status: 'HitGround', maxHeight: Math.max(...e.trajectory.map(p => p.y)),
        maxHeightX: e.trajectory.reduce((a, p) => p.y > a.y ? p : a).x, launchX: e.trajectory[0].x, launchY: e.trajectory[0].y },
      compact: true,
    }));
    ctx.setExtraLayers(layers);
    ctx.setObservations(r.entries.filter(e => e.modelLanding != null).map(e => ({ x: e.modelLanding / factor, y: 0, label: e.modelName.charAt(0) })));

    $('#gCmpCard').classList.remove('hidden');
    $('#gCmpTable').innerHTML = `<thead><tr><th class="l">Mô hình</th><th>Góc</th><th>Lực</th><th>Điểm rơi theo mô hình</th><th>Sai số mô hình</th>
      <th>Điểm rơi theo vật lý</th><th>Sai số vật lý</th><th>Thời gian bay (vật lý)</th><th class="l">Trạng thái / ghi chú</th></tr></thead><tbody>` +
      r.entries.map(e => e.solution ? `<tr>
        <td class="l">${sw(e.modelId)} ${esc(e.modelName)}</td><td>${f(e.solution.angle, 1)}°</td><td>${f(e.solution.power, 1)}</td>
        <td>${f(e.modelLanding)}</td><td>${(e.modelError >= 0 ? '+' : '') + f(e.modelError)}</td>
        <td>${f(e.physicsLanding)}</td><td>${(e.physicsError >= 0 ? '+' : '') + f(e.physicsError)}</td><td>${f(e.physicsFlightTime, 2)} s</td>
        <td class="l">${e.solution.confirmed ? '<span class="pill confirmed">Đã xác nhận</span>' : '<span class="pill predicted">Chỉ dự đoán</span>'} <span class="muted">${esc(e.solution.note ?? '')}</span></td></tr>`
        : `<tr><td class="l">${sw(e.modelId)} ${esc(e.modelName)}</td><td colspan="8" class="l muted">${esc(e.message)}</td></tr>`).join('') + '</tbody>';
    ctx.toast(`Đã vẽ ${layers.length} đường đạn – mục tiêu ${f(r.targetDistance, 2)} khoảng = ${f(r.targetSimDistance, 2)} u`);
  } catch (e) { ctx.handleError(e); }
}
