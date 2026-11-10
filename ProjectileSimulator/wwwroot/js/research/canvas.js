// =============================================================================
// canvas.js – rendering of the simulation scene on an HTML5 Canvas 2D.
//
// COORDINATE SYSTEMS
//   Physics / world space : X → right, Y ↑ UP,   unit = "u" (one Distance step)
//   Canvas / screen space : X → right, Y ↓ DOWN, unit = CSS pixel
//
//   screenX = (worldX − view.x0) · scale
//   screenY = height − (worldY − view.y0) · scale        ← the Y flip happens ONLY here
//
// view.x0 / view.y0 are the world coordinates of the bottom-left corner of the canvas.
// The same scale is used on both axes so angles and parabolas are not distorted.
//
// PERFORMANCE
//   Everything that does not move (sky, grid, ground, characters, stored trajectories…) is drawn
//   into an off-screen "static layer" only when the scene or the view changes. During the
//   firing animation each frame just blits that layer and draws the projectile + trail.
// =============================================================================

const TAU = Math.PI * 2;

export class SimCanvas {
  /**
   * @param {HTMLCanvasElement} canvas
   * @param {{onTargetDrag?:(x:number)=>void, onHover?:(p:{x:number,y:number}|null)=>void}} hooks
   */
  constructor(canvas, hooks = {}) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.hooks = hooks;
    this.static = document.createElement('canvas');
    this.sctx = this.static.getContext('2d');
    this.dpr = 1;
    this.w = 1;
    this.h = 1;
    this.view = { x0: -1.5, y0: -1, scale: 60 };
    this.userView = false;          // true once the user zoomed / panned
    this.staticDirty = true;
    this.frameRequested = false;
    this.effects = [];
    this.options = { grid: true, labels: true, aim: true };

    /** Scene description – set via setScene(). */
    this.scene = {
      distance: 5, angle: 45, power: 50, wind: 0, windEnabled: true, windMode: 'WindAcceleration',
      launchHeight: 0.4, barrelLength: 0.3, targetWidth: 0.4, targetHeight: 0.6, groundLevel: 0,
      trajectories: [],   // [{id, points, color, label, width, dash, alpha, result}]
      preview: null,      // {points, result}
      observations: [],   // [{x, y, label, px?, py?}]
      projectile: null,   // {x, y, trail:[{X,Y}], color}
    };

