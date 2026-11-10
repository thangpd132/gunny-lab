// =============================================================================
// GameController.js – vòng lặp game và trạng thái màn chơi.
//
// Vòng đời một lượt bắn:
//   ngắm (↑↓ ←→)  →  giữ Space (tích lực)  →  thả Space (chốt lực)  →
//   gọi Physics Engine (POST /api/simulation/run)  →  đạn bay theo thời gian thực  →
//   chạm mục tiêu / mặt đất  →  hiển thị kết quả  →  lượt mới.
//
// Trong khi đạn đang bay không thể bắn tiếp (giống game theo lượt).
// =============================================================================
import { KeyboardController } from './KeyboardController.js';
import { PowerController } from './PowerController.js';
import { CanvasRenderer, ARENA } from './CanvasRenderer.js';
import { buildRequest, simulate, Predictor, ShotPlayback } from './ProjectileSimulator.js';

const $ = (s) => document.querySelector(s);

// ------------------------------------------------------------------ cấu hình

const START = { ax: 1, bx: 6, angle: 45 };          // A ở trái, B ở phải, khoảng cách 5
const DIST_MIN = 1, DIST_MAX = 10;
const EDGE = 0.3;                                   // khoảng cách tối thiểu tới mép sân
const SHOT_COLORS = ['#ff6b1a', '#ffd23f', '#e84393', '#00b894', '#6c5ce7'];
const SETTINGS_KEY = 'gunnylab.game.settings.v1';

const DEFAULT_SETTINGS = {
  gravity: 9.81,
  windEnabled: true,
  wind: 0,
  windMode: 'WindAcceleration',
  randomWind: false,
  dragEnabled: false,
  dragCoefficient: 0.47,
  moveSpeed: 1.5,          // u/s khi giữ ← →
  fillTime: 2,             // giây để lực đầy 100 %
  playbackSpeed: 1,
  showPrediction: true,
  keepTrails: true,
  showGrid: true,
  deltaTime: 0.016,
};

// ------------------------------------------------------------------ trạng thái

const settings = loadSettings();
const state = {
  ax: START.ax,            // vị trí x của nhân vật A (u)
  bx: START.bx,            // vị trí x của mục tiêu B (u)
  angle: START.angle,      // 0–180°
  flying: null,            // { playback, pos, index, points, originX, request }
  firing: false,           // đang chờ máy chủ trả quỹ đạo
  shots: [],               // các đường đạn đã bắn (nét liền)
  prediction: null,        // quỹ đạo dự đoán (nét đứt)
  shotCount: 0,
  hits: 0,
};

const renderer = new CanvasRenderer($('#gameCanvas'));
const power = new PowerController({ fillTime: settings.fillTime, onChange: updatePowerUI });
const predictor = new Predictor((r, err) => {
  if (err) { setApi(false); return; }
  setApi(true);
  state.prediction = r;
});

const keyboard = new KeyboardController({
  onAngleStep: (dir) => setAngle(state.angle + dir),
  onChargeStart: () => {
    if (isBusy()) { flash('Đạn đang bay – chờ lượt sau'); return; }
    power.startCharge();
    renderer.focusOn(null);
  },
  onChargeRelease: () => {
    const locked = power.release();      // chốt lực ĐÚNG lúc thả phím
    if (locked != null) fire(locked);
  },
  onFire: () => fire(power.value),
  onReset: () => reset(),
  onFocusLost: () => { if (power.cancel()) flash('Cửa sổ mất focus – đã huỷ tích lực'); },
  isEnabled: () => !$('#settings').classList.contains('open'),
});

// expose cho kiểm thử tự động / gỡ lỗi trong console
window.__game = { state, power, settings, keyboard };

// ------------------------------------------------------------------ hành động

function isBusy() { return state.firing || state.flying != null; }

function distance() { return state.bx - state.ax; }

function setAngle(a) {
  const v = Math.round(Math.min(180, Math.max(0, a)));
  if (v === state.angle) return;
  state.angle = v;
  updateHud();
  requestPrediction();
}

