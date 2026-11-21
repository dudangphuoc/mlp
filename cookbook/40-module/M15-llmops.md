# M15 — LLMOps

> **Phase:** P3 · **Phụ thuộc:** M05, M09, M11, M12 · **Proto:** `llmops.proto`  (`LlmOpsService`) · **Schema DB:** `llmops`
> **Trạng thái spec:** lát cắt đầu (prompt registry, guardrail luật, token/chi phí, eval theo tiêu chí xác định) đã hiện thực và mô tả ở mục 2–10; LLM-as-judge, faithfulness RAG, toxicity bằng model, QLoRA/DPO và recipe vLLM/SGLang viết tiếp khi làm.

## 1. Mục tiêu & phạm vi
Quản lý vòng đời ứng dụng LLM: prompt, đánh giá, an toàn nội dung, theo dõi token.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Prompt registry có version, biến, liên kết model; so sánh output giữa version |  |  | ✔ |
| Eval: bộ test, LLM-as-judge, hallucination/faithfulness (RAG), toxicity |  |  | ✔ |
| Guardrails tại gateway: PII, nội dung không an toàn, prompt injection |  |  | ✔ |
| Đếm token, latency, chi phí ước tính (chỉ quan sát, không thu phí — D-02) |  |  | ✔ |
| Fine-tune SFT/LoRA bằng recipe LLM (đã có ở P1 qua recipe `hf-llm-sft` + M09) | ✔ |  |  |
| QLoRA/DPO, recipe Unsloth/Axolotl, serve bằng recipe vLLM/SGLang; dữ liệu preference từ M05 |  |  | ✔ |

## 2. Khái niệm
- **Prompt** có tên trong workspace; mỗi **version** bất biến gồm `system` (tuỳ chọn), `template` với biến `{{ten}}` (tự nhận diện), deployment LLM mặc định (M12), `temperature`, `max_tokens`, ghi chú.
- **Chạy prompt**: điền biến → gửi `system` + `user` tới replica qua `ModelServer.Predict` (input `chat`, output `generate`). **So sánh version** = chạy cùng biến trên hai version cạnh nhau.
- **Guardrail** (theo deployment): PII (email, SĐT VN, CCCD/CMND, thẻ) `off | redact | block`; chặn prompt injection (mẫu tiếng Anh + Việt); từ cấm; tuỳ chọn kiểm cả đầu ra. Áp ở **mọi** đường gọi: RPC chạy prompt, eval, và REST gateway `{"messages":[…]}`.
- **Usage**: mỗi lượt gọi ghi token vào/ra (model không báo → ước lượng theo từ), độ trễ, chi phí ước tính = token × giá/1 000 (chỉ quan sát, D-02), có bị chặn hay không, cờ guardrail.
- **Eval**: bộ test `[{vars, expect:{contains, not_contains, equals, regex, max_chars}}]`; điểm = tỉ lệ case đạt mọi điều kiện.

## 3. Dữ liệu (schema `llmops`)
| Bảng | Cột chính |
|---|---|
| `prompts` | id, workspace_id, name, description, created_by |
| `prompt_versions` | prompt_id, version, system, template, variables jsonb, deployment_id, temperature, max_tokens, note |
| `settings` | deployment_id, pii_mode, block_injection, blocked_terms jsonb, check_output, price_per_1k |
| `usage` | id, workspace_id, deployment_id, prompt_id, prompt_version, source (`gateway/prompt/eval`), at, prompt_tokens, completion_tokens, latency_ms, cost, blocked, flags jsonb |
| `eval_sets` / `eval_runs` | bộ test (cases jsonb) / kết quả (score, results jsonb theo case) |

## 4. Trạng thái & quy tắc
- **R1** Version prompt bất biến; sửa = tạo version mới.
- **R2** Thiếu biến khi chạy → `LLM_PROMPT_VARIABLE_MISSING`, không gửi gì tới model.
- **R3** Bị guardrail chặn đầu vào → không gọi model, vẫn ghi usage `blocked`; RPC trả `blocked` + lý do, REST trả 400 `LLM_GUARDRAIL_BLOCKED`. Che PII thay giá trị bằng nhãn (`[EMAIL]`, `[SĐT]`, …) trước khi gửi.
- **R4** Chi phí chỉ để quan sát (D-02), không thu phí.

## 5. Luồng chính
1. Deploy một model LLM (M12, recipe có handler chat) → đặt guardrail + giá.
2. Tạo prompt → các version → chạy/so sánh với cùng biến.
3. Tạo bộ test → chạy eval cho từng version → chọn version có điểm cao.
4. Client gọi REST `{"messages":[…]}` → guardrail + usage; theo dõi token/chi phí theo ngày.

## 6. Hợp đồng (`llmops.proto` — `LlmOpsService`)
| RPC | Quyền | Mô tả |
|---|---|---|
| `CreatePrompt` / `CreatePromptVersion` | `model.write` | |
| `ListPrompts` / `GetPrompt` | `run.read` | |
| `RunPrompt` | `run.read` | Trả output, token, độ trễ, chi phí, cờ guardrail, message đã gửi (sau khi che) |
| `GetGuardrails` / `UpdateGuardrails` | đọc `run.read` / ghi `deployment.manage` | |
| `GetUsage` | `run.read` | Theo ngày × deployment |
| `CreateEvalSet` / `ListEvalSets` / `RunEval` / `ListEvalRuns` | `model.write` (tạo) / `run.read` | |
| REST `POST …/predict` với `messages` | khoá `mlpd_` | `{"output":{"text"},"usage":{…},"flags":[…]}` |

## 7. Màn hình (phác thảo)
Prompt registry (danh sách, editor có biến, so sánh version); trang eval (bộ test, kết quả theo tiêu chí); cấu hình guardrail theo deployment; bảng token usage.

## 8. Sự kiện
_Chưa có_ (P3 tiếp: cảnh báo vượt ngân sách token, tỉ lệ bị chặn cao → M16).

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `LLM_PROMPT_NOT_FOUND` / `LLM_PROMPT_VERSION_NOT_FOUND` | NOT_FOUND | — |
| `LLM_PROMPT_NAME_TAKEN` | ALREADY_EXISTS | Tên prompt đã dùng |
| `LLM_PROMPT_VARIABLE_MISSING` | INVALID_ARGUMENT | Thiếu biến |
| `LLM_NO_DEPLOYMENT` | INVALID_ARGUMENT | Version chưa gắn deployment |
| `LLM_NOT_GENERATIVE` | INVALID_ARGUMENT | Deployment không trả output sinh văn bản |
| `LLM_GUARDRAIL_BLOCKED` | 400 (REST) | Bị guardrail chặn |

## 10. Nghiệm thu
- [x] Hai version prompt cho output khác nhau trên cùng biến; thiếu biến bị từ chối (kịch bản M12+M13+M15).
- [x] PII (email, SĐT) bị che trước khi tới model; injection và từ cấm bị chặn ở RPC lẫn REST.
- [x] Eval 2 case cho điểm 0.5; usage ghi đủ lượt, token, chi phí, số bị chặn.
- [ ] LLM-as-judge, faithfulness/hallucination cho RAG, toxicity bằng model phân loại.
- [ ] QLoRA/DPO, recipe Unsloth/Axolotl, serve vLLM/SGLang; dữ liệu preference từ M05.