    this._bindInteraction();
    new ResizeObserver(() => this.resize()).observe(canvas.parentElement);
    this.resize();
  }

  // ------------------------------------------------------------------ public API

  setScene(patch) {
    Object.assign(this.scene, patch);
    this.invalidate();
  }

  setOptions(patch) {
    Object.assign(this.options, patch);
    this.invalidate();
  }

  /** Redraw the static layer at the next animation frame. */
  invalidate() {
    this.staticDirty = true;
    this.requestRender();
  }

  /** Only the dynamic layer changed (projectile position). */
  requestRender() {
    if (this.frameRequested) return;
    this.frameRequested = true;
    requestAnimationFrame(() => {
      this.frameRequested = false;
      this.render();
    });
  }

  setProjectile(p) {
    this.scene.projectile = p;
    this.requestRender();
  }

  addImpactEffect(x, y, hit) {
    this.effects.push({ x, y, hit, t0: performance.now() });
    this.requestRender();
  }

  resetView() {
    this.userView = false;
    this._applyDefaultView();
    this.invalidate();
  }

  /** Zoom to include every stored trajectory, the preview, both characters and observations. */
  fitView() {
    const b = this._contentBounds(true);
    this._fitBounds(b, 0.08);
    this.userView = true;
    this.invalidate();
  }

  /** Called after new trajectories were committed: grow the default view if needed. */
  autoFrame() {
    if (!this.userView) {
      this._applyDefaultView();
      this.invalidate();
    }
  }

  toScreen(x, y) {
    const v = this.view;
    return [(x - v.x0) * v.scale, this.h - (y - v.y0) * v.scale];
  }

  toWorld(sx, sy) {
    const v = this.view;
    return [sx / v.scale + v.x0, (this.h - sy) / v.scale + v.y0];
  }

  // ------------------------------------------------------------------ view handling

  resize() {
    const rect = this.canvas.parentElement.getBoundingClientRect();
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = Math.max(50, Math.floor(rect.width));
    const h = Math.max(50, Math.floor(rect.height));
    if (w === this.w && h === this.h && dpr === this.dpr) return;
    this.w = w; this.h = h; this.dpr = dpr;
    for (const c of [this.canvas, this.static]) {
      c.width = Math.floor(w * dpr);
      c.height = Math.floor(h * dpr);
    }
    this.canvas.style.width = w + 'px';
    this.canvas.style.height = h + 'px';
    if (!this.userView) this._applyDefaultView();
    this.invalidate();
  }

  _applyDefaultView() {
    // Default: shooter and a target at distance 10 are both comfortably visible,
    // expanded to include every committed trajectory.
    const s = this.scene;
    const b = { minX: -1.2, maxX: Math.max(11.2, s.distance + 1.2), minY: s.groundLevel - 0.9, maxY: s.groundLevel + 4.5 };
    const t = this._contentBounds(false);
    if (t) {
      b.minX = Math.min(b.minX, t.minX - 0.5);
      b.maxX = Math.max(b.maxX, t.maxX + 0.5);
      b.maxY = Math.max(b.maxY, t.maxY + 0.6);
      b.minY = Math.min(b.minY, t.minY - 0.3);
    }
    this._fitBounds(b, 0, true);
  }

  _fitBounds(b, margin, anchorBottom = false) {
    const wx = (b.maxX - b.minX) * (1 + 2 * margin);
    const wy = (b.maxY - b.minY) * (1 + 2 * margin);
    const scale = Math.max(2, Math.min(this.w / wx, this.h / wy));
    const cx = (b.minX + b.maxX) / 2;
    this.view.scale = scale;
    this.view.x0 = cx - this.w / scale / 2;
    if (anchorBottom) this.view.y0 = b.minY;
    else this.view.y0 = (b.minY + b.maxY) / 2 - this.h / scale / 2;
  }

  _contentBounds(includeAll) {
    const s = this.scene;
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    const add = (x, y) => {
      if (!isFinite(x) || !isFinite(y)) return;
      if (x < minX) minX = x; if (x > maxX) maxX = x;
      if (y < minY) minY = y; if (y > maxY) maxY = y;
    };
    const lists = s.trajectories.map(t => t.points);
    if (includeAll && s.preview) lists.push(s.preview.points);
    for (const pts of lists) {
      if (!pts) continue;
      const step = Math.max(1, Math.floor(pts.length / 400));
      for (let i = 0; i < pts.length; i += step) add(pts[i].x, pts[i].y);
      if (pts.length) add(pts[pts.length - 1].x, pts[pts.length - 1].y);
    }
    if (includeAll) {
      for (const o of s.observations) { add(o.x, o.y); if (o.px != null) add(o.px, o.py); }
      add(-1, s.groundLevel - 0.5);
      add(s.distance + 1, s.groundLevel + 2);
    }
    if (!isFinite(minX)) return null;
    // Clamp crazy trajectories (out-of-bounds shots) so the view stays usable.
    return { minX: Math.max(minX, -600), maxX: Math.min(maxX, 600), minY: Math.max(minY, -600), maxY: Math.min(maxY, 600) };
  }

  _bindInteraction() {
    const c = this.canvas;
    let drag = null;

    c.addEventListener('wheel', (e) => {
      e.preventDefault();
      const r = c.getBoundingClientRect();
      const sx = e.clientX - r.left, sy = e.clientY - r.top;
      const [wx, wy] = this.toWorld(sx, sy);
      const factor = Math.pow(1.0015, -e.deltaY);
      const scale = Math.min(2000, Math.max(1, this.view.scale * factor));
      this.view.scale = scale;
      // keep the world point under the cursor fixed
      this.view.x0 = wx - sx / scale;
      this.view.y0 = wy - (this.h - sy) / scale;
      this.userView = true;
      this.invalidate();
    }, { passive: false });

    c.addEventListener('pointerdown', (e) => {
      const r = c.getBoundingClientRect();
      const sx = e.clientX - r.left, sy = e.clientY - r.top;
      c.setPointerCapture(e.pointerId);
      if (this._isOverTarget(sx, sy) && this.hooks.onTargetDrag) {
        drag = { mode: 'target' };
      } else {
        drag = { mode: 'pan', sx, sy, x0: this.view.x0, y0: this.view.y0 };
        c.classList.add('grab');
      }
    });

    c.addEventListener('pointermove', (e) => {
      const r = c.getBoundingClientRect();
      const sx = e.clientX - r.left, sy = e.clientY - r.top;
      const [wx, wy] = this.toWorld(sx, sy);
      this.hooks.onHover?.({ x: wx, y: wy });
      if (!drag) {
        c.classList.toggle('can-drag', this._isOverTarget(sx, sy));
        return;
      }
      if (drag.mode === 'pan') {
        this.view.x0 = drag.x0 - (sx - drag.sx) / this.view.scale;
        this.view.y0 = drag.y0 + (sy - drag.sy) / this.view.scale;
        this.userView = true;
        this.invalidate();
      } else if (drag.mode === 'target') {
        this.hooks.onTargetDrag(wx);
      }
    });

    const end = (e) => {
      if (drag && c.hasPointerCapture?.(e.pointerId)) c.releasePointerCapture(e.pointerId);
      drag = null;
      c.classList.remove('grab');
    };
    c.addEventListener('pointerup', end);
    c.addEventListener('pointercancel', end);
    c.addEventListener('pointerleave', () => this.hooks.onHover?.(null));
    c.addEventListener('dblclick', () => this.resetView());
  }

  _isOverTarget(sx, sy) {
    const s = this.scene;
    const [wx, wy] = this.toWorld(sx, sy);
    const pad = 12 / this.view.scale;
    return Math.abs(wx - s.distance) <= s.targetWidth / 2 + pad &&
      wy >= s.groundLevel - pad && wy <= s.groundLevel + s.targetHeight + 0.35 + pad;
  }

  // ------------------------------------------------------------------ rendering

  render() {
    const ctx = this.ctx;
    if (this.staticDirty) {
      this.sctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
      this._drawStatic(this.sctx);
      this.staticDirty = false;
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.drawImage(this.static, 0, 0);
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    this._drawDynamic(ctx);
  }

  _drawStatic(ctx) {
    const s = this.scene;
    ctx.clearRect(0, 0, this.w, this.h);

    // sky
    const [, groundSy] = this.toScreen(0, s.groundLevel);
    const sky = ctx.createLinearGradient(0, 0, 0, Math.max(1, groundSy));
    sky.addColorStop(0, '#07101f');
    sky.addColorStop(1, '#16304d');
    ctx.fillStyle = sky;
    ctx.fillRect(0, 0, this.w, this.h);

    if (this.options.grid) this._drawGrid(ctx);
    this._drawGround(ctx);
    this._drawDistanceRuler(ctx);

    // stored trajectories (oldest first so the newest is on top)
    for (const t of s.trajectories) this._drawTrajectory(ctx, t);
    if (s.preview) this._drawTrajectory(ctx, {
      points: s.preview.points, result: s.preview.result, color: '#e6f1ff', dash: [6, 6], width: 1.6, alpha: 0.75, preview: true
    });

    this._drawObservations(ctx);
    this._drawTarget(ctx);
    this._drawShooter(ctx);
    this._drawWindIndicator(ctx);
    this._drawScaleBar(ctx);
  }

  _gridStep() {
    const candidates = [0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10, 20, 25, 50, 100, 200, 500];
    for (const c of candidates) if (c * this.view.scale >= 55) return c;
    return 1000;
  }

  _drawGrid(ctx) {
    const v = this.view;
    const major = this._gridStep();
    const minor = major / 5;
    const [wx0, wy1] = this.toWorld(0, 0);
    const [wx1, wy0] = this.toWorld(this.w, this.h);

    ctx.lineWidth = 1;
    // minor lines
    if (minor * v.scale >= 8) {
      ctx.strokeStyle = 'rgba(120,180,255,0.06)';
      ctx.beginPath();
      for (let x = Math.ceil(wx0 / minor) * minor; x <= wx1; x += minor) {
        const sx = Math.round((x - v.x0) * v.scale) + 0.5;
        ctx.moveTo(sx, 0); ctx.lineTo(sx, this.h);
      }
      for (let y = Math.ceil(wy0 / minor) * minor; y <= wy1; y += minor) {
        const sy = Math.round(this.h - (y - v.y0) * v.scale) + 0.5;
        ctx.moveTo(0, sy); ctx.lineTo(this.w, sy);
      }
      ctx.stroke();
    }
    // major lines
    ctx.strokeStyle = 'rgba(120,180,255,0.16)';
    ctx.beginPath();
    for (let x = Math.ceil(wx0 / major) * major; x <= wx1; x += major) {
      const sx = Math.round((x - v.x0) * v.scale) + 0.5;
      ctx.moveTo(sx, 0); ctx.lineTo(sx, this.h);
    }
    for (let y = Math.ceil(wy0 / major) * major; y <= wy1; y += major) {
      const sy = Math.round(this.h - (y - v.y0) * v.scale) + 0.5;
      ctx.moveTo(0, sy); ctx.lineTo(this.w, sy);
    }
    ctx.stroke();

    // axis labels
    if (!this.options.labels) return;
    ctx.fillStyle = 'rgba(170,200,240,0.55)';
    ctx.font = '10px ui-monospace, monospace';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'top';
    const fmt = (n) => (Math.abs(n) < 1e-9 ? '0' : +n.toFixed(3) + '');
    for (let y = Math.ceil(wy0 / major) * major; y <= wy1; y += major) {
      if (Math.abs(y - this.scene.groundLevel) < 1e-9) continue;
      const [, sy] = this.toScreen(0, y);
      ctx.fillText('y ' + fmt(y), 4, sy + 2);
    }
  }

  _drawGround(ctx) {
    const s = this.scene;
    const [, gy] = this.toScreen(0, s.groundLevel);
    if (gy > this.h) return;
    const top = Math.max(0, gy);
    const g = ctx.createLinearGradient(0, top, 0, this.h);
    g.addColorStop(0, '#3b5a2a');
    g.addColorStop(0.08, '#2c3f22');
    g.addColorStop(1, '#1b2216');
    ctx.fillStyle = g;
    ctx.fillRect(0, top, this.w, this.h - top);
    ctx.strokeStyle = '#7fc35a';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(0, gy); ctx.lineTo(this.w, gy);
    ctx.stroke();

    // ruler ticks on the ground surface every unit (0.5 minor) — distances visible at a glance
    const v = this.view;
    const [wx0] = this.toWorld(0, 0);
    const [wx1] = this.toWorld(this.w, 0);
    const step = v.scale >= 30 ? 0.5 : (v.scale >= 8 ? 1 : 5);
    ctx.font = '10px ui-monospace, monospace';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    for (let x = Math.ceil(wx0 / step) * step; x <= wx1; x += step) {
      const sx = (x - v.x0) * v.scale;
      const isMajor = Math.abs(x - Math.round(x)) < 1e-9 && (step < 5 || Math.round(x) % 5 === 0);
      ctx.strokeStyle = isMajor ? 'rgba(220,255,200,0.6)' : 'rgba(220,255,200,0.25)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(sx, gy); ctx.lineTo(sx, gy + (isMajor ? 8 : 4));
      ctx.stroke();
      if (isMajor && this.options.labels) {
        ctx.fillStyle = 'rgba(220,255,200,0.7)';
        ctx.fillText(String(Math.round(x)), sx, gy + 10);
      }
    }
  }

  _drawDistanceRuler(ctx) {
    const s = this.scene;
    const y = s.groundLevel;
    const [x0, gy] = this.toScreen(0, y);
    const [x1] = this.toScreen(s.distance, y);
    const ry = gy + 30;
    if (ry > this.h - 4) return;
    ctx.strokeStyle = '#39d0ff';
    ctx.fillStyle = '#39d0ff';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.moveTo(x0, ry); ctx.lineTo(x1, ry);
    ctx.moveTo(x0, ry - 6); ctx.lineTo(x0, ry + 6);
    ctx.moveTo(x1, ry - 6); ctx.lineTo(x1, ry + 6);
    ctx.stroke();
    arrowHead(ctx, x1, ry, 0, 7);
    arrowHead(ctx, x0, ry, Math.PI, 7);
    const label = `khoảng cách = ${s.distance.toFixed(1)} u`;
    ctx.font = '600 11px ui-monospace, monospace';
    const tw = ctx.measureText(label).width + 10;
    const mx = (x0 + x1) / 2;
    ctx.fillStyle = 'rgba(8,16,28,0.9)';
    ctx.fillRect(mx - tw / 2, ry - 8, tw, 16);
    ctx.fillStyle = '#39d0ff';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(label, mx, ry);
  }

  _drawShooter(ctx) {
    const s = this.scene;
    const sc = this.view.scale;
    const theta = (s.angle) * Math.PI / 180;
    const [px, py] = this.toScreen(0, s.launchHeight);
    const [gx, gy] = this.toScreen(0, s.groundLevel);
    const unit = sc;                       // px per u
    const bodyW = 0.42 * unit, bodyH = Math.max(6, Math.min(0.24 * unit, Math.abs(gy - py) + 0.12 * unit));

    // support post from ground to pivot (shows the launch height)
    ctx.strokeStyle = 'rgba(57,208,255,0.35)';
    ctx.setLineDash([3, 3]);
    ctx.beginPath(); ctx.moveTo(gx, gy); ctx.lineTo(px, py); ctx.stroke();
    ctx.setLineDash([]);

    // tank body
    ctx.save();
    ctx.fillStyle = '#1c6f8f';
    ctx.strokeStyle = '#39d0ff';
    ctx.lineWidth = 1.5;
    roundRect(ctx, gx - bodyW / 2, gy - bodyH, bodyW, bodyH, Math.min(6, bodyH / 2));
    ctx.fill(); ctx.stroke();
    // wheels
    ctx.fillStyle = '#0d2b38';
    for (const f of [-0.3, 0, 0.3]) {
      ctx.beginPath();
      ctx.arc(gx + f * bodyW, gy - 2, Math.max(2, bodyH * 0.18), 0, TAU);
      ctx.fill();
    }
    ctx.restore();

    // aim line + angle arc
    const v = Vector(theta);
    if (this.options.aim) {
      const aimLen = (0.6 + 2.4 * Math.min(1, s.power / 100)) * unit;
      ctx.strokeStyle = 'rgba(255,214,102,0.8)';
      ctx.setLineDash([5, 5]);
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.moveTo(px, py);
      ctx.lineTo(px + v.x * aimLen, py - v.y * aimLen);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = 'rgba(255,214,102,0.9)';
      arrowHead(ctx, px + v.x * aimLen, py - v.y * aimLen, -theta, 7);

      const r = Math.max(18, 0.5 * unit);
      ctx.strokeStyle = 'rgba(255,214,102,0.45)';
      ctx.beginPath();
      ctx.arc(px, py, r, 0, -theta, true);   // canvas angles are clockwise → negate
      ctx.stroke();
      if (this.options.labels) {
        ctx.fillStyle = '#ffd666';
        ctx.font = '600 11px ui-monospace, monospace';
        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';
        const lv = Vector(theta / 2);
        ctx.fillText(`θ ${s.angle.toFixed(1)}°`, px + lv.x * (r + 6), py - lv.y * (r + 6));
      }
    }

    // barrel (rotates with the effective angle)
    const bl = Math.max(0.12, s.barrelLength) * unit;
    ctx.save();
    ctx.translate(px, py);
    ctx.rotate(-theta);
    ctx.fillStyle = '#9be7ff';
    ctx.strokeStyle = '#0b3446';
    ctx.lineWidth = 1;
    const bw = Math.max(4, 0.07 * unit);
    roundRect(ctx, 0, -bw / 2, bl, bw, bw / 3);
    ctx.fill(); ctx.stroke();
    ctx.restore();
    // turret
    ctx.fillStyle = '#39d0ff';
    ctx.beginPath(); ctx.arc(px, py, Math.max(4, 0.09 * unit), 0, TAU); ctx.fill();

    // muzzle (launch point)
    const [mx, my] = this.toScreen(Math.cos(theta) * s.barrelLength, Math.max(s.groundLevel, s.launchHeight + Math.sin(theta) * s.barrelLength));
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.arc(mx, my, 3.5, 0, TAU); ctx.stroke();

    if (this.options.labels) label(ctx, 'A · người bắn', gx, gy - bodyH - 10, '#39d0ff');
  }

  _drawTarget(ctx) {
    const s = this.scene;
    const [cx, gy] = this.toScreen(s.distance, s.groundLevel);
    const w = s.targetWidth * this.view.scale;
    const h = s.targetHeight * this.view.scale;

    // hit box
    ctx.strokeStyle = 'rgba(255,120,120,0.55)';
    ctx.setLineDash([4, 3]);
    ctx.lineWidth = 1;
    ctx.strokeRect(cx - w / 2, gy - h, w, h);
    ctx.setLineDash([]);

    // character
    const bw = w * 0.8, bh = h * 0.62;
    ctx.fillStyle = '#8f2b2b';
    ctx.strokeStyle = '#ff7a7a';
    ctx.lineWidth = 1.5;
    roundRect(ctx, cx - bw / 2, gy - bh, bw, bh, Math.min(8, bw / 3));
    ctx.fill(); ctx.stroke();
    const hr = Math.min(bw * 0.38, h * 0.2);
    ctx.fillStyle = '#d65c5c';
    ctx.beginPath(); ctx.arc(cx, gy - bh - hr * 0.85, hr, 0, TAU); ctx.fill(); ctx.stroke();
    // eyes looking at the shooter
    ctx.fillStyle = '#fff';
    ctx.beginPath();
    ctx.arc(cx - hr * 0.4, gy - bh - hr * 0.95, Math.max(1, hr * 0.18), 0, TAU);
    ctx.arc(cx + hr * 0.05, gy - bh - hr * 0.95, Math.max(1, hr * 0.18), 0, TAU);
    ctx.fill();
    // centre mark
    ctx.strokeStyle = 'rgba(255,255,255,0.4)';
    ctx.beginPath(); ctx.moveTo(cx, gy); ctx.lineTo(cx, gy - h); ctx.stroke();

    if (this.options.labels) label(ctx, 'B · mục tiêu', cx, gy - h - hr * 2 - 8, '#ff8a8a');
  }

  _drawTrajectory(ctx, t) {
    const pts = t.points;
    if (!pts || pts.length < 1) return;
    ctx.save();
    ctx.globalAlpha = t.alpha ?? 1;
    ctx.strokeStyle = t.color;
    ctx.lineWidth = t.width ?? 2;
    ctx.lineJoin = 'round';
    ctx.setLineDash(t.dash ?? []);
    ctx.beginPath();
    // Skip points closer than ~0.75px on screen: same picture, far fewer path segments.
    let [lx, ly] = this.toScreen(pts[0].x, pts[0].y);
    ctx.moveTo(lx, ly);
    const last = pts.length - 1;
    for (let i = 1; i <= last; i++) {
      const [sx, sy] = this.toScreen(pts[i].x, pts[i].y);
      if (i !== last && Math.abs(sx - lx) < 0.75 && Math.abs(sy - ly) < 0.75) continue;
      ctx.lineTo(sx, sy);
      lx = sx; ly = sy;
    }
    ctx.stroke();
    ctx.setLineDash([]);

    const r = t.result;
    if (r) {
      // apex
      const [ax, ay] = this.toScreen(r.maxHeightX, r.maxHeight + this.scene.groundLevel);
      ctx.fillStyle = t.color;
      ctx.beginPath(); ctx.arc(ax, ay, 2.5, 0, TAU); ctx.fill();
      if (this.options.labels && !t.preview && !t.compact) {
        ctx.font = '10px ui-monospace, monospace';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'bottom';
        ctx.fillText(`H ${r.maxHeight.toFixed(2)}`, ax, ay - 4);
      }
      // launch point
      const [lx0, ly0] = this.toScreen(r.launchX, r.launchY);
      ctx.beginPath(); ctx.arc(lx0, ly0, 2.5, 0, TAU); ctx.fill();
      // landing / impact
      const [ix, iy] = this.toScreen(r.landingX, r.landingY);
      if (r.status === 'HitTarget') starBurst(ctx, ix, iy, 7, t.color);
      else if (r.status === 'HitGround') cross(ctx, ix, iy, 5, t.color);
      if (this.options.labels && !t.compact) {
        ctx.font = '600 10px ui-monospace, monospace';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.fillStyle = t.color;
        const txt = (t.preview ? 'xem trước ' : '') + `x=${r.landingX.toFixed(2)}`;
        ctx.fillText(txt, ix, iy + (t.preview ? 18 : 8));
      }
    }
    ctx.restore();
  }

  _drawObservations(ctx) {
    for (const o of this.scene.observations) {
      const [ox, oy] = this.toScreen(o.x, o.y);
      if (o.px != null) {
        const [px, py] = this.toScreen(o.px, o.py);
        ctx.strokeStyle = 'rgba(251,191,36,0.6)';
        ctx.setLineDash([2, 2]);
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(ox, oy); ctx.lineTo(px, py); ctx.stroke();
        ctx.setLineDash([]);
        ctx.strokeStyle = '#e879f9';
        ctx.lineWidth = 1.5;
        ctx.beginPath(); ctx.arc(px, py, 4, 0, TAU); ctx.stroke();
      }
      ctx.fillStyle = '#fbbf24';
      ctx.strokeStyle = '#3a2a00';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(ox, oy - 6); ctx.lineTo(ox + 5, oy); ctx.lineTo(ox, oy + 6); ctx.lineTo(ox - 5, oy);
      ctx.closePath(); ctx.fill(); ctx.stroke();
      if (this.options.labels && o.label) {
        ctx.fillStyle = '#fbbf24';
        ctx.font = '10px ui-monospace, monospace';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'bottom';
        ctx.fillText(o.label, ox, oy - 8);
      }
    }
  }

  _drawWindIndicator(ctx) {
    const s = this.scene;
    const cx = this.w / 2, cy = 26;
    const boxW = 220, boxH = 38;
    ctx.fillStyle = 'rgba(8,16,28,0.78)';
    ctx.strokeStyle = 'rgba(57,208,255,0.35)';
    ctx.lineWidth = 1;
    roundRect(ctx, cx - boxW / 2, cy - boxH / 2 + 2, boxW, boxH, 6);
    ctx.fill(); ctx.stroke();

    ctx.font = '600 10px ui-monospace, monospace';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';
    ctx.fillStyle = '#7d8ba3';
    ctx.fillText('GIÓ', cx - boxW / 2 + 8, cy - 6);
    ctx.fillText(s.windMode === 'ConstantWind' ? 'trôi' : 'gia tốc', cx - boxW / 2 + 8, cy + 8);

    const active = s.windEnabled && s.wind !== 0;
    const maxLen = 70;
    const len = Math.min(1, Math.abs(s.wind) / 20) * maxLen;
    const dir = Math.sign(s.wind);
    const ay = cy + 2;
    ctx.strokeStyle = 'rgba(255,255,255,0.15)';
    ctx.beginPath(); ctx.moveTo(cx - maxLen, ay); ctx.lineTo(cx + maxLen, ay); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(cx, ay - 5); ctx.lineTo(cx, ay + 5); ctx.stroke();
    if (active && len > 0) {
      const col = Math.abs(s.wind) > 12 ? '#f87171' : (Math.abs(s.wind) > 6 ? '#fbbf24' : '#4ade80');
      ctx.strokeStyle = col;
      ctx.fillStyle = col;
      ctx.lineWidth = 4;
      ctx.beginPath(); ctx.moveTo(cx, ay); ctx.lineTo(cx + dir * len, ay); ctx.stroke();
      arrowHead(ctx, cx + dir * (len + 4), ay, dir > 0 ? 0 : Math.PI, 10);
    }
    ctx.font = '600 12px ui-monospace, monospace';
    ctx.textAlign = 'right';
    ctx.fillStyle = s.windEnabled ? '#dbe6f5' : '#7d8ba3';
    ctx.fillText(s.windEnabled ? (s.wind > 0 ? '+' : '') + s.wind.toFixed(1) : 'TẮT', cx + boxW / 2 - 8, cy + 2);
  }

  _drawScaleBar(ctx) {
    const step = this._gridStep();
    const px = step * this.view.scale;
    const x = 12, y = this.h - 34;
    ctx.strokeStyle = '#dbe6f5';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(x, y - 4); ctx.lineTo(x, y); ctx.lineTo(x + px, y); ctx.lineTo(x + px, y - 4);
    ctx.stroke();
    ctx.fillStyle = '#dbe6f5';
    ctx.font = '10px ui-monospace, monospace';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'bottom';
    ctx.fillText(`${step} u = ${px.toFixed(0)} px`, x, y - 5);
  }

  _drawDynamic(ctx) {
    const p = this.scene.projectile;
    if (p) {
      // trail
      if (p.trail && p.trail.length > 1) {
        ctx.strokeStyle = p.color || '#ff7a3d';
        ctx.lineWidth = 2.5;
        ctx.lineJoin = 'round';
        ctx.beginPath();
        let [lx, ly] = this.toScreen(p.trail[0].x, p.trail[0].y);
        ctx.moveTo(lx, ly);
        for (let i = 1; i < p.trail.length; i++) {
          const [sx, sy] = this.toScreen(p.trail[i].x, p.trail[i].y);
          if (Math.abs(sx - lx) < 0.75 && Math.abs(sy - ly) < 0.75 && i < p.trail.length - 1) continue;
          ctx.lineTo(sx, sy); lx = sx; ly = sy;
        }
        ctx.lineTo(...this.toScreen(p.x, p.y));
        ctx.stroke();
      }
      const [sx, sy] = this.toScreen(p.x, p.y);
      const glow = ctx.createRadialGradient(sx, sy, 0, sx, sy, 14);
      glow.addColorStop(0, 'rgba(255,200,120,0.95)');
      glow.addColorStop(1, 'rgba(255,122,61,0)');
      ctx.fillStyle = glow;
      ctx.beginPath(); ctx.arc(sx, sy, 14, 0, TAU); ctx.fill();
      ctx.fillStyle = '#fff3e0';
      ctx.beginPath(); ctx.arc(sx, sy, 4, 0, TAU); ctx.fill();
      // velocity vector
      if (p.vx != null) {
        const k = 0.12 * this.view.scale / 10;
        ctx.strokeStyle = 'rgba(255,255,255,0.7)';
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(sx, sy); ctx.lineTo(sx + p.vx * k * 3, sy - p.vy * k * 3); ctx.stroke();
      }
      if (this.options.labels && p.t != null) {
        ctx.font = '10px ui-monospace, monospace';
        ctx.fillStyle = '#ffd6b0';
        ctx.textAlign = 'left';
        ctx.textBaseline = 'bottom';
        ctx.fillText(`t=${p.t.toFixed(3)}s  (${p.x.toFixed(2)}, ${p.y.toFixed(2)})`, sx + 10, sy - 8);
      }
    }

    // impact effects (expanding rings)
    const now = performance.now();
    this.effects = this.effects.filter(e => now - e.t0 < 700);
    for (const e of this.effects) {
      const k = (now - e.t0) / 700;
      const [sx, sy] = this.toScreen(e.x, e.y);
      ctx.strokeStyle = e.hit ? `rgba(74,222,128,${1 - k})` : `rgba(255,170,90,${1 - k})`;
      ctx.lineWidth = 3 * (1 - k) + 1;
      ctx.beginPath(); ctx.arc(sx, sy, 6 + 40 * k, 0, TAU); ctx.stroke();
    }
    if (this.effects.length) this.requestRender();
  }
}

