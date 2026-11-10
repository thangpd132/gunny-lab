# 2D Projectile Simulator – Gunny Research Lab

Ứng dụng web mô phỏng quỹ đạo đường đạn 2D (lấy cảm hứng từ cơ chế bắn của Gunny) với **bộ máy vật lý thật chạy ở backend C#** và giao diện Canvas tương tác. Có chế độ **Reverse Engineering** để hiệu chỉnh mô hình bằng dữ liệu bạn tự đo trong game (grid search + bình phương tối thiểu Levenberg–Marquardt).

> Ứng dụng **không** giả định biết thuật toán nội bộ của Gunny và **không** đi kèm dữ liệu Gunny nào. Mô hình là một giả thuyết có thể hiệu chỉnh được; dữ liệu thực nghiệm do bạn nhập vào.

---

## 1. Yêu cầu

- .NET SDK 8.0 (`dotnet --list-sdks` phải có dòng `8.0.x`)
- Một trình duyệt hiện đại (Chrome, Edge, Firefox)

Không cần Node, npm hay framework frontend nào.

## 2. Chạy ứng dụng

```bash
cd ProjectileSimulator
```

```bash
dotnet run
```

Mở trình duyệt tại **http://localhost:5080** (cổng cấu hình trong `ProjectileSimulator/Properties/launchSettings.json`).

## 3. Build và chạy test

Chạy ở thư mục gốc (chứa `ProjectileSimulator.sln`):

```bash
dotnet build
```

```bash
dotnet test
```

Hiện có 52 test (physics engine, các thành phần vật lý, reverse engineering/optimizer, và test tích hợp API).

---

## 4. Cấu trúc project

```
GunnyLab/
├── ProjectileSimulator.sln
├── ProjectileSimulator/                 ← ASP.NET Core 8 Web API + static frontend
│   ├── Program.cs                       ← chỉ cấu hình DI, JSON, static files, controllers
│   ├── Controllers/
│   │   ├── SimulationController.cs      ← /api/simulation/run | compare | defaults
│   │   └── ExperimentsController.cs     ← /api/experiments/evaluate | optimize | parameters
│   ├── Models/                          ← DTO: request, result, trajectory point, experiment, optimization
│   ├── Physics/                         ← các thành phần vật lý độc lập
│   │   ├── Vector2D.cs
│   │   ├── Projectile.cs                ← trạng thái + phương trình chuyển động
│   │   ├── IAccelerationModel.cs        ← giao diện chung cho mọi lực
│   │   ├── GravityModel.cs
│   │   ├── WindModel.cs                 ← ConstantWind (trôi theo khối khí) / WindAcceleration (lực ngang)
│   │   ├── DragModel.cs                 ← None / Linear / Quadratic
│   │   ├── Integrators.cs               ← Semi-implicit Euler, Explicit Euler, RK4
│   │   └── CollisionDetector.cs         ← va chạm liên tục (segment vs ground / AABB)
│   ├── Services/
│   │   ├── IPhysicsEngine.cs / PhysicsEngine.cs   ← vòng lặp time-step
│   │   ├── ShotSetup.cs                 ← "công thức game": Power → V0, góc, gió, điểm bắn
│   │   ├── ModelPresets.cs              ← Model A/B/C/D
│   │   ├── ReverseEngineeringService.cs ← dự đoán điểm rơi, sai số, MAE/RMSE
│   │   └── OptimizationService.cs       ← Grid search + Levenberg–Marquardt
│   └── wwwroot/
│       ├── index.html
│       ├── css/style.css
│       └── js/
│           ├── app.js        ← slider, nút, kết quả, lịch sử, CSV
│           ├── canvas.js     ← vẽ Canvas, đổi tọa độ, zoom/pan, kéo mục tiêu
│           ├── simulator.js  ← Fetch API client + hoạt ảnh (chỉ nội suy, không tính vật lý)
│           └── reverse.js    ← Reverse Engineering Mode
└── ProjectileSimulator.Tests/           ← xUnit
```

---

## 5. Mô hình vật lý

### Đơn vị
Mọi độ dài tính bằng **u** (một bước của slider Distance). Distance = 5 nghĩa là mục tiêu cách người bắn 5 u. Trọng lực tính bằng u/s², vận tốc u/s. Canvas tự quy đổi u → pixel (thước tỉ lệ hiển thị góc dưới trái).

### Hệ tọa độ
- Physics: trục Y **hướng lên**, người bắn ở x = 0, mặt đất y = 0.
- Canvas: trục Y hướng xuống. Phép đổi duy nhất nằm trong `canvas.js`:
  `screenY = height − (worldY − y0) · scale`.

### Từ thông số game → điều kiện đầu (`ShotSetup.cs`)
```
θ      = Angle + AngleOffset                       (0° = phải, 90° = thẳng lên, 180° = trái)
V0     = k · VelocityMultiplier · (Power · PowerMultiplier)^PowerExponent     (k mặc định 0.14)
Vx     = V0·cos θ,   Vy = V0·sin θ
g_eff  = Gravity · GravityMultiplier
điểm bắn = (0, LaunchHeight) + BarrelLength·(cos θ, sin θ)
```
Với mặc định (45°, Power 50) ⇒ V0 = 7 u/s, tầm xa ≈ 5 u, nên mục tiêu mặc định ở Distance 5 bị trúng.

