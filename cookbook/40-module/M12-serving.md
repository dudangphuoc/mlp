# M12 — Serving (suy luận)

> **Phase:** P1 realtime (batch, canary P2) · **Phụ thuộc:** M01, M02, M03, M11, M17 · **Quyết định:** D-10, D-20, D-21, D-22 · **Proto:** `serving.proto` (`ServingService`), `inference.proto` (`InferenceService`), `model_server.proto` · **Schema DB:** `serving`

## 1. Mục tiêu & phạm vi
Biến model version của **bất kỳ recipe nào** thành endpoint suy luận chạy trên các máy, truy cập qua gateway có xác thực.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Deployment từ model version; replica = container serve của recipe (`mlp-serve` + handler) | ✔ | | |
| REST JSON + gRPC `Predict`; **streaming** cho task sinh (TTS audio, sinh văn bản) | ✔ | | |
| Replica min/max, autoscale theo request đang xử lý (do control plane điều khiển) | ✔ | theo latency | |
| Rolling update, tự rollback, rollback thủ công | ✔ | | |
| Key endpoint, rate limit, metric latency/throughput/lỗi, log mẫu | ✔ | | |
| Canary, blue/green, A/B, shadow; batch inference | | ✔ | |
| Prediction log + drift | | ✔ (M13) | |
| Edge OTA | | | ✔ |

## 2. Khái niệm
**Deployment** (endpoint có slug), **revision** (mỗi lần đổi version/tài nguyên/replica), **replica** (một container serve của recipe trên một máy, giữ tài nguyên bằng holder Ray — D-21), **endpoint key** `mlpd_`, **hợp đồng suy luận** (theo task trong danh mục task — D-22).

## 3. Dữ liệu (schema `serving`)

| Bảng | Cột chính |
|---|---|
| `deployments` | id, workspace_id, slug, name, model_id (null với `assist` từ biến thể catalog), recipe, variant, kind (`standard/playground/assist`), state, current_revision_id, auth_mode, rate_limit_rps, created_by |
| `revisions` | id, deployment_id, number, model_version_id, recipe_build_id, resources (cpus, gpus, min_vram_gb, memory_gb), placement (pool, host_ids), replicas (min, max, target_inflight), runtime_options (batching, max_batch_size, timeout_ms, half, stream), state, created_by |
| `replicas` | id, revision_id, host_id, job_id (holder), overlay_ip, port, state (`starting/ready/draining/stopped/failed`), started_at, last_health_at |
| `endpoint_keys` | id, deployment_id, name, prefix, hash, created_by, revoked_at |
| `request_metrics` (hypertable) | deployment_id, revision_id, at (10 s), count, errors_4xx, errors_5xx, p50_ms, p95_ms, p99_ms, bytes_in, bytes_out, inflight_max |
| `request_samples` | deployment_id, at, status, latency_ms, input_summary, output_summary (1 %, 7 ngày, tắt được) |

## 4. Trạng thái & quy tắc
```
deployment: creating → deploying → healthy ⇄ degraded ; deploying → failed ; * → stopped → deploying
replica:    starting → ready → draining → stopped ; starting|ready → failed
```
- **R1** Mỗi replica = job loại `serve` (M03): holder Ray giữ GPU/CPU trên một máy, agent chạy container recipe build của model version, lệnh `mlp-serve --handler <recipe.serve.handler> --model <dir>`; container phơi gRPC `ModelServer` trên IP overlay của máy.
- **R2** Gateway (trong api) giữ bảng định tuyến replica `ready`, cân bằng theo số request đang xử lý; chuyển REST/gRPC công khai sang `ModelServer.Predict` / `PredictStream`.
- **R3** Autoscale: control plane đo `inflight` mỗi 10 s; vượt `target_inflight × số replica` trong 1 phút → thêm replica (≤ max); thấp trong 10 phút → bớt (≥ min).
- **R4** Rolling update: tạo replica revision mới → `ready` → chuyển dần traffic → drain replica cũ; revision mới không `ready` trong 10 phút → rollback.
- **R5** Máy chứa replica offline → replica `failed`, scheduler đặt replica mới ở máy khác; deployment `degraded` trong lúc chờ.
- **R6** Playground (từ M11) tự dừng sau 15 phút không có request.
- **R7** Replica tính vào quota GPU của pool.
- **R8** Model version phải có recipe hỗ trợ serve (khai `entrypoints.serve`); không có → không deploy được.
- **R9** Kind `assist`: deployment nội bộ cho gán nhãn (M05) từ model version **hoặc** biến thể pretrained trong catalog có `entrypoints.assist` (recipe chỉ có `assist` mà không có `serve` — vd. phân vùng tương tác — chỉ dùng được ở kind này); không có endpoint công khai, không key — chỉ api gọi qua `ModelServer`; 1 replica, tự dừng sau 30 phút không dùng, khởi lại khi cần; dữ liệu `host_local` → replica ghim máy chứa dữ liệu (D-17).
- **R10** Handler trả `TaskOutput` có kiểu ([20/08 §10](../20-kien-truc/08-recipe-va-runtime.md), toạ độ pixel); gateway chuyển thành JSON ở bảng dưới — handler không tự dựng JSON công khai.