// ---------------------------------------------------------------- drawing helpers

function Vector(theta) { return { x: Math.cos(theta), y: Math.sin(theta) }; }

function roundRect(ctx, x, y, w, h, r) {
  r = Math.max(0, Math.min(r, Math.abs(w) / 2, Math.abs(h) / 2));
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

/** Arrow head at (x,y) pointing at screen angle a (radians, clockwise positive). */
function arrowHead(ctx, x, y, a, size) {
  ctx.beginPath();
  ctx.moveTo(x, y);
  ctx.lineTo(x - size * Math.cos(a - 0.45), y - size * Math.sin(a - 0.45));
  ctx.lineTo(x - size * Math.cos(a + 0.45), y - size * Math.sin(a + 0.45));
  ctx.closePath();
  ctx.fill();
}

function cross(ctx, x, y, r, color) {
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(x - r, y - r); ctx.lineTo(x + r, y + r);
  ctx.moveTo(x + r, y - r); ctx.lineTo(x - r, y + r);
  ctx.stroke();
}

function starBurst(ctx, x, y, r, color) {
  ctx.fillStyle = color;
  ctx.beginPath();
  for (let i = 0; i < 16; i++) {
    const a = i * Math.PI / 8;
    const rr = i % 2 ? r * 0.45 : r;
    ctx.lineTo(x + Math.cos(a) * rr, y + Math.sin(a) * rr);
  }
  ctx.closePath();
  ctx.fill();
  ctx.strokeStyle = '#fff';
  ctx.lineWidth = 1;
  ctx.stroke();
}

function label(ctx, text, x, y, color) {
  ctx.font = '600 11px system-ui, sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'bottom';
  const w = ctx.measureText(text).width + 10;
  ctx.fillStyle = 'rgba(8,16,28,0.75)';
  ctx.fillRect(x - w / 2, y - 15, w, 15);
  ctx.fillStyle = color;
  ctx.fillText(text, x, y - 2);
}
