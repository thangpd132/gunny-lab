# Mô hình tính toán Gunny PC – V3

Tài liệu này mô tả các mô hình trong tab **Nghiên cứu công thức → Mô hình Gunny (V3)**: nguồn tham khảo, giả định, điều kiện áp dụng và những gì **chưa xác định được**.

> **Quan trọng:** Không mô hình nào ở đây là mã nguồn chính thức của Gunny. Công thức cộng đồng có thể đã lỗi thời so với phiên bản hiện tại. Kết quả chỉ đáng tin khi đã được kiểm chứng bằng dữ liệu bạn tự bắn trong game.

## 1. Tình trạng nghiên cứu tài liệu

Ba tài liệu được yêu cầu:

| # | Tài liệu | Tình trạng |
|---|---|---|
| [1] | https://gunny.360game.vn/tin-tuc/hang-ngay/cong-thuc-ban-sieu-cao.html | **Không truy cập được** từ môi trường phát triển (tên miền bị chặn bởi chính sách mạng). Công thức Model B lấy theo nội dung người dùng trích từ bài này. |
| [2] | https://gunny.360game.vn/tin-tuc/cong-dong/hoan-thanh-bai-tap-65-do-sieu-cao-va-cao-thap.html | **Không truy cập được.** Chỉ dùng các nhận xét định tính người dùng tóm tắt (góc 65 nhạy với lực; gió có thể cần chỉnh góc hoặc lực). Không có số liệu → không đặt công thức. |
| [3] | https://gunnymoinhat.blogspot.com/2015/04/tong-hop-cong-thuc-ban-goc-thap-10-20.html | **Không truy cập được.** Không trích được bảng nào → Model D chỉ học từ dữ liệu. Bài viết từ 2015, có thể đã cũ. |

Tìm kiếm web cũng không trả về nội dung của các bài trên. Nếu sau này đọc được, hãy cập nhật tham số Model B và nhập bảng số liệu của [2], [3] vào **Dữ liệu bắn thực tế** (ghi chú nguồn ở cột Notes) thay vì sửa code.

## 2. Vì sao đường đạn simulator khác Gunny

Engine vật lý ban đầu (nay là Model A) khác Gunny ở các điểm:

1. **Đơn vị khoảng cách tự đặt.** 1 "u" trên thước gameplay không có quan hệ với khoảng chia màn hình Gunny. → Thêm *Distance Calibration*.
2. **Quy đổi lực → vận tốc là giả định.** `V0 = 0.14·Power` được chọn để mặc định 45°/lực 50 trúng khoảng cách 5; không dựa trên Gunny.
3. **Mô hình gió là giả định.** Gió là gia tốc ngang `0.1·W`; thang đo gió Gunny chưa biết.
4. **Không có dữ liệu thực nghiệm.** Không thể khớp tham số khi chưa có lần bắn thực tế nào.
5. **Một công thức cho mọi kỹ thuật.** Cộng đồng Gunny dùng các quy tắc khác nhau cho siêu cao, 65°, góc thấp. → Tách thành các mô hình độc lập.

## 3. Quy đổi cự ly (Distance Calibration)

```
GunnyDistance     = SimDistance × CalibrationFactor
CalibrationFactor = 1 / SimUnitsPerDivision
PixelsPerDivision = ReferenceScreenWidthPx / ScreenDivisions
```

- **ScreenDivisions** (mặc định 9 — theo mô tả tài liệu [1]), **ReferenceScreenWidthPx** (mặc định 1000 px, chỉ dùng để đổi số đo pixel trên ảnh chụp màn hình).
- **SimUnitsPerDivision**, xác định bằng một trong hai cách:
  - *Neo theo phát tham chiếu* (mặc định): mô phỏng phát 81° / lực 100 / gió 0 bằng engine vật lý, gán điểm rơi = 9 khoảng. Với cấu hình mặc định: **1 khoảng = 0.7023 u**, hệ số = 1.4239. **Hệ quả:** Model A và B trùng nhau đúng tại mốc này *vì cách neo*, không phải vì vật lý khớp Gunny.
  - *Nhập tay*: nhập trực tiếp sau khi tự đo.