/** Di chuyển A; B đứng yên. Giới hạn trong sân và khoảng cách 1–10. */
function moveShooter(dx) {
  const min = Math.max(ARENA.minX + EDGE, state.bx - DIST_MAX);
  const max = state.bx - DIST_MIN;
  const nx = Math.min(max, Math.max(min, state.ax + dx));
  if (nx === state.ax) return false;
  state.ax = nx;
  return true;
}

/** Thanh khoảng cách: B di chuyển theo (A chỉ lùi lại nếu B chạm mép phải). */
function setDistance(d) {
  d = Math.min(DIST_MAX, Math.max(DIST_MIN, d));
  state.bx = state.ax + d;
  if (state.bx > ARENA.maxX - EDGE) {
    state.bx = ARENA.maxX - EDGE;
    state.ax = state.bx - d;
  }
  updateHud();
  requestPrediction();
}

async function fire(pwr) {
  if (isBusy()) { flash('Đạn đang bay – chờ lượt sau'); return; }
  if (power.charging) return;
  state.firing = true;
  updateHud();
  const request = buildRequest({ ax: state.ax, bx: state.bx, angle: state.angle, power: pwr }, settings);
  const originX = state.ax;
  try {
    const result = await simulate(request);
    setApi(true);
    power.markShot(pwr);
    state.flying = {
      playback: new ShotPlayback(result, originX, settings.playbackSpeed),
      points: result.trajectory, originX, index: 0, pos: { x: originX + result.launchX, y: result.launchY },
      request, power: pwr, angle: state.angle,
    };
    renderer.focusOn(boundsOf(result, originX));
  } catch (e) {
    setApi(false);
    flash(e.message, true);
  } finally {
    state.firing = false;
    updateHud();
  }
}

function onShotLanded(f) {
  const r = f.playback.result;
  state.shotCount++;
  if (r.hitTarget) state.hits++;
  const color = SHOT_COLORS[(state.shotCount - 1) % SHOT_COLORS.length];
  if (!settings.keepTrails) state.shots = [];
  state.shots.push({ points: f.points, originX: f.originX, result: r, color, alpha: 1 });
  if (state.shots.length > 5) state.shots.shift();
  state.shots.forEach((s, i, a) => { s.alpha = i === a.length - 1 ? 1 : 0.35 + 0.1 * i; });

  const lx = f.originX + r.landingX;
  renderer.explode(lx, r.landingY, r.hitTarget);
  const miss = r.missDistance;
  if (r.hitTarget) renderer.floatText(lx, r.landingY, 'TRÚNG!', '#4ade80');
  else if (r.status === 'HitGround') renderer.floatText(lx, r.landingY, `Trượt ${miss > 0 ? '+' : ''}${miss.toFixed(2)}`, '#ffb4b4');
  else renderer.floatText(Math.min(Math.max(lx, 0), 12), 0.5, r.collisionStatus, '#ffb4b4');

  showShotCard(f, r);
  state.flying = null;
  if (settings.randomWind && settings.windEnabled) {
    settings.wind = Math.round((Math.random() * 40 - 20) * 2) / 2;
    syncSettingsUI();
  }
  setTimeout(() => { if (!isBusy() && !power.charging) renderer.focusOn(null); }, 1300);
  updateHud();
  requestPrediction();
}

function reset() {
  state.ax = START.ax;
  state.bx = START.bx;
  state.angle = START.angle;
  state.flying = null;
  state.shots = [];
  state.prediction = null;
  state.shotCount = 0;
  state.hits = 0;
  power.reset();
  renderer.focusOn(null);
  $('#shotCard').classList.add('hidden');
  updateHud();
  requestPrediction();
  flash('Đã đặt lại');
}

function boundsOf(result, originX) {
  let minX = Infinity, maxX = -Infinity, maxY = 0;
  for (const p of result.trajectory) {
    const x = originX + p.x;
    if (x < minX) minX = x; if (x > maxX) maxX = x; if (p.y > maxY) maxY = p.y;
  }
  return { minX, maxX, maxY };
}

function requestPrediction() {
  if (!settings.showPrediction || power.value <= 0) { predictor.cancel(); state.prediction = null; return; }
  predictor.request(buildRequest({ ax: state.ax, bx: state.bx, angle: state.angle, power: power.value }, settings));
}

// ------------------------------------------------------------------ vòng lặp

