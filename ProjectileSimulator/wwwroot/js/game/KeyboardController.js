// =============================================================================
// KeyboardController.js – điều khiển bằng bàn phím.
//
//   ↑ / ↓      : đổi góc 1° mỗi lần nhấn (giữ phím → lặp theo tốc độ lặp phím của hệ điều hành)
//   ← / →      : di chuyển liên tục khi giữ (GameController đọc isHeld() mỗi frame)
//   Space      : keydown đầu tiên → bắt đầu tích lực; keyup → chốt lực và bắn
//                (keydown lặp lại do giữ phím bị bỏ qua → không bao giờ bắn nhiều lần)
//   Enter      : bắn ngay với lực hiện tại
//   R          : đặt lại
//
// Mọi phím game đều preventDefault ở cả keydown và keyup (capture) để trang không cuộn và
// nút đang được focus không bị "bấm" bởi Space/Enter.
// Khi cửa sổ mất focus (blur / chuyển tab) → nhả hết phím và huỷ tích lực.
// =============================================================================

const GAME_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space', 'Enter', 'KeyR']);

export class KeyboardController {
  /**
   * @param {{
   *   onAngleStep:(dir:number)=>void, onChargeStart:()=>void, onChargeRelease:()=>void,
   *   onFire:()=>void, onReset:()=>void, onFocusLost:()=>void, isEnabled?:()=>boolean
   * }} handlers
   */
  constructor(handlers) {
    this.h = handlers;
    this.held = new Set();
    this.spaceDown = false;

    this._down = this._down.bind(this);
    this._up = this._up.bind(this);
    this._lost = this._lost.bind(this);
    window.addEventListener('keydown', this._down, { capture: true });
    window.addEventListener('keyup', this._up, { capture: true });
    window.addEventListener('blur', this._lost);
    document.addEventListener('visibilitychange', () => { if (document.hidden) this._lost(); });
  }

  isHeld(code) { return this.held.has(code); }

  /** Đang gõ vào ô nhập liệu (Cài đặt) → không chiếm phím. */
  _isTyping(e) {
    const t = e.target;
    if (!(t instanceof HTMLElement)) return false;
    if (t.isContentEditable || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT') return true;
    return t.tagName === 'INPUT' && !['range', 'checkbox', 'button'].includes(t.type);
  }

  _down(e) {
    if (!GAME_KEYS.has(e.code) || this._isTyping(e)) return;
    if (this.h.isEnabled && !this.h.isEnabled()) return;
    e.preventDefault();
    e.stopPropagation();

    switch (e.code) {
      case 'ArrowUp': this.h.onAngleStep(+1); break;          // cả keydown lặp → giữ phím để xoay liên tục
      case 'ArrowDown': this.h.onAngleStep(-1); break;
      case 'ArrowLeft':
      case 'ArrowRight': this.held.add(e.code); break;
      case 'Space':
        if (e.repeat || this.spaceDown) return;                // chặn keydown lặp lại
        this.spaceDown = true;
        this.h.onChargeStart();
        break;
      case 'Enter': if (!e.repeat) this.h.onFire(); break;
      case 'KeyR': if (!e.repeat) this.h.onReset(); break;
    }
  }

  _up(e) {
    if (!GAME_KEYS.has(e.code) || this._isTyping(e)) return;
    e.preventDefault();
    e.stopPropagation();
    this.held.delete(e.code);
    if (e.code === 'Space' && this.spaceDown) {
      this.spaceDown = false;
      this.h.onChargeRelease();
    }
  }

  _lost() {
    this.held.clear();
    if (this.spaceDown) {
      this.spaceDown = false;
      this.h.onFocusLost();
    }
  }
}
