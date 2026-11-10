// =============================================================================
// CanvasRenderer.js – vẽ màn chơi.
//
// HỆ TOẠ ĐỘ (một nguồn duy nhất cho mọi thứ được vẽ):
//   Thế giới : X sang phải, Y hướng LÊN, đơn vị u (1 u = 1 vạch của thước đo khoảng cách),
//              mặt đất y = 0, sân chơi x ∈ [0, 12].
//   Màn hình : X sang phải, Y hướng XUỐNG, đơn vị pixel CSS.
//
//     sx = (x − camera.x0) · camera.scale
//     sy = groundY − y · camera.scale          (groundY = vị trí pixel của mặt đất)
//
// Cùng một hệ số scale cho cả hai trục ⇒ khoảng cách, góc và quỹ đạo không bị méo.
// Nhân vật, mục tiêu, nòng súng, viên đạn và thước đo đều đi qua toScreen() này.
// Camera tự thu nhỏ (giữ nguyên tỉ lệ) khi đường đạn vượt khỏi khung hình.
// =============================================================================
import { GEOMETRY } from './ProjectileSimulator.js';

export const ARENA = { minX: 0, maxX: 12 };
const GROUND_BAND = 84;      // chiều cao dải đất dưới mặt đất (chứa thước đo), px
const MIN_VIEW_HEIGHT = 4.2; // chiều cao tối thiểu nhìn thấy phía trên mặt đất, u
const TAU = Math.PI * 2;