### Phương trình chuyển động (`Projectile.cs`)
```
dX/dt = v + w_drift
dv/dt = Σ a_i(X, v) = gravity + windAcceleration + drag/m
```
Không có gió và lực cản, nghiệm giải tích là ném xiên quen thuộc:
`X(t) = X0 + Vx·t`, `Y(t) = Y0 + Vy·t − ½·g·t²` — engine trả kèm `analyticRange` để so sánh sai số tích phân.

### Gió (`WindModel.cs`)
| Chế độ | Công thức | Ý nghĩa |
|---|---|---|
| **Wind acceleration** (mặc định) | `a_x = Wind · WindMultiplier · 0.1` | Lực ngang không đổi, độ lệch ∝ t² |
| **Constant wind** | `w = Wind · WindMultiplier · 0.05` (u/s) cộng vào vận tốc vị trí | Khối khí chuyển động đều, đạn bị cuốn theo; lực cản (nếu bật) tác dụng trên vận tốc tương đối với không khí |

Hệ số 0.1 / 0.05 chỉnh được trong *Advanced formula constants*. Checkbox *Wind enabled* tắt hẳn gió để so sánh.

### Lực cản (`DragModel.cs`)
- Linear: `F = −b·v`
- Quadratic: `F = −½·ρ·Cd·A·|v|·v` (mặc định ρ = 1.225, Cd = 0.47, A = 0.07, m = 1 — mô hình đơn giản hóa, đơn vị game)

Mỗi lực là một lớp `IAccelerationModel` riêng. Muốn thử công thức mới: viết thêm một lớp và thêm vào danh sách trong `ShotSetup.From`.

### Tích phân theo thời gian (`PhysicsEngine.cs`, `Integrators.cs`)
Mỗi bước Δt (mặc định 0.016 s): (1) tính gia tốc → (2) cập nhật vận tốc → (3) cập nhật vị trí → (4) kiểm tra va chạm trên **cả đoạn** vừa đi (không xuyên qua mục tiêu mỏng) → (5) lưu điểm.
- **Semi-implicit Euler** (mặc định, kiểu game engine), **Explicit Euler** (chỉ để so sánh), **RK4** (chuẩn tham chiếu; chính xác tuyệt đối với gia tốc không đổi, điểm chạm đất được tinh chỉnh bằng nội suy Hermite).
- Điểm chạm đất/mục tiêu được nội suy chính xác trong bước cuối.
- Vòng lặp luôn dừng: giới hạn `MaxTime/Δt` (tối đa 2 000 000 bước), ra khỏi biên thế giới, hoặc phát hiện NaN/∞. Trạng thái trả về: `HitTarget`, `HitGround`, `OutOfBounds`, `Timeout`.
- Số điểm trả về cho trình duyệt bị giới hạn (mặc định 3000, decimate đều, luôn giữ điểm đầu/cuối) để giao diện không bị giật.

---

## 6. Hướng dẫn sử dụng

### Tab Simulator
- **Slider**: Distance (1–10), Angle (0–180°), Power (1–100), Wind (−20…20), Gravity, lực cản, Δt, Max time, tốc độ phát hoạt ảnh. Ô số cạnh mỗi slider cho phép gõ giá trị chính xác.
- **Live preview**: kéo slider là đường đạn dự đoán (nét đứt trắng) cập nhật ngay (gọi API, debounce + hủy request cũ).
- Kéo trực tiếp **mục tiêu B** trên Canvas để đổi khoảng cách. Cuộn chuột = zoom, kéo = di chuyển, double-click = khung nhìn mặc định.
- Nút: **Simulate** (Enter) tính và vẽ ngay; **Fire** (Space) chạy hoạt ảnh viên đạn; **Pause/Resume** (P); **Reset** đưa mọi thông số về mặc định; **Clear Trajectory** xóa các đường trên Canvas (giữ lịch sử); **Compare** chạy Model A/B/C/D với thông số hiện tại.
- **Shot result**: góc, power, V0, distance, gió, thời gian bay, độ cao cực đại, Landing X/Y, tầm xa, độ lệch so với mục tiêu, số bước mô phỏng, trạng thái va chạm…
- **Shot history**: bật/tắt hiển thị từng phát bắn trên Canvas (so sánh nhiều lần bắn), nạp lại thông số (↺), xóa, **Export CSV**. Lịch sử lưu trong localStorage.

