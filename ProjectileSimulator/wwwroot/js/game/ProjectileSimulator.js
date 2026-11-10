// =============================================================================
// ProjectileSimulator.js – cầu nối tới Physics Engine C# (POST /api/simulation/run).
//
// Trình duyệt KHÔNG tự tính vật lý. Backend mô phỏng từng bước thời gian (Δt) và trả về
// danh sách điểm {t, x, y, vx, vy}. Ở đây chỉ:
//   1. dựng SimulationRequest từ trạng thái game,
//   2. gọi API,
//   3. phát lại quỹ đạo theo thời gian thực (nội suy tuyến tính giữa hai điểm liền kề).
//
// Hệ toạ độ backend: người bắn đứng tại x = 0, mặt đất y = 0, trục Y hướng lên.
// Game cộng thêm vị trí thật của nhân vật A (originX) khi vẽ.
// =============================================================================

/** Thông số hình học dùng chung cho game và backend (đơn vị u). */
export const GEOMETRY = {
  launchHeight: 0.4,   // độ cao trục nòng súng so với mặt đất
  barrelLength: 0.3,   // chiều dài nòng – đạn xuất phát ở đầu nòng
  targetWidth: 0.4,    // hộp va chạm của mục tiêu B
  targetHeight: 0.6,
};

/**
 * @param {{ax:number,bx:number,angle:number,power:number}} game
 * @param {object} s  cài đặt nâng cao (gravity, windEnabled, wind, windMode, dragEnabled, dragCoefficient, deltaTime)
 */
export function buildRequest(game, s) {
  return {
    distance: Math.max(0.1, game.bx - game.ax),
    angle: game.angle,
    power: game.power,                 // 0–100 %  →  V0 = 0.14 · power (u/s), xem ShotSetup.cs
    wind: s.windEnabled ? s.wind : 0,
    windEnabled: s.windEnabled,
    windMode: s.windMode,
    gravity: s.gravity,
    dragType: s.dragEnabled ? 'Quadratic' : 'None',
    dragCoefficient: s.dragCoefficient,
    integrator: 'SemiImplicitEuler',
    deltaTime: s.deltaTime,
    maxTime: 30,
    maxTrajectoryPoints: 4000,
    barrelLength: GEOMETRY.barrelLength,
    targetWidth: GEOMETRY.targetWidth,
    targetHeight: GEOMETRY.targetHeight,
    targetCollisionEnabled: true,
    groundLevel: 0,
    calibration: { launchHeight: GEOMETRY.launchHeight },
  };
}

export async function simulate(request, signal) {
  const res = await fetch('/api/simulation/run', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    signal,
  });
  if (!res.ok) {
    let msg = `Lỗi máy chủ (${res.status})`;
    try {
      const p = await res.json();
      const parts = [p.detail, ...Object.values(p.errors ?? {}).flat()].filter(Boolean);
      if (parts.length) msg = parts.join(' · ');
    } catch { /* không phải JSON */ }
    throw new Error(msg);
  }
  return res.json();
}

/**
 * Quỹ đạo dự đoán (trợ giúp ngắm): chỉ gửi yêu cầu mới nhất, huỷ yêu cầu cũ,
 * giới hạn tần suất để không spam máy chủ khi đang tích lực.
 */
export class Predictor {
  constructor(onResult, minIntervalMs = 50) {
    this.onResult = onResult;
    this.minInterval = minIntervalMs;
    this.pending = null;
    this.timer = 0;
    this.lastSent = 0;
    this.controller = null;
    this.seq = 0;
  }

  request(req) {
    this.pending = req;
    if (this.timer) return;
    const wait = Math.max(0, this.minInterval - (performance.now() - this.lastSent));
    this.timer = setTimeout(() => this._send(), wait);
  }

  cancel() {
    clearTimeout(this.timer);
    this.timer = 0;
    this.pending = null;
    this.controller?.abort();
  }

  async _send() {
    this.timer = 0;
    const req = this.pending;
    this.pending = null;
    if (!req) return;
    this.lastSent = performance.now();
    this.controller?.abort();
    const controller = new AbortController();
    this.controller = controller;
    const seq = ++this.seq;
    try {
      const r = await simulate(req, controller.signal);
      if (seq === this.seq) this.onResult(r, null);
    } catch (e) {
      if (e.name !== 'AbortError') this.onResult(null, e);
    }
  }
}

/** Phát lại một quỹ đạo đã tính sẵn theo thời gian thực. */
export class ShotPlayback {
  constructor(result, originX, speed = 1) {
    this.result = result;
    this.points = result.trajectory;
    this.originX = originX;   // vị trí x của nhân vật A tại thời điểm bắn (A có thể di chuyển sau đó)
    this.speed = speed;
    this.time = 0;
    this.index = 0;
    this.done = false;
  }

  get duration() { return this.points[this.points.length - 1].t; }

  /** Tiến dt giây thực; trả về vị trí hiện tại của viên đạn (toạ độ thế giới game). */
  advance(dt) {
    const pts = this.points;
    this.time = Math.min(this.duration, this.time + dt * this.speed);
    if (this.time >= this.duration) this.done = true;
    while (this.index < pts.length - 2 && pts[this.index + 1].t <= this.time) this.index++;
    const a = pts[this.index];
    const b = pts[Math.min(this.index + 1, pts.length - 1)];
    const f = b.t > a.t ? Math.min(1, (this.time - a.t) / (b.t - a.t)) : 1;
    return {
      x: this.originX + a.x + (b.x - a.x) * f,
      y: a.y + (b.y - a.y) * f,
      vx: a.vx + (b.vx - a.vx) * f,
      vy: a.vy + (b.vy - a.vy) * f,
    };
  }
}