let last = performance.now();
let lastHudDistance = null;
function frame(now) {
  const dt = Math.min(0.05, (now - last) / 1000);
  last = now;

  // di chuyển liên tục khi giữ phím
  const dir = (keyboard.isHeld('ArrowRight') ? 1 : 0) - (keyboard.isHeld('ArrowLeft') ? 1 : 0);
  if (dir && moveShooter(dir * settings.moveSpeed * dt)) requestPrediction();

  power.update(dt);

  if (state.flying) {
    const f = state.flying;
    f.pos = f.playback.advance(dt);
    f.index = f.playback.index;
    if (f.playback.done) onShotLanded(f);
  }

  const d = distance().toFixed(1);
  if (d !== lastHudDistance) { lastHudDistance = d; updateHud(); }

  renderer.render({
    ax: state.ax, bx: state.bx, angle: state.angle,
    wind: settings.wind, windEnabled: settings.windEnabled,
    showGrid: settings.showGrid,
    shots: state.shots,
    prediction: settings.showPrediction && !state.flying ? state.prediction : null,
    flying: state.flying,
  }, dt);
  requestAnimationFrame(frame);
}

// ------------------------------------------------------------------ HUD

function updatePowerUI(p) {
  const v = p.value;
  $('#powerValue').textContent = Math.round(v);
  const fill = $('#powerFill');
  fill.style.width = v + '%';
  fill.style.setProperty('--pct', Math.max(0.5, v));
  $('#powerTrack').setAttribute('aria-valuenow', String(Math.round(v)));
  $('#powerTrack').classList.toggle('charging', p.charging);
  $('#powerHint').textContent = p.charging ? 'Đang tích lực… thả Space để bắn' : 'Giữ Space để tích lực · kéo thanh để đặt lực';
  const lastEl = $('#powerLast');
  lastEl.classList.toggle('hidden', p.lastShot == null);
  if (p.lastShot != null) lastEl.style.left = p.lastShot + '%';
  requestPrediction();
}

function updateHud() {
  $('#angleValue').textContent = state.angle;
  $('#dialNeedle').setAttribute('transform', `rotate(${-state.angle})`);
  $('#distanceValue').textContent = distance().toFixed(1);
  $('#distanceSlider').value = String(distance());
  $('#windValue').textContent = settings.windEnabled ? `${settings.wind > 0 ? '→' : settings.wind < 0 ? '←' : ''} ${Math.abs(settings.wind).toFixed(1)}` : 'Tắt';
  $('#btnFire').disabled = isBusy();
}

function showShotCard(f, r) {
  const card = $('#shotCard');
  const miss = r.missDistance;
  card.innerHTML = `
    <div class="title ${r.hitTarget ? 'hit' : 'miss'}">Phát #${state.shotCount}: ${r.hitTarget ? 'Trúng mục tiêu' : r.collisionStatus}</div>
    Góc <b>${f.angle}°</b> · Lực <b>${Math.round(f.power)}%</b> · Khoảng cách <b>${r.distance.toFixed(1)}</b><br>
    Thời gian bay <b>${r.flightTime.toFixed(2)} s</b> · Cao nhất <b>${r.maxHeight.toFixed(2)}</b><br>
    Điểm rơi <b>${r.landingX.toFixed(2)}</b> · Lệch <b>${miss > 0 ? '+' : ''}${miss.toFixed(2)}</b><br>
    Trúng <b>${state.hits}/${state.shotCount}</b> phát`;
  card.classList.remove('hidden');
}

let flashTimer = 0;
function flash(text, error = false) {
  const m = $('#message');
  m.textContent = text;
  m.style.color = error ? '#ffb4b4' : '';
  m.classList.add('show');
  clearTimeout(flashTimer);
  flashTimer = setTimeout(() => m.classList.remove('show'), error ? 4000 : 1600);
}

function setApi(ok) {
  const d = $('#apiDot');
  d.classList.toggle('ok', ok);
  d.classList.toggle('err', !ok);
  d.title = ok ? 'Máy chủ vật lý: đã kết nối' : 'Không kết nối được máy chủ vật lý';
}

// ------------------------------------------------------------------ thanh lực kéo tay