## 5. Luồng chính
1. **Deploy**: chọn model version → tài nguyên (gợi ý từ biến thể), pool/máy, replica, batching, streaming, auth → `CreateDeployment` → scheduler tạo replica → agent kéo image recipe (nếu chưa có), khởi container → `ModelServer.Health` OK → `ready` → deployment `healthy`.
2. **Gọi**: client → `POST /v1/deployments/{ws}/{slug}/predict` (hoặc gRPC `InferenceService.Predict`, `PredictStream`) → gateway kiểm key, rate limit → replica → kết quả; gateway ghi metric.
3. **Đổi version**: `CreateRevision(model_version)` → rolling update (R4).

### Hợp đồng suy luận theo task (REST công khai; gRPC tương đương)
| Task | Vào | Ra |
|---|---|---|
| detect / segment / obb / pose | multipart `image` hoặc `{image}`; `conf`, `iou`, `imgsz` | `{predictions:[{class, classId, confidence, box, polygon?, mask_rle?, keypoints?}], imageSize}` |
| classify (ảnh/text) | ảnh hoặc `{inputs:[…]}` | `{predictions:[[{label, score}]]}` |
| ner | `{inputs:[…]}` | `{predictions:[[{entity, start, end, text, score}]]}` |
| generate (text) | `{prompt, maxTokens, temperature}`; `stream=true` → SSE-tương-đương qua chunked HTTP / gRPC stream | `{text, usage}` |
| asr | multipart `audio` (wav/mp3/flac) ; `language?` | `{text, segments:[{start, end, text, speaker?}]}` |
| **tts** | `{text, speaker?, voicePrompt?, format: wav|opus, sampleRate?}`; `stream=true` → audio theo chunk | `audio/wav` hoặc `audio/ogg` (header `X-Mlp-Duration-Ms`), hoặc stream chunk |
| tabular classify/regress | `{rows:[{col: value}]}` | `{predictions:[value | {label, probabilities}]}` |
Task mới trong danh mục (D-22) mang hợp đồng riêng; gateway chỉ chuyển tiếp theo message proto của task. Mọi response có `X-Mlp-Model-Version`, `X-Mlp-Revision`, `X-Request-Id`.

## 6. Hợp đồng

| RPC | Quyền | Mô tả |
|---|---|---|
| `ServingService.ListDeployments` / `GetDeployment` / `WatchDeployment` | `run.read` | |
| `CreateDeployment` / `UpdateDeployment` / `DeleteDeployment` | `deployment.manage` | `request_id` |
| `CreateRevision` / `RollbackDeployment` / `StopDeployment` / `StartDeployment` | `deployment.manage` | |
| `ListEndpointKeys` / `CreateEndpointKey` / `RevokeEndpointKey` | `deployment.manage` | |
| `GetDeploymentMetrics` / `ListReplicas` / `GetDeploymentLogs` / `ListRequestSamples` | `run.read` | |
| `InferenceService.Predict` / `PredictStream` / `GetMetadata` | key endpoint hoặc thành viên | Công khai |
| `ModelServer.Predict` / `PredictStream` / `Health` / `Metadata` | nội bộ (gateway → replica, qua overlay) | `model_server.proto` |
| REST `/v1/deployments/{ws}/{slug}/predict` · `GET …/{slug}` · `GET …/{slug}/health` | như trên | Ngoại lệ JSON (20/03) |

## 7. Màn hình

### S-12-1 Deployments — `/w/{ws}/deployments`
Bảng: tên, model@version, **recipe**, task, trạng thái, replica ready/tổng, request/phút, p95, lỗi %, cập nhật.