export class CanvasRenderer {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.w = 1; this.h = 1; this.dpr = 1;
    this.camera = { x0: 0, scale: 100 };
    this.target = { x0: 0, scale: 100 };
    this.focusBounds = null;            // khung cần nhìn thấy (đường đạn đang bay)
    this.effects = [];
    new ResizeObserver(() => this.resize()).observe(canvas.parentElement);
    this.resize();
    this.camera = { ...this.target };
  }

  // ------------------------------------------------------------------ camera

  resize() {
    const r = this.canvas.parentElement.getBoundingClientRect();
    this.dpr = Math.min(2, window.devicePixelRatio || 1);
    this.w = Math.max(100, Math.floor(r.width));
    this.h = Math.max(100, Math.floor(r.height));
    this.canvas.width = Math.floor(this.w * this.dpr);
    this.canvas.height = Math.floor(this.h * this.dpr);
    this.canvas.style.width = this.w + 'px';
    this.canvas.style.height = this.h + 'px';
    this._computeTarget();
    this.camera = { ...this.target };
  }

  get groundY() { return this.h - GROUND_BAND; }

  /** Khung nhìn mặc định: cả sân 0–12 u, hoặc vừa khung đường đạn khi đang theo dõi. */
  _computeTarget() {
    let minX = ARENA.minX - 0.2, maxX = ARENA.maxX + 0.2, maxY = MIN_VIEW_HEIGHT;
    const b = this.focusBounds;
    if (b) {
      minX = Math.min(minX, b.minX - 0.6);
      maxX = Math.max(maxX, b.maxX + 0.6);
      maxY = Math.max(maxY, b.maxY + 0.9);
    }
    const scale = Math.max(10, Math.min(this.w / (maxX - minX), this.groundY / maxY));
    const x0 = (minX + maxX) / 2 - this.w / scale / 2;
    this.target = { x0, scale };
  }

  /** Theo dõi một quỹ đạo (toạ độ thế giới) – camera thu nhỏ mượt để thấy toàn bộ. */
  focusOn(bounds) {
    this.focusBounds = bounds ? {
      minX: Math.max(-40, bounds.minX), maxX: Math.min(55, bounds.maxX), maxY: Math.min(40, bounds.maxY),
    } : null;
    this._computeTarget();
  }

  updateCamera(dt) {
    const k = Math.min(1, dt * 5);
    this.camera.scale += (this.target.scale - this.camera.scale) * k;
    this.camera.x0 += (this.target.x0 - this.camera.x0) * k;
  }

  toScreen(x, y) {
    return [(x - this.camera.x0) * this.camera.scale, this.groundY - y * this.camera.scale];
  }

  toWorldX(sx) { return sx / this.camera.scale + this.camera.x0; }

  // ------------------------------------------------------------------ effects

  explode(x, y, hit) {
    const parts = [];
    for (let i = 0; i < 26; i++) {
      const a = Math.random() * Math.PI;           // nửa trên
      const v = 1 + Math.random() * 2.5;
      parts.push({ x, y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, life: 0.5 + Math.random() * 0.5 });
    }
    this.effects.push({ kind: 'boom', x, y, t: 0, hit, parts });
  }

  floatText(x, y, text, color) {
    this.effects.push({ kind: 'text', x, y, t: 0, text, color });
  }

  _updateEffects(dt) {
    for (const e of this.effects) {
      e.t += dt;
      if (e.parts) for (const p of e.parts) { p.vy -= 6 * dt; p.x += p.vx * dt; p.y = Math.max(0, p.y + p.vy * dt); p.life -= dt; }
    }
    this.effects = this.effects.filter(e => e.t < (e.kind === 'text' ? 1.8 : 1.1));
  }

  // ------------------------------------------------------------------ render

  /**
   * @param {object} s  trạng thái game (xem GameController.renderState)
   * @param {number} dt giây kể từ frame trước
   */
  render(s, dt) {
    this.updateCamera(dt);
    this._updateEffects(dt);
    const ctx = this.ctx;
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);

    this._sky(ctx);
    if (s.showGrid) this._grid(ctx);
    this._ground(ctx);
    this._ruler(ctx, s);

    // đường dự đoán (nét đứt) vẽ dưới, đường đạn thực tế (nét liền) vẽ đè lên
    if (s.prediction) this._trajectory(ctx, s.prediction.trajectory, s.ax, { color: '#ffffff', width: 2, alpha: 0.7, dash: [7, 7] });
    for (const shot of s.shots) this._trajectory(ctx, shot.points, shot.originX, { color: shot.color, width: 2.5, alpha: shot.alpha, result: shot.result });

    this._target(ctx, s);
    this._shooter(ctx, s);
    if (s.flying) this._bullet(ctx, s.flying);
    this._effects(ctx);
    this._wind(ctx, s);
  }

  _sky(ctx) {
    const g = ctx.createLinearGradient(0, 0, 0, this.groundY);
    g.addColorStop(0, '#4f9bd9');
    g.addColorStop(1, '#bfe3f5');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, this.w, this.groundY + 1);
  }

  _grid(ctx) {
    const sc = this.camera.scale;
    const step = sc >= 35 ? 1 : sc >= 15 ? 2 : 5;
    const x0 = Math.floor(this.toWorldX(0) / step) * step;
    const x1 = this.toWorldX(this.w);
    ctx.strokeStyle = 'rgba(255,255,255,0.22)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = x0; x <= x1; x += step) {
      const [sx] = this.toScreen(x, 0);
      ctx.moveTo(Math.round(sx) + 0.5, 0); ctx.lineTo(Math.round(sx) + 0.5, this.groundY);
    }
    const ytop = this.groundY / sc;
    for (let y = step; y <= ytop; y += step) {
      const [, sy] = this.toScreen(0, y);
      ctx.moveTo(0, Math.round(sy) + 0.5); ctx.lineTo(this.w, Math.round(sy) + 0.5);
    }
    ctx.stroke();
  }

  _ground(ctx) {
    const gy = this.groundY;
    const g = ctx.createLinearGradient(0, gy, 0, this.h);
    g.addColorStop(0, '#8a5a2b');
    g.addColorStop(1, '#5b3a1c');
    ctx.fillStyle = g;
    ctx.fillRect(0, gy, this.w, this.h - gy);
    ctx.fillStyle = '#5cb83a';
    ctx.fillRect(0, gy - 2, this.w, 9);
    ctx.fillStyle = '#3f8f25';
    ctx.fillRect(0, gy + 7, this.w, 2);
  }

  /** Thước đo khoảng cách: vạch 0 tại nhân vật A, mỗi vạch = 1 u, đoạn A→B được tô sáng. */
  _ruler(ctx, s) {
    const gy = this.groundY;
    const y = gy + 34;
    const [ax] = this.toScreen(s.ax, 0);
    const [bx] = this.toScreen(s.bx, 0);
    const [end] = this.toScreen(s.ax + 10, 0);

    // thanh nền 0–10
    ctx.fillStyle = 'rgba(0,0,0,0.35)';
    roundRect(ctx, ax - 6, y - 7, end - ax + 12, 14, 7); ctx.fill();
    // đoạn A → B
    ctx.fillStyle = '#ffd23f';
    roundRect(ctx, Math.min(ax, bx), y - 4, Math.abs(bx - ax), 8, 4); ctx.fill();

    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    for (let i = 0; i <= 20; i++) {
      const v = i / 2;
      const [sx] = this.toScreen(s.ax + v, 0);
      const major = i % 2 === 0;
      ctx.strokeStyle = major ? '#ffffff' : 'rgba(255,255,255,0.6)';
      ctx.lineWidth = major ? 2 : 1;
      ctx.beginPath();
      ctx.moveTo(sx, y - (major ? 11 : 7)); ctx.lineTo(sx, y + (major ? 11 : 7));
      ctx.stroke();
      if (major) {
        ctx.fillStyle = '#fff';
        ctx.font = '700 13px system-ui, sans-serif';
        ctx.fillText(String(v), sx, y + 13);
      }
    }
    // dấu B
    ctx.fillStyle = '#ffd23f';
    ctx.beginPath();
    ctx.moveTo(bx, y - 10); ctx.lineTo(bx - 7, y - 20); ctx.lineTo(bx + 7, y - 20); ctx.closePath(); ctx.fill();

    // nhãn khoảng cách
    const d = s.bx - s.ax;
    const label = `Khoảng cách ${d.toFixed(1)}`;
    ctx.font = '800 14px system-ui, sans-serif';
    const tw = ctx.measureText(label).width + 18;
    const mx = (ax + bx) / 2;
    ctx.fillStyle = 'rgba(20,20,30,0.75)';
    roundRect(ctx, mx - tw / 2, gy + 9, tw, 20, 10); ctx.fill();
    ctx.fillStyle = '#ffd23f';
    ctx.textBaseline = 'middle';
    ctx.fillText(label, mx, gy + 19);
  }

  _shooter(ctx, s) {
    const sc = this.camera.scale;
    const theta = s.angle * Math.PI / 180;
    const facing = s.angle > 90 ? -1 : 1;
    const [cx, gy] = this.toScreen(s.ax, 0);
    const [px, py] = this.toScreen(s.ax, GEOMETRY.launchHeight);

    // bóng
    ctx.fillStyle = 'rgba(0,0,0,0.25)';
    ctx.beginPath(); ctx.ellipse(cx, gy + 2, 0.3 * sc, 0.06 * sc, 0, 0, TAU); ctx.fill();

    // vòng cung góc + đường ngắm
    const r = 0.75 * sc;
    ctx.strokeStyle = 'rgba(255,255,255,0.55)';
    ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(px, py, r, 0, -theta, true); ctx.stroke();
    ctx.strokeStyle = 'rgba(255,255,255,0.25)';
    ctx.beginPath(); ctx.moveTo(px - r, py); ctx.lineTo(px + r, py); ctx.stroke();

    const muzzleX = s.ax + Math.cos(theta) * GEOMETRY.barrelLength;
    const muzzleY = GEOMETRY.launchHeight + Math.sin(theta) * GEOMETRY.barrelLength;
    const [mx, my] = this.toScreen(muzzleX, muzzleY);
    const aimLen = 1.6 * sc;
    ctx.strokeStyle = '#ffe14d';
    ctx.lineWidth = 2;
    ctx.setLineDash([8, 6]);
    ctx.beginPath(); ctx.moveTo(mx, my); ctx.lineTo(mx + Math.cos(theta) * aimLen, my - Math.sin(theta) * aimLen); ctx.stroke();
    ctx.setLineDash([]);

    // thân nhân vật
    const bw = 0.5 * sc, bh = 0.42 * sc;
    ctx.fillStyle = '#2f80ed';
    ctx.strokeStyle = '#123d7a';
    ctx.lineWidth = 2;
    roundRect(ctx, cx - bw / 2, gy - bh, bw, bh, bh * 0.45); ctx.fill(); ctx.stroke();
    ctx.fillStyle = '#123d7a';
    for (const f of [-0.28, 0.28]) { ctx.beginPath(); ctx.arc(cx + f * bw, gy - 0.03 * sc, 0.07 * sc, 0, TAU); ctx.fill(); }

    // nòng súng: xoay quanh trục (ax, launchHeight), dài đúng barrelLength ⇒ đầu nòng = điểm xuất phát đạn
    ctx.save();
    ctx.translate(px, py);
    ctx.rotate(-theta);
    ctx.fillStyle = '#3b3b48';
    ctx.strokeStyle = '#111';
    ctx.lineWidth = 1.5;
    roundRect(ctx, 0, -0.065 * sc, GEOMETRY.barrelLength * sc, 0.13 * sc, 0.04 * sc); ctx.fill(); ctx.stroke();
    ctx.restore();

    // đầu
    const hr = 0.17 * sc;
    const hx = cx - facing * 0.05 * sc, hy = gy - bh - hr * 0.7;
    ctx.fillStyle = '#ffd7b0';
    ctx.strokeStyle = '#8a5a3c';
    ctx.beginPath(); ctx.arc(hx, hy, hr, 0, TAU); ctx.fill(); ctx.stroke();
    ctx.fillStyle = '#2f80ed';
    ctx.beginPath(); ctx.arc(hx, hy - hr * 0.2, hr * 1.02, Math.PI, 0); ctx.fill();
    ctx.fillStyle = '#222';
    ctx.beginPath(); ctx.arc(hx + facing * hr * 0.45, hy + hr * 0.15, hr * 0.13, 0, TAU); ctx.fill();

    // trục nòng
    ctx.fillStyle = '#ffcf33';
    ctx.beginPath(); ctx.arc(px, py, 0.07 * sc, 0, TAU); ctx.fill();

    // góc hiện tại
    const lx = px + Math.cos(theta / 2) * (r + 0.32 * sc) * (s.angle > 150 ? 0.6 : 1);
    const ly = py - Math.sin(theta / 2) * (r + 0.32 * sc);
    badge(ctx, `${Math.round(s.angle)}°`, lx, ly, '#ffe14d', 15);
    badge(ctx, 'A', cx, hy - hr - 16, '#ffffff', 13, '#2f80ed');
  }

  _target(ctx, s) {
    const sc = this.camera.scale;
    const [cx, gy] = this.toScreen(s.bx, 0);
    const w = GEOMETRY.targetWidth * sc, h = GEOMETRY.targetHeight * sc;

    // hộp va chạm (mờ)
    ctx.strokeStyle = 'rgba(255,255,255,0.35)';
    ctx.setLineDash([4, 4]);
    ctx.lineWidth = 1;
    ctx.strokeRect(cx - w / 2, gy - h, w, h);
    ctx.setLineDash([]);

    // bia tập bắn: chân đế + bảng tròn
    ctx.fillStyle = 'rgba(0,0,0,0.25)';
    ctx.beginPath(); ctx.ellipse(cx, gy + 2, 0.25 * sc, 0.05 * sc, 0, 0, TAU); ctx.fill();
    ctx.fillStyle = '#7a4b22';
    ctx.fillRect(cx - 0.035 * sc, gy - h * 0.55, 0.07 * sc, h * 0.55);
    ctx.fillRect(cx - 0.16 * sc, gy - 0.05 * sc, 0.32 * sc, 0.05 * sc);
    const ry = gy - h * 0.66, rr = w * 0.5;
    const rings = ['#e63946', '#ffffff', '#e63946', '#ffffff', '#e63946'];
    rings.forEach((c, i) => {
      ctx.fillStyle = c;
      ctx.beginPath(); ctx.arc(cx, ry, rr * (1 - i / rings.length), 0, TAU); ctx.fill();
    });
    ctx.strokeStyle = '#7a1f26';
    ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(cx, ry, rr, 0, TAU); ctx.stroke();

    badge(ctx, 'B', cx, gy - h - 18, '#ffffff', 13, '#e63946');
  }

  _trajectory(ctx, pts, originX, o) {
    if (!pts || pts.length < 2) return;
    ctx.save();
    ctx.globalAlpha = o.alpha ?? 1;
    ctx.strokeStyle = o.color;
    ctx.lineWidth = o.width;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    ctx.setLineDash(o.dash ?? []);
    ctx.beginPath();
    let [lx, ly] = this.toScreen(originX + pts[0].x, pts[0].y);
    ctx.moveTo(lx, ly);
    const n = o.upTo ?? pts.length - 1;
    for (let i = 1; i <= n; i++) {
      const [sx, sy] = this.toScreen(originX + pts[i].x, pts[i].y);
      if (i < n && Math.abs(sx - lx) < 1 && Math.abs(sy - ly) < 1) continue;   // bỏ điểm trùng pixel
      ctx.lineTo(sx, sy); lx = sx; ly = sy;
    }
    if (o.tip) ctx.lineTo(...this.toScreen(o.tip.x, o.tip.y));
    ctx.stroke();
    ctx.setLineDash([]);
    if (o.result) {
      const [ix, iy] = this.toScreen(originX + o.result.landingX, o.result.landingY);
      ctx.lineWidth = 2.5;
      ctx.beginPath();
      ctx.moveTo(ix - 5, iy - 5); ctx.lineTo(ix + 5, iy + 5);
      ctx.moveTo(ix + 5, iy - 5); ctx.lineTo(ix - 5, iy + 5);
      ctx.stroke();
    }
    ctx.restore();
  }

  _bullet(ctx, f) {
    // vệt đạn thực tế: các điểm mô phỏng đã đi qua + vị trí nội suy hiện tại
    this._trajectory(ctx, f.points, f.originX, { color: '#ff6b1a', width: 3, upTo: f.index, tip: f.pos });
    const [sx, sy] = this.toScreen(f.pos.x, f.pos.y);
    const inside = sx >= -10 && sx <= this.w + 10 && sy >= -10;
    if (inside) {
      const g = ctx.createRadialGradient(sx, sy, 0, sx, sy, 16);
      g.addColorStop(0, 'rgba(255,230,150,0.9)');
      g.addColorStop(1, 'rgba(255,120,30,0)');
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.arc(sx, sy, 16, 0, TAU); ctx.fill();
      ctx.fillStyle = '#2b2b2b';
      ctx.strokeStyle = '#ffb347';
      ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(sx, sy, 6, 0, TAU); ctx.fill(); ctx.stroke();
    } else {
      // chỉ báo khi đạn ra khỏi khung hình
      const x = Math.min(this.w - 20, Math.max(20, sx));
      ctx.fillStyle = '#ff6b1a';
      ctx.beginPath(); ctx.moveTo(x, 6); ctx.lineTo(x - 9, 22); ctx.lineTo(x + 9, 22); ctx.closePath(); ctx.fill();
      badge(ctx, `↑ ${f.pos.y.toFixed(1)}`, x, 36, '#fff', 12, '#ff6b1a');
    }
  }

  _effects(ctx) {
    const sc = this.camera.scale;
    for (const e of this.effects) {
      const [sx, sy] = this.toScreen(e.x, e.y);
      if (e.kind === 'boom') {
        const k = e.t / 1.1;
        ctx.fillStyle = `rgba(255,${e.hit ? 220 : 160},60,${Math.max(0, 0.8 - k)})`;
        ctx.beginPath(); ctx.arc(sx, sy, (0.15 + 0.5 * Math.min(1, k * 3)) * sc, 0, TAU); ctx.fill();
        for (const p of e.parts) {
          if (p.life <= 0) continue;
          const [px, py] = this.toScreen(p.x, p.y);
          ctx.fillStyle = e.hit ? '#ffd23f' : '#8a5a2b';
          ctx.fillRect(px - 2, py - 2, 4, 4);
        }
      } else {
        const k = e.t / 1.8;
        ctx.globalAlpha = Math.max(0, 1 - k * k);
        badge(ctx, e.text, sx, sy - 40 - k * 40, e.color, 20, 'rgba(20,20,30,0.8)');
        ctx.globalAlpha = 1;
      }
    }
  }

  _wind(ctx, s) {
    const cx = this.w / 2, y = 30;
    ctx.fillStyle = 'rgba(20,30,50,0.65)';
    roundRect(ctx, cx - 95, y - 20, 190, 40, 20); ctx.fill();
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = '800 12px system-ui, sans-serif';
    ctx.fillStyle = '#cfe6ff';
    ctx.fillText('GIÓ', cx - 66, y);
    const active = s.windEnabled && s.wind !== 0;
    if (!s.windEnabled) {
      ctx.fillStyle = '#9fb3c8';
      ctx.font = '700 14px system-ui, sans-serif';
      ctx.fillText('Tắt', cx + 10, y);
      return;
    }
    const len = Math.min(1, Math.abs(s.wind) / 20) * 60;
    ctx.strokeStyle = 'rgba(255,255,255,0.25)';
    ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(cx - 40, y); ctx.lineTo(cx + 40, y); ctx.stroke();
    if (active) {
      const dir = Math.sign(s.wind);
      const x0 = cx - dir * len / 2, x1 = cx + dir * len / 2;
      ctx.strokeStyle = '#ffd23f';
      ctx.fillStyle = '#ffd23f';
      ctx.lineWidth = 5;
      ctx.beginPath(); ctx.moveTo(x0, y); ctx.lineTo(x1, y); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(x1 + dir * 10, y); ctx.lineTo(x1 - dir * 2, y - 8); ctx.lineTo(x1 - dir * 2, y + 8); ctx.closePath(); ctx.fill();
    }
    ctx.fillStyle = '#fff';
    ctx.font = '800 15px system-ui, sans-serif';
    ctx.fillText(Math.abs(s.wind).toFixed(1), cx + 70, y);
  }
}

// ---------------------------------------------------------------- helpers

function roundRect(ctx, x, y, w, h, r) {
  if (w < 0) { x += w; w = -w; }
  r = Math.max(0, Math.min(r, w / 2, h / 2));
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

function badge(ctx, text, x, y, color, size = 13, bg = 'rgba(20,20,30,0.7)') {
  ctx.font = `800 ${size}px system-ui, sans-serif`;
  const w = ctx.measureText(text).width + size;
  const h = size + 10;
  ctx.fillStyle = bg;
  roundRect(ctx, x - w / 2, y - h / 2, w, h, h / 2); ctx.fill();
  ctx.fillStyle = color;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, x, y + 1);
}