- **WindScale**: gió engine = gió Gunny × hệ số (mặc định 1, giả định).
- Thước đo gameplay (0–10 u) **không thay đổi**.

## 4. Các mô hình

Mọi mô hình cài đặt `IProjectileModel` (`ProjectileSimulator/Gunny/`): tên, mô tả, nguồn, điều kiện áp dụng, giả định, tham số, và các phương thức `Predict` (điểm rơi), `SuggestAim` (góc + lực), `Evaluate` (sai số trên dữ liệu thực).

### Model A – Physics Baseline (`PhysicsBaselineModel`)
- Engine ném xiên hiện có (Semi-implicit Euler, Δt 0.004 s), quy đổi cự ly ở trên.
- Áp dụng: góc 0–90°, lực 0–100. Có thời gian bay.
- Tối ưu: grid search → Levenberg–Marquardt trên hệ số vận tốc (và hệ số gió nếu dữ liệu có nhiều mức gió). Khi áp dụng, quy đổi cự ly chuyển sang *Nhập tay* (nếu vẫn neo thì đổi hệ số vận tốc sẽ kéo quy đổi đổi theo và triệt tiêu hiệu chỉnh).
- Tìm góc/lực: với mỗi góc 1–89°, chia đôi tìm lực để rơi đúng mục tiêu.

### Model B – Gunny Super High (`GunnySuperHighModel`)
```
Angle = BaseAngle + AnglePerDivision·(ReferenceDistance − Distance) + WindAngleFactor·Wind
mặc định: Angle = 81 + (9 − Distance) + 2·Wind,   Power = 100
```
- Nguồn: [1] (theo trích dẫn của người dùng, chưa đối chiếu trực tiếp).
- Wind có dấu theo chiều bắn (thuận +, ngược −). Distance tính bằng khoảng Gunny.
- Áp dụng: lực 100 (± 0.5) và góc 70–90° (*70° là giả định*, chỉnh được). Không áp dụng cho góc 65 hay góc thấp.
- Đây là quy tắc **ngắm**, không mô tả quỹ đạo; dự đoán điểm rơi là phép đảo: `D = RefD + (BaseAngle + k·W − Angle)/AnglePerDivision`. Không có thời gian bay.
- Tối ưu: bình phương tối thiểu tuyến tính cho (BaseAngle, AnglePerDivision, WindAngleFactor) trên các lần bắn siêu cao có điểm rơi. Nếu dữ liệu chỉ có một mức gió, hệ số gió giữ nguyên.

### Model C – Gunny 65° (`Gunny65DegreeModel`)
- **Chưa xác định công thức.** Không có hằng số nào đặt sẵn; điểm rơi = f(lực, gió) học từ các lần bắn ở góc 65° ± 0.5.
- Không dùng quy tắc "2° mỗi đơn vị gió" của siêu cao.
- Nếu dữ liệu chỉ có một mức gió, mô hình **từ chối** dự đoán với gió khác ("chưa xác định ảnh hưởng của gió").

### Model D – Gunny góc thấp (`GunnyLowAngleModel`)
- Mỗi góc kỹ thuật (mặc định 10°, 20°, 30°; sửa được) là một mô hình con độc lập, cùng cơ chế học như Model C nhưng **dữ liệu riêng**.
- Chưa xác định công thức nào (nguồn [3] không truy cập được).

### Model E – Custom Empirical (`CustomEmpiricalModel`)
- Điểm rơi = f(góc, lực, gió) trên mọi lần bắn trong khoảng góc đã chọn.

### Cách chọn phương pháp cho mô hình học từ dữ liệu (C, D, E)
`EmpiricalFitter` thử các ứng viên:

