# Thuật ngữ

| Thuật ngữ | Nghĩa trong MLPlatform |
|---|---|
| **Control plane** | Server portal: API .NET, web UI, PostgreSQL, kho S3, registry, Ray head, WireGuard hub. Không train. |
| **Execution plane** | Tập các PC đã cắm, mỗi PC chạy agent + Ray worker mỏng + container workload của recipe (train, eval, serve) + container notebook. |
| **Host / máy** | Một PC đã cắm vào workspace. |
| **Agent** | Chương trình .NET AOT chạy như service trên host; giữ kênh điều khiển tới control plane, quản lý Docker, WireGuard, Ray node, container recipe, notebook. |
| **Enroll** | Thủ tục cắm máy bằng token một lần. |
| **Pool** | Nhóm host dùng chung giới hạn (số job, GPU/người). |
| **Kênh agent** | Kết nối TCP+TLS dài hạn agent → control plane, khung protobuf delimited. |
| **Data channel** | Kết nối TCP ngắn hạn agent mở theo yêu cầu để chuyển byte thô (Jupyter, file local-only). |
| **Overlay** | Mạng WireGuard `10.88.0.0/16` chỉ cho giao thức Ray. |
| **Ray head / worker node** | Head trên control plane (0 CPU/0 GPU), worker mỏng trong container `mlp-node` trên host — chỉ giữ tài nguyên và báo trạng thái; workload chạy trong container của recipe (D-21). |
| **Job** | Đơn vị việc được lập lịch (train, ingest, label_assist, notebook…); mỗi lần chạy là một **attempt** = một placement group Ray + holder + container recipe trên mỗi máy. |
| **Holder** | Ray actor/job giữ chỗ CPU/GPU trong placement group cho container chạy ngoài Ray (train, eval, serve, notebook). |
| **Workspace** | Không gian chia sẻ của một người hoặc một nhóm; ranh giới quyền và dữ liệu. |
| **Dataset** | Tập **item** cùng modality + task + label schema. |
| **Item** | Đơn vị gán nhãn/huấn luyện: 1 ảnh, 1 video, 1 văn bản, 1 clip audio, 1 dòng bảng. |
| **Storage kind** | `managed` (kho S3), `external` (S3 ngoài), `host_local` (thư mục trên PC, không rời PC). |
| **Dataset version** | Manifest bất biến (item + hash + split + label set version). |
| **Label schema** | Danh sách class/thuộc tính/quan hệ có version của một dataset. |
| **Annotation** | Một nhãn trên item (bbox, polygon, span…). |
| **Label set version** | Ảnh chụp bất biến toàn bộ nhãn tại một thời điểm. |
| **Labeling project** | Đợt gán nhãn có phạm vi item, thành viên, cách phân công, cách review. |
| **Project (tracking)** | Nhóm các run cùng mục tiêu. |
| **Run** | Một lần train/eval có tham số, metric, artifact. |
| **Registered model / model version** | Model có tên; mỗi version bất biến, có signature, stage, alias. |
| **Stage / alias** | `none/staging/production/archived`; alias tự đặt (`champion`). |
| **Deployment / revision** | Endpoint suy luận; mỗi lần đổi version/tài nguyên là một revision. |
| **Profile (notebook)** | Image môi trường dựng sẵn (`cv`, `nlp-llm`…). |
| **Environment** | Image tuỳ biến người dùng lưu lại từ notebook. |
| **Recipe** | Mô tả một họ model (nguồn repo/pip/HF, license, task, môi trường, lệnh train/eval/export/serve, biến thể, định dạng dữ liệu, tham số, cách lấy metric) — xem [20/08](../20-kien-truc/08-recipe-va-runtime.md). Mọi họ model (dựng sẵn hay người dùng nhập) đều là recipe ngang hàng; recipe dựng sẵn chỉ là ví dụ. |
| **Biến thể** | Kích cỡ/cấu hình trong một recipe (`dfine-s`, `rf-detr-base`, `vibevoice-1.5b`). |
| **Recipe image / build** | Image Docker của recipe ở một commit, có digest; mọi job/model ghi build đã dùng. |
| **Shim** (`mlp-shim`) | Chương trình .NET AOT trong mọi container workload: chuẩn bị dữ liệu, chạy lệnh của repo, thu metric/checkpoint/artifact. |
| **Danh mục task** | Bản ghi task (modality vào/ra, loại nhãn, định dạng, hợp đồng suy luận, metric) — mở rộng được, gồm cả `asr`, `tts`. |
| **Bộ chuyển định dạng** | Chuyển dataset version sang định dạng recipe cần (COCO, YOLO, JSONL, manifest audio…). |
| **ModelServer** | Giao thức gRPC chuẩn mà container serve của recipe phơi ra cho gateway. |
| **Model Zoo** | Kho file dùng chung (trọng số pretrained đã cache, backbone, file bị gate do Admin tải lên), định danh bằng khoá + sha256; recipe tham chiếu bằng `{zoo.<khoá>}`. |
| **Thư mục model** | Bố cục chuẩn của mọi model version: `weights/`, `config/`, `classes.json`, `signature.json`, `recipe.lock.json` (D-28). |
| **Base weights** | Model gốc mà một model adapter (LoRA…) cần; khai bằng `base_weights_ref`, tải vào `{base_weights}`. |
| **Template lệnh** | Lệnh trong recipe có placeholder `{x}`, `{x?TEXT}`, `{x\|a=…}` do shim dựng ([20/08 §4](../20-kien-truc/08-recipe-va-runtime.md)). |
| **Holder** | Actor Ray giữ tài nguyên (GPU/CPU) cho một attempt; báo GPU UUID để agent chạy container recipe. |
| **mlp-net** | Container WireGuard trên mỗi PC; mọi container của nền tảng dùng chung namespace mạng của nó (D-26). |
| **Registry phân phối / registry riêng** | Registry công khai của nhà phát hành chứa image nền tảng + recipe builtin; mỗi instance đồng bộ về registry riêng bằng `sync_images` (D-31). |
| **Attempt `detached`** | Workload vẫn chạy sau khi Ray head khởi động lại nhưng không còn holder Ray; scheduler tự giữ chỗ GPU của nó (D-32). |
| **Instance Admin** | Người quản trị toàn instance (cài đặt, SMTP, recipe builtin, Model Zoo dùng chung, Ray Client) — khác Owner/Admin của workspace (D-27). |
| **Maturity** | Mức độ của recipe: `stable`, `experimental` (dùng được, có cảnh báo), `draft` (chỉ notebook). |
| **Lineage** | Đồ thị nguồn gốc dataset → run → model → deployment. |
| **Audit** | Nhật ký append-only mọi hành động thay đổi. |
| **Phase** | P0 nền tảng · P1 lõi · P2 MLOps đầy đủ · P3 nâng cao. |
