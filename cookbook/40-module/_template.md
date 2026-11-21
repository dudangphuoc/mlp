# Mxx — Tên module

> **Phase:** P? · **Phụ thuộc:** Mxx · **Quyết định:** D-xx · **Proto:** `proto/mlplatform/v1/<file>.proto` · **Schema DB:** `<schema>`

## 1. Mục tiêu & phạm vi
Một đoạn mục tiêu, rồi bảng phạm vi theo phase:

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|

## 2. Khái niệm
Thuật ngữ riêng của module (thuật ngữ chung ở [thuật ngữ](../00-tong-quan/02-thuat-ngu.md)).

## 3. Dữ liệu
Bảng trong schema, cột chính, chỉ mục, ràng buộc.

## 4. Trạng thái & quy tắc
Sơ đồ trạng thái, bảng chuyển trạng thái (ai được làm), quy tắc nghiệp vụ đánh số `R1, R2…`.

## 5. Luồng chính
Các luồng đánh số, mỗi bước nêu ai/cái gì làm và chạy ở đâu.

## 6. Hợp đồng
Service gRPC, bảng: `RPC | Quyền tối thiểu | Mô tả`. Message chi tiết nằm trong file `.proto`.

## 7. Màn hình
Mỗi màn hình: mã `S-xx-n`, route, ai thấy, wireframe, thành phần (dẫn tới [hệ thống thiết kế](../30-ui/01-he-thong-thiet-ke.md)), trạng thái (tải / rỗng / lỗi), bảng hành động → RPC.

## 8. Sự kiện
Domain event phát ra (`events.proto`) và ai nghe.

## 9. Lỗi
`ErrorInfo.reason` → gRPC status → thông điệp cho người dùng.

## 10. Nghiệm thu
Checklist kiểm được trên stack Docker.