| Phương pháp | C, D (góc cố định) | E (nhiều góc) |
|---|---|---|
| Nội suy tuyến tính từng đoạn theo lực | ✓ (chỉ khi gió không đổi) | – |
| Hồi quy tuyến tính | D = a + b·P (+ c·W) | D = a + b·θ + c·P (+ d·W) |
| Hồi quy bậc 2 | D = a + b·P + c·P² (+ d·W) | bậc 2 theo θ, P (+ W) |
| Hồi quy dạng vật lý | – | D = a + b·P²·sin2θ (+ c·W) |
| Bảng tra (lần bắn gần nhất) | ✓ | ✓ |

- Chọn ứng viên có **LOOCV RMSE** (kiểm định chéo bỏ-một) nhỏ nhất, *không* chọn theo sai số trên chính dữ liệu đã khớp (đa thức nhiều hệ số luôn "khớp" tốt hơn trên dữ liệu đã thấy).
- Mỗi hồi quy cần ≥ (số hệ số + 2) lần bắn. Dữ liệu suy biến (ví dụ mọi lần bắn cùng một lực) bị từ chối.
- Dự đoán ngoài phạm vi dữ liệu được đánh dấu **NGOẠI SUY**.

## 5. Dữ liệu thực nghiệm

Mỗi bản ghi: Distance, Angle, Power, Wind (≥ 0), WindDirection (thuận/ngược), Weapon, ActualLanding (khoảng Gunny, đo từ người bắn), HitTarget, Notes. Lưu tại `ProjectileSimulator/App_Data/gunny-experiments.json` (đổi thư mục bằng cấu hình `GunnyLab:DataDirectory`).

CSV:
```
distance,angle,power,wind,windDirection,weapon,actualLanding,hitTarget,notes
```
Tiêu đề tiếng Việt có dấu cũng được nhận (khoảng cách, góc, lực, gió, hướng gió, vũ khí, điểm rơi, trúng, ghi chú). Gió âm = gió ngược. Dòng lỗi bị bỏ qua và báo kèm số dòng.

Điểm rơi để trống nhưng "Trúng = Có" → coi như rơi đúng tại Distance. Trống và "Không" → không dùng để tính sai số.

**Không có dữ liệu Gunny nào được đóng gói sẵn.** Bảng ví dụ trong yêu cầu (5.0/65/68/0/5.1, …) chỉ là định dạng, không được nạp vào ứng dụng.

## 6. Tìm góc và lực / So sánh

- **Tìm góc và lực tối ưu**: mỗi mô hình đề xuất phương án cho (khoảng cách mục tiêu, gió). Sắp xếp: *Đã xác nhận* trước, rồi theo |sai số| tăng dần.
  - **Đã xác nhận (n)** = có n lần bắn thực tế lệch góc ≤ 0.5°, lực ≤ 1, gió ≤ 0.5, khoảng cách ≤ 0.25 khoảng, và đã trúng (hoặc điểm rơi lệch mục tiêu ≤ 0.25 khoảng).
  - **Chỉ dự đoán** = chưa kiểm chứng; không được coi là chắc chắn trúng trong game.
- **So sánh đường đạn**: lấy phương án tốt nhất của từng mô hình, vẽ quỹ đạo do **engine vật lý** tính với đúng (góc, lực, gió) đó. Bảng hiển thị riêng *điểm rơi theo mô hình* và *điểm rơi theo vật lý*; quỹ đạo không bị chỉnh cho khớp.

## 7. Thêm công thức mới

1. Tạo lớp mới trong `ProjectileSimulator/Gunny/Models/` cài đặt `IProjectileModel` (điền nguồn, giả định, điều kiện áp dụng).
2. Thêm tham số vào `GunnyResearchSettings` nếu cần.
3. Đăng ký trong `GunnyModelRegistry`.
4. Viết unit test trong `ProjectileSimulator.Tests/GunnyModelTests.cs`.

Giao diện tự hiển thị mô hình mới; tham số chỉnh được trên giao diện cần thêm ánh xạ trong `PARAM_PATHS` (`wwwroot/js/research/gunny.js`).