### Tab Reverse Engineering
1. **Model parameters** – Initial Velocity Multiplier, Gravity Multiplier, Wind Multiplier, Air Resistance Coefficient, Angle Offset, Power Multiplier, Launch Height (+ hằng số nâng cao). Các tham số này áp dụng cho mọi phát bắn (Model D).
2. **Compare models** – chọn A: Basic, B: + Wind, C: + Air Resistance, D: Custom; mỗi model một màu trên cùng Canvas, kèm bảng so sánh.
3. **Experimental data** – nhập các phát bắn bạn đo trong game: Distance, Angle, Power, Wind, Observed Landing X, Observed Landing Y, và *(tùy chọn)* thời gian bay. Hỗ trợ Import/Export CSV:
   ```
   label,distance,angle,power,wind,observedLandingX,observedLandingY,observedFlightTime
   ```
   X đo từ người bắn, Y đo từ mặt đất của người bắn.
4. **Evaluate error** – bảng Predicted / Actual / Error X / Error Distance cùng **MAE** và **RMSE**; điểm quan sát (hình thoi vàng) và điểm dự đoán (vòng tím) được vẽ trên Canvas.
   *Điểm rơi dự đoán* = giao điểm đầu tiên của quỹ đạo (đang đi xuống) với đường y = Observed Y; nếu quỹ đạo không lên tới độ cao đó thì dùng điểm gần nhất.
5. **Optimize parameters** – tích chọn tham số cần fit, đặt Min/Max/số điểm lưới, chọn phương pháp:
   - *Grid search*: duyệt toàn bộ lưới (chạy song song),
   - *Least squares (Levenberg–Marquardt)*: Jacobian sai phân hữu hạn, trong không gian chuẩn hóa có ràng buộc,
   - *Grid → Least squares*: tìm vùng bằng lưới rồi tinh chỉnh.
   Hàm mục tiêu: `Σ ErrorDistance² + Σ (w · ErrorTime)²`. Kết quả hiển thị RMSE/MAE trước–sau, tham số tốt nhất, biểu đồ hội tụ, nút **Apply best parameters**.

**Lưu ý nhận dạng tham số:** nếu chỉ có điểm rơi trên mặt phẳng, tầm xa chủ yếu phụ thuộc V0²/g nên Velocity và Gravity multiplier gần như không tách được — hãy nhập thêm thời gian bay (đếm frame video). Velocity Multiplier và Power Multiplier hoàn toàn tương đương khi Power exponent = 1, chỉ nên fit một trong hai. Optimizer sẽ ghi chú các trường hợp này.

**Synthetic test data…** sinh dữ liệu từ *chính mô hình hiện tại* (có nhiễu tùy chọn), gắn nhãn `SYNTHETIC-n`. Dữ liệu này **không phải dữ liệu Gunny**; chỉ dùng để kiểm tra optimizer (ví dụ: sinh dữ liệu → đổi Velocity ×1.3 → Optimize → kiểm tra nó tìm lại 1.0).

---

## 7. API

Enum gửi/nhận dạng chuỗi (`"RungeKutta4"`, `"Quadratic"`, …). Mọi trường đều có giá trị mặc định, nên chỉ cần gửi các trường muốn thay đổi. Dữ liệu sai trả về `400` (ValidationProblemDetails).

| Method | Endpoint | Mô tả |
|---|---|---|
| GET | `/api/simulation/defaults` | Request mặc định |
| POST | `/api/simulation/run` | Một lần mô phỏng → quỹ đạo + kết quả |
| POST | `/api/simulation/compare` | `{ base, models: ["Basic","Wind","Drag","Custom"], scenarios: [{label, request}] }` |
| GET | `/api/experiments/parameters` | Khoảng tìm kiếm mặc định |
| POST | `/api/experiments/evaluate` | `{ base, experiments, flightTimeWeight }` → dự đoán + MAE/RMSE |
| POST | `/api/experiments/optimize` | `{ base, experiments, method, parameters, maxIterations, maxGridEvaluations, flightTimeWeight }` |

Ví dụ:

```bash
curl -s -X POST http://localhost:5080/api/simulation/run -H "Content-Type: application/json" -d '{"angle":60,"power":70,"wind":-5,"dragType":"Quadratic","integrator":"RungeKutta4"}'
```

Các trường chính của `SimulationRequest`: `distance, angle, power, wind, gravity, windEnabled, windMode, dragType, dragCoefficient, airDensity, crossSectionArea, mass, integrator, deltaTime, maxTime, maxTrajectoryPoints, barrelLength, targetWidth, targetHeight, targetCollisionEnabled, groundLevel, calibration{velocityMultiplier, powerMultiplier, powerExponent, gravityMultiplier, windMultiplier, angleOffset, launchHeight, powerToVelocity, windAccelerationScale, windDriftScale}`.

---

## 8. Giả định đã chọn

- Mặt đất phẳng (y = 0); mục tiêu là hộp va chạm chữ nhật 0.4 × 0.6 u đặt tại x = Distance; đạn không va chạm với chính người bắn.
- Góc > 90° bắn về phía sau (trái); mục tiêu luôn ở bên phải.
- Ở chế độ Constant wind, đạn được khối khí cuốn theo ngay khi rời nòng (vận tốc so với đất = vận tốc nòng + vận tốc gió).
- Power → V0 tuyến tính (mũ = 1) theo mặc định; có thể đổi bằng Power exponent.
- Đơn vị vật lý là đơn vị game (u), không phải mét.