### S-12-2 Deploy (hộp thoại, mở từ model version)
Tên + slug, tài nguyên (`ResourcePicker`, gợi ý theo biến thể), pool/máy, replica min/max + `target_inflight`, batching (task hỗ trợ), streaming (task sinh), xác thực, rate limit. Recipe có cảnh báo license → hộp xác nhận (D-23).

### S-12-3 Deployment — `/w/{ws}/deployments/{d}`
```
┌ voice-api  [● healthy]  vibevoice-ft@v3 · recipe VibeVoice TTS · revision 2   [Đổi version] [Dừng] [⋯] ┐
│ Endpoint: https://cp…/v1/deployments/lab/voice-api/predict   [Copy]                                  │
│ [Tổng quan] [Thử] [Giám sát] [Replica] [Revision] [Key] [Log] [Mẫu request]                          │
│ ┌ req/phút ┐┌ p50 ┐┌ p95 ┐┌ lỗi % ┐┌ inflight ┐                                                       │
│ │ 42       ││ 1.8s││ 4.1s││ 0 %   ││ 3        │                                                       │
│ └──────────┘└─────┘└─────┘└───────┘└──────────┘                                                       │
│ Gọi thử: [curl] [Python] [JavaScript]                                                                 │
│  curl -X POST …/predict -H "Authorization: Bearer mlpd_…" -d '{"text":"Xin chào","format":"wav"}' -o out.wav │
└───────────────────────────────────────────────────────────────────────────────────────────────────────┘
```
- **Thử**: playground theo task — ảnh (vẽ kết quả), text, **audio** (thu âm/tải file cho ASR; nhập văn bản + chọn giọng → trình phát cho TTS), dòng bảng.
- **Giám sát**: request, latency p50/p95/p99, lỗi, inflight, GPU/RAM replica.
- **Replica**: bảng replica (máy, GPU, trạng thái, khởi động, health gần nhất).
- **Revision**, **Key**, **Log**, **Mẫu request** như mô tả ở mục 6.

## 8. Sự kiện
`serving.DeploymentCreated`, `DeploymentHealthy`, `DeploymentDegraded`, `DeploymentFailed`, `DeploymentStopped`, `DeploymentRevisionCreated` (M06 lineage), `DeploymentRolledBack`, `ReplicaFailed`.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `SERVE_MODEL_NOT_READY` | FAILED_PRECONDITION | Model version chưa sẵn sàng |
| `SERVE_RECIPE_NO_SERVE` | FAILED_PRECONDITION | Recipe của model không hỗ trợ phục vụ |
| `SERVE_NO_CAPACITY` | RESOURCE_EXHAUSTED | Không đủ tài nguyên trên fleet |
| `SERVE_SLUG_TAKEN` | ALREADY_EXISTS | Slug đã dùng |
| `SERVE_INVALID_INPUT` | INVALID_ARGUMENT | Đầu vào không khớp hợp đồng của task |
| `SERVE_RATE_LIMITED` | RESOURCE_EXHAUSTED | Vượt giới hạn request |
| `SERVE_UNAVAILABLE` | UNAVAILABLE | Không có replica sẵn sàng |

## 10. Nghiệm thu
- [ ] Deploy hai model detect của hai họ recipe khác nhau (chọn tại lúc test) → cả hai `healthy`, cùng hợp đồng `detect`, `curl` ảnh trả `predictions` cùng định dạng.
- [ ] (Khi catalog có recipe TTS verified có `serve`) deploy model TTS → `predict` trả WAV phát được; `stream=true` trả audio theo chunk. Streaming tối thiểu ở P1 nghiệm thu bằng task `generate`.
- [ ] Deploy một model ASR → gửi file audio trả transcript có segment.
- [ ] Deployment `assist` của một recipe có `assist.kinds: [segment]` (chọn trong cài đặt project) tự lên khi mở trình gán nhãn ảnh và tự dừng sau 30 phút không dùng; không gọi được từ ngoài.
- [ ] Đổi version khi bắn 20 req/s → không lỗi 5xx do chuyển đổi; version lỗi nạp → tự rollback.
- [ ] Tắt máy chứa replica duy nhất → `degraded`, replica mới lên ở máy khác.
- [ ] Không key → 401; key thu hồi → 401 ngay.
- [ ] Không replica nào chạy trên control plane; cổng `ModelServer` không truy cập được ngoài overlay.
