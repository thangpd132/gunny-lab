// =============================================================================
// PowerController.js – cơ chế tích lực.
//
//   startCharge()  : lực về 0 % và bắt đầu tăng (chỉ khi đang giữ Space)
//   update(dt)     : gọi mỗi frame; lực chỉ tăng khi charging = true, dừng ở 100 %
//   release()      : chốt lực tại đúng thời điểm thả phím, trả về giá trị đã chốt
//   cancel()       : huỷ tích lực (mất focus cửa sổ) – không bắn
//   set(v)         : đặt lực thủ công (kéo thanh Power)
// =============================================================================

export class PowerController {
  /** @param {{fillTime?:number, onChange?:(p:PowerController)=>void}} opts */
  constructor({ fillTime = 2, onChange } = {}) {
    this.fillTime = fillTime;   // số giây để lực đi từ 0 % lên 100 %
    this.onChange = onChange;
    this.value = 0;             // 0–100
    this.charging = false;
    this.lastShot = null;       // lực của phát bắn trước – hiển thị vạch đánh dấu trên thanh
  }

  startCharge() {
    if (this.charging) return false;
    this.charging = true;
    this.value = 0;
    this._emit();
    return true;
  }

  update(dt) {
    if (!this.charging) return;
    const before = this.value;
    this.value = Math.min(100, this.value + (100 / this.fillTime) * dt);
    if (this.value !== before) this._emit();
  }

  /** @returns {number|null} lực đã chốt, hoặc null nếu không đang tích lực */
  release() {
    if (!this.charging) return null;
    this.charging = false;
    this.value = round1(this.value);
    this._emit();
    return this.value;
  }

  cancel() {
    if (!this.charging) return false;
    this.charging = false;
    this._emit();
    return true;
  }

  set(v) {
    if (this.charging) return;
    this.value = round1(Math.min(100, Math.max(0, v)));
    this._emit();
  }

  markShot(v) {
    this.lastShot = v;
    this._emit();
  }

  reset() {
    this.charging = false;
    this.value = 0;
    this.lastShot = null;
    this._emit();
  }

  _emit() { this.onChange?.(this); }
}

const round1 = (v) => Math.round(v * 10) / 10;
