# Gunny Research Lab – Bắn Pháo 2D

Trò chơi bắn pháo 2D theo lượt, chạy trên web, lấy cảm hứng từ lối chơi của Gunny PC: nhân vật A bên trái, mục tiêu B bên phải, mặt đất phẳng, chỉnh góc bằng phím mũi tên, **giữ Space để tích lực, thả để bắn**. Quỹ đạo đạn do **Physics Engine C# ở backend** tính theo từng bước thời gian; trình duyệt chỉ vẽ và phát lại hoạt ảnh.

Ngoài màn chơi còn có trang **Nghiên cứu công thức** (giao diện kỹ thuật cũ, đã Việt hoá) để chỉnh thông số vật lý, so sánh mô hình và dò ngược công thức bằng dữ liệu bạn tự đo trong game.

> Ứng dụng **không** giả định biết thuật toán nội bộ của Gunny và **không** dùng đồ hoạ hay dữ liệu của Gunny. Nhân vật và mục tiêu là hình khối tự vẽ.

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

Mở trình duyệt tại **http://localhost:5080** — màn chơi hiện ra ngay. Trang nghiên cứu ở **http://localhost:5080/research.html** (hoặc bấm nút *Nghiên cứu công thức* góc trên bên phải). Cổng cấu hình trong `ProjectileSimulator/Properties/launchSettings.json`.

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
│       ├── index.html                   ← MÀN CHƠI (trang chính)
│       ├── research.html                ← Nghiên cứu công thức
│       ├── css/game.css, css/research.css
│       └── js/
│           ├── game/
│           │   ├── GameController.js    ← vòng lặp game, trạng thái, lượt bắn, HUD, cài đặt
│           │   ├── KeyboardController.js← ↑↓ ←→ Space Enter R, chống lặp phím, mất focus
│           │   ├── PowerController.js   ← tích lực 0–100 %, chốt lực khi thả, kéo tay
│           │   ├── CanvasRenderer.js    ← hệ toạ độ, camera, nhân vật, bia, thước đo, đạn
│           │   └── ProjectileSimulator.js← gọi API vật lý, phát lại quỹ đạo theo thời gian thực
│           └── research/                ← app.js, canvas.js, simulator.js, reverse.js (trang nghiên cứu)
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

### Màn chơi (trang chính)

| Phím | Tác dụng |
|---|---|
| `↑` / `↓` | Tăng / giảm góc 1° mỗi lần nhấn (giữ phím để xoay liên tục), giới hạn 0–180° |
| `←` / `→` | Giữ để di chuyển nhân vật A; mục tiêu B đứng yên, khoảng cách cập nhật liên tục |
| `Space` | **Giữ** để tích lực (thanh Lực chạy 0 → 100 %), **thả** để chốt lực và bắn |
| `Enter` | Bắn ngay với lực đang hiển thị (chế độ thử nghiệm) |
| `R` | Đặt lại vị trí, góc, lực và xoá đường đạn |

- **Thanh Lực** ở dưới màn hình: giữ Space thì tăng; có thể bấm/kéo trên thanh để đặt lực thủ công. Vạch trắng đánh dấu lực của phát trước.
- **Đồng hồ góc** (trái), **khoảng cách** + thanh kéo khoảng cách 1–10 (kéo thì B di chuyển), **gió**, nút **BẮN** và **Đặt lại** (phải).
- **Thước đo** nằm trên mặt đất: vạch 0 luôn ở vị trí A, mỗi vạch = 1 đơn vị, đoạn vàng là khoảng A→B.
- Đường **nét đứt trắng** là quỹ đạo dự đoán với lực hiện tại (bật/tắt trong Cài đặt); đường **nét liền** là đường đạn thật được vẽ dần theo vị trí viên đạn. Khi đường đạn vượt khỏi khung, camera tự thu nhỏ (giữ nguyên tỉ lệ) để thấy toàn bộ.
- Trong lúc đạn bay không thể bắn tiếp (theo lượt). Thẻ góc trên trái tóm tắt phát vừa bắn: góc, lực, thời gian bay, điểm rơi, độ lệch, số phát trúng.
- **⚙ Cài đặt**: trọng lực, bật/tắt gió + cường độ + kiểu gió, gió ngẫu nhiên mỗi lượt, bật/tắt lực cản + hệ số, tốc độ di chuyển, thời gian tích đầy lực, tốc độ hoạt ảnh, Δt, hiển thị lưới/quỹ đạo dự đoán/đường đạn cũ. Cài đặt được lưu trong trình duyệt.

Chi tiết xử lý bàn phím: mọi phím game đều `preventDefault` ở cả `keydown` và `keyup` nên trang không cuộn và không nút nào bị "bấm" nhầm; `keydown` lặp lại của Space bị bỏ qua nên một lần giữ chỉ bắn một phát; nếu cửa sổ mất focus (chuyển tab, Alt+Tab) khi đang giữ Space thì lần tích lực đó bị huỷ, không bắn.

### Trang Nghiên cứu công thức – tab Mô phỏng vật lý
Đầy đủ thông số vật lý (khoảng cách, góc, lực, gió, trọng lực, lực cản, phương pháp tích phân, Δt), các nút Mô phỏng / Bắn / Tạm dừng / Tiếp tục / So sánh / Xoá đường đạn / Đặt lại, bảng kết quả chi tiết, lịch sử bắn với toạ độ (bật/tắt từng phát trên canvas, xuất CSV).

### Trang Nghiên cứu công thức – tab Dò công thức
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
