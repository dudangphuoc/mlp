# Cookbook — MLPlatform

Portal điều phối ML tự host: dữ liệu → gán nhãn → notebook → train → registry → suy luận, cho **mọi loại dữ liệu và mọi họ model** (mỗi họ là một recipe chạy trong image riêng); tính toán chạy trên các PC được cắm vào. Bắt đầu từ **[Toàn cảnh](00-tong-quan/01-toan-canh.md)**.

## Cấu trúc

| Thư mục | Tài liệu | Tính chất |
|---|---|---|
| **00-tong-quan** | [01 Toàn cảnh](00-tong-quan/01-toan-canh.md) · [02 Thuật ngữ](00-tong-quan/02-thuat-ngu.md) · [03 Sổ quyết định (ADR)](00-tong-quan/03-quyet-dinh.md) | Chuẩn |
| **10-khao-sat** | [01 Ultralytics Platform](10-khao-sat/01-ultralytics-platform.md) · [02 Ultralytics On Premise](10-khao-sat/02-ultralytics-on-premise.md) · [03 Đối chiếu MLOps](10-khao-sat/03-doi-chieu-mlops.md) · [04 Tổng quát hoá](10-khao-sat/04-tong-quat-hoa.md) · [05 UI Ultralytics bằng REA](10-khao-sat/05-ui-ultralytics-rea.md) (+ [bằng chứng](10-khao-sat/rea-ui-ultralytics/)) | Tham khảo, không phải spec |
| **20-kien-truc** | [01 Hệ thống](20-kien-truc/01-he-thong.md) · [02 Backend .NET AOT](20-kien-truc/02-backend-dotnet-aot.md) · [03 Hợp đồng proto](20-kien-truc/03-hop-dong-proto.md) · [04 Agent](20-kien-truc/04-agent.md) · [05 Dữ liệu & lưu trữ](20-kien-truc/05-du-lieu-va-luu-tru.md) · [06 Bảo mật](20-kien-truc/06-bao-mat.md) · [07 Quy ước chung](20-kien-truc/07-quy-uoc-chung.md) · [08 Recipe & runtime](20-kien-truc/08-recipe-va-runtime.md) · [09 Vận hành](20-kien-truc/09-van-hanh.md) · [10 Quan sát & khôi phục](20-kien-truc/10-quan-sat-va-khoi-phuc.md) | Chuẩn |
| **30-ui** | [01 Hệ thống thiết kế](30-ui/01-he-thong-thiet-ke.md) · [02 Sơ đồ màn hình](30-ui/02-so-do-man-hinh.md) | Chuẩn |
| **40-module** | Bảng dưới ([template](40-module/_template.md)) | Chuẩn |
| **50-lo-trinh** | [01 Lộ trình P0–P3](50-lo-trinh/01-lo-trinh.md) · [02 Spec P0](50-lo-trinh/02-p0-nen-tang.md) | Chuẩn |

## Module

| Mã | Module | Phase | Spec |
|---|---|---|---|
| [M01](40-module/M01-iam.md) | Workspace & IAM | P1 | ✅ |
| [M02](40-module/M02-fleet.md) | Fleet (cắm máy) | P1 | ✅ |
| [M03](40-module/M03-scheduler.md) | Job Scheduler | P1 | ✅ |
| [M04](40-module/M04-data.md) | Data Management | P1 | ✅ |
| [M05](40-module/M05-labeling.md) | Labeling | P1 | ✅ |
| [M06](40-module/M06-lineage-audit.md) | Lineage & Audit | P1 | ✅ |
| [M07](40-module/M07-feature-store.md) | Feature Store | P3 | 🟡 khung |
| [M08](40-module/M08-notebook.md) | Jupyter Notebook | P1 | ✅ |
| [M09](40-module/M09-tracking.md) | Experiment Tracking & Train | P1 | ✅ |
| [M10](40-module/M10-hpo-automl.md) | HPO & AutoML | P2 | 🟡 khung |
| [M11](40-module/M11-registry.md) | Model Registry | P1 | ✅ |
| [M12](40-module/M12-serving.md) | Serving | P1 | ✅ |
| [M13](40-module/M13-monitoring-drift.md) | Monitoring, Drift & CT | P2 | 🟡 khung |
| [M14](40-module/M14-pipelines-agents.md) | Pipelines & Agents | P2 | 🟡 khung |
| [M15](40-module/M15-llmops.md) | LLMOps | P3 | 🟡 khung |
| [M16](40-module/M16-notifications.md) | Thông báo | P1 | ✅ |
| [M17](40-module/M17-model-catalog.md) | **Model Catalog** (recipe mọi họ model, biến thể, task) | P1 | ✅ |

## Quy tắc viết tài liệu
- Mỗi module đúng một file theo [template](40-module/_template.md); phase ghi trong file, không tách thư mục theo phase.
- Quyết định mới → thêm dòng vào [sổ quyết định](00-tong-quan/03-quyet-dinh.md) và sửa tài liệu liên quan trong cùng lần; **không dán banner "cập nhật" lên nội dung cũ**.
- Khảo sát nằm ở `10-khao-sat`, không dùng làm spec.
- Cổng mạng chỉ định nghĩa ở [20/01 §2](20-kien-truc/01-he-thong.md); mã lỗi ở mục 9 từng module; màn hình có mã `S-xx-n` trong [sơ đồ màn hình](30-ui/02-so-do-man-hinh.md).
