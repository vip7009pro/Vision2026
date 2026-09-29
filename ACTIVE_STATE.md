# ACTIVE_STATE.md

## Task hiện tại
Task 379 — Tăng tốc Auto Tune Preprocess bằng ĐA LUỒNG ĐA NHÂN. **ĐÃ HOÀN THÀNH**.

## Mục tiêu
Auto Tune trước đây chạy tuần tự (quá chậm với ~250-300 tổ hợp × preprocess ảnh 20MP). Nay chạy song song trên nhiều nhân.

## Cách làm (Task 379)
- `PreprocessAutoTuner.Tune`: Stage 1 (preset) và **mỗi nhóm của Stage 2 (coordinate descent)** chạy `Parallel.For`.
- `MaxDegreeOfParallelism = clamp(ProcessorCount - 1, 1, cap)`; `cap = 4` cho ảnh > 2MP (20MP), `cap = 8` cho ảnh nhỏ — chừa 1 nhân cho UI, giới hạn đỉnh RAM (~60MB/ứng viên).
- An toàn đa luồng: `bestLock` cho điểm cao nhất, `Interlocked` cho bộ đếm tiến trình, chọn option tốt nhất theo thứ tự index cố định (tất định). Mỗi ứng viên tự tạo/thu hồi Mat riêng.
- Hủy vẫn hoạt động (bắt cả `OperationCanceledException` bọc trong `AggregateException` của `Parallel.For`).
- Tóm tắt cuối ghi rõ: "… đã thử N tổ hợp trên K luồng".

## Ghi chú kỹ thuật liên quan (Task 378)
- Preview "Final" chỉ dựng lại khi ảnh/nội dung Job/kết quả Run đổi (trỏ node không còn dựng lại) → giảm lag chính.
- Đã sửa rò rỉ ~60MB mỗi lần nạp ảnh URL/snapshot camera (`SetImage(..., transferOwnership: true)`).

## Việc cần làm tiếp theo
- [ ] Kiểm thử thực tế: đo thời gian Auto Tune trước/sau trên máy nhiều nhân với ảnh 20MP.
- [ ] (Ghi chú) Test runner `TestExtractApp` fail ở `CameraTest.TestContinuousEngineHandshakeBypass` do ảnh Simulator lưu cá nhân hóa (không 640x480) — lỗi môi trường. Khi tạm bỏ test này: 9/9 test hiệu năng PASS.
