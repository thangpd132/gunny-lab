// =============================================================================
// simulator.js – talks to the C# physics backend and plays a computed trajectory.
// The browser never computes physics: it only interpolates between points returned
// by POST /api/simulation/run for smooth animation.
// =============================================================================

export class ApiError extends Error {
  constructor(message, status) { super(message); this.status = status; }
}

async function request(method, url, body, signal) {
  const res = await fetch(url, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    signal,
  });
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`;
    try {
      const p = await res.json();
      // ASP.NET ValidationProblemDetails: { title, detail, errors: { field: [msgs] } }
      const parts = [];
      if (p.detail) parts.push(p.detail);
      if (p.errors) for (const [k, v] of Object.entries(p.errors)) parts.push(`${k}: ${[].concat(v).join(', ')}`);
      if (parts.length) msg = parts.join(' · ');
      else if (p.title) msg = p.title;
    } catch { /* not JSON */ }
    throw new ApiError(msg, res.status);
  }
  return res.json();
}

export const Api = {
  defaults: () => request('GET', '/api/simulation/defaults'),
  run: (req, signal) => request('POST', '/api/simulation/run', req, signal),
  compare: (req, signal) => request('POST', '/api/simulation/compare', req, signal),
  parameters: () => request('GET', '/api/experiments/parameters'),
  evaluate: (req, signal) => request('POST', '/api/experiments/evaluate', req, signal),
  optimize: (req, signal) => request('POST', '/api/experiments/optimize', req, signal),
};

/**
 * Plays back a trajectory in (scaled) real time with requestAnimationFrame.
 * Position at arbitrary time t is linearly interpolated between the two surrounding samples.
 */
export class ShotAnimator {
  constructor({ onFrame, onFinish, onStateChange }) {
    this.onFrame = onFrame;
    this.onFinish = onFinish;
    this.onStateChange = onStateChange;
    this.state = 'idle';           // idle | playing | paused
    this.result = null;
    this.simTime = 0;
    this.index = 0;
    this.speed = 1;
    this._raf = 0;
    this._last = 0;
  }

  start(result, speed = 1) {
    this.stop(false);
    if (!result?.trajectory?.length) return;
    this.result = result;
    this.speed = speed;
    this.simTime = 0;
    this.index = 0;
    this._setState('playing');
    this._last = performance.now();
    this._raf = requestAnimationFrame(this._tick);
  }

  pause() {
    if (this.state !== 'playing') return;
    cancelAnimationFrame(this._raf);
    this._setState('paused');
  }

  resume() {
    if (this.state !== 'paused') return;
    this._setState('playing');
    this._last = performance.now();
    this._raf = requestAnimationFrame(this._tick);
  }

  /** Abort the animation (no onFinish). */
  stop(notify = true) {
    cancelAnimationFrame(this._raf);
    const was = this.state;
    this.result = null;
    if (was !== 'idle') this._setState('idle', notify);
  }

  setSpeed(s) { this.speed = s; }

  _setState(s, notify = true) {
    this.state = s;
    if (notify) this.onStateChange?.(s);
  }

  _tick = (now) => {
    if (this.state !== 'playing') return;
    // clamp the frame delta so a background tab does not teleport the bullet
    const dt = Math.min(0.1, (now - this._last) / 1000);
    this._last = now;
    this.simTime += dt * this.speed;

    const pts = this.result.trajectory;
    const end = pts[pts.length - 1].t;
    if (this.simTime >= end) {
      const last = pts[pts.length - 1];
      this.onFrame({ x: last.x, y: last.y, vx: last.vx, vy: last.vy, t: last.t, index: pts.length - 1, points: pts });
      const result = this.result;
      this.result = null;
      this._setState('idle');
      this.onFinish?.(result);
      return;
    }
    // advance monotonic index (O(1) amortised per frame)
    while (this.index < pts.length - 2 && pts[this.index + 1].t <= this.simTime) this.index++;
    const a = pts[this.index], b = pts[this.index + 1];
    const f = b.t > a.t ? (this.simTime - a.t) / (b.t - a.t) : 0;
    this.onFrame({
      x: a.x + (b.x - a.x) * f,
      y: a.y + (b.y - a.y) * f,
      vx: a.vx + (b.vx - a.vx) * f,
      vy: a.vy + (b.vy - a.vy) * f,
      t: this.simTime,
      index: this.index,
      points: pts,
    });
    this._raf = requestAnimationFrame(this._tick);
  };
}

/** Debounced "latest wins" runner – used for the live preview while dragging sliders. */
export class LatestRunner {
  constructor(fn, delay = 25) {
    this.fn = fn;
    this.delay = delay;
    this.timer = 0;
    this.controller = null;
    this.seq = 0;
  }

  schedule(arg) {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this._run(arg), this.delay);
  }

  async _run(arg) {
    this.controller?.abort();
    const controller = new AbortController();
    this.controller = controller;
    const seq = ++this.seq;
    try {
      const value = await this.fn(arg, controller.signal);
      return seq === this.seq ? value : undefined;
    } catch (e) {
      if (e.name !== 'AbortError') throw e;
    }
  }
}