function bindPowerDrag() {
  const track = $('#powerTrack');
  const setFrom = (e) => {
    const r = track.getBoundingClientRect();
    power.set(((e.clientX - r.left) / r.width) * 100);
  };
  track.addEventListener('pointerdown', (e) => {
    if (power.charging) return;
    track.setPointerCapture(e.pointerId);
    setFrom(e);
    const move = (ev) => setFrom(ev);
    const up = () => { track.removeEventListener('pointermove', move); track.removeEventListener('pointerup', up); };
    track.addEventListener('pointermove', move);
    track.addEventListener('pointerup', up);
  });
}

// ------------------------------------------------------------------ cài đặt

function loadSettings() {
  try {
    return { ...DEFAULT_SETTINGS, ...(JSON.parse(localStorage.getItem(SETTINGS_KEY)) ?? {}) };
  } catch { return { ...DEFAULT_SETTINGS }; }
}

function saveSettings() {
  try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings)); } catch { /* bỏ qua */ }
}

function syncSettingsUI() {
  for (const el of document.querySelectorAll('[data-setting]')) {
    const k = el.dataset.setting;
    if (el.type === 'checkbox') el.checked = !!settings[k];
    else el.value = String(settings[k]);
  }
  for (const out of document.querySelectorAll('[data-out]')) {
    const v = settings[out.dataset.out];
    out.textContent = typeof v === 'number' ? (Math.abs(v) < 1 && v !== 0 ? v.toFixed(3).replace(/0+$/, '') : String(+v.toFixed(2))) : v;
  }
  updateHud();
}

function bindSettings() {
  for (const el of document.querySelectorAll('[data-setting]')) {
    el.addEventListener(el.type === 'range' ? 'input' : 'change', () => {
      const k = el.dataset.setting;
      settings[k] = el.type === 'checkbox' ? el.checked : el.type === 'range' ? +el.value : el.value;
      if (k === 'fillTime') power.fillTime = settings.fillTime;
      syncSettingsUI();
      saveSettings();
      requestPrediction();
    });
  }
  const drawer = $('#settings');
  const open = (v) => {
    drawer.classList.toggle('open', v);
    drawer.setAttribute('aria-hidden', String(!v));
    if (!v) document.activeElement?.blur?.();
  };
  $('#btnSettings').addEventListener('click', () => open(!drawer.classList.contains('open')));
  $('#btnCloseSettings').addEventListener('click', () => open(false));
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') open(false); });
  $('#btnDefaults').addEventListener('click', () => {
    Object.assign(settings, DEFAULT_SETTINGS);
    power.fillTime = settings.fillTime;
    syncSettingsUI(); saveSettings(); requestPrediction();
  });
}

// ------------------------------------------------------------------ khởi động

function boot() {
  // vạch chia đồng hồ góc: mỗi 15°, nửa trên
  const ticks = $('#dialTicks');
  for (let a = 0; a <= 180; a += 15) {
    const r1 = a % 45 === 0 ? 30 : 34, rad = a * Math.PI / 180;
    const l = document.createElementNS('http://www.w3.org/2000/svg', 'line');
    l.setAttribute('x1', String(Math.cos(rad) * r1)); l.setAttribute('y1', String(-Math.sin(rad) * r1));
    l.setAttribute('x2', String(Math.cos(rad) * 40)); l.setAttribute('y2', String(-Math.sin(rad) * 40));
    ticks.appendChild(l);
  }

  // nút không nhận focus bằng chuột → Space/Enter không "bấm" nhầm nút
  for (const b of document.querySelectorAll('button, a.chip, input[type=range]')) b.addEventListener('mousedown', (e) => {
    if (b.tagName !== 'INPUT') e.preventDefault();
  });
  $('#btnFire').addEventListener('click', () => fire(power.value));
  $('#btnReset').addEventListener('click', reset);
  $('#distanceSlider').addEventListener('input', (e) => setDistance(+e.target.value));
  $('#distanceSlider').addEventListener('change', (e) => e.target.blur());

  bindPowerDrag();
  bindSettings();
  syncSettingsUI();
  updatePowerUI(power);
  updateHud();

  fetch('/api/simulation/defaults').then(r => setApi(r.ok)).catch(() => {
    setApi(false);
    flash('Không kết nối được máy chủ vật lý – hãy chạy `dotnet run`', true);
  });
  requestAnimationFrame(frame);
}

boot();
