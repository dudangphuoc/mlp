# MLPlatform starter recipes

Each directory is a **recipe** (D-20): `mlp-recipe.yaml` + `Dockerfile` + glue code (handler, scripts) when needed. These are **examples**, not limits (D-25): users can import more recipes from any repo (M17 S-17-3).

- Field reference + template grammar + placeholders: [cookbook 20/08](../cookbook/20-kien-truc/08-recipe-va-runtime.md) §3–§4.
- Schema: [`_schema/mlp-recipe.schema.json`](_schema/mlp-recipe.schema.json). Task catalog + standard converters: [`_tasks/tasks.yaml`](_tasks/tasks.yaml).
- Business rules, Model Zoo, UI: [M17](../cookbook/40-module/M17-model-catalog.md).

**Overall status:** commits, licenses, commands and paths in the manifests were written from **reading the source** on **2026-11-17**; some metric keys and behaviors are only inferred from the source — noted in each recipe's `openItems`. **No recipe has been built or smoke-tested yet** — `verification.built/smokeTested: false`.

## Catalog

| Recipe | Task | Source (pinned) | License | Warning (D-23) | Maturity | Handler |
|---|---|---|---|---|---|---|
| [rt-detrv4](rt-detrv4/) | detect | AIMIA-PKU/RT-DETRv4 @55fefaa | Apache-2.0 code; checkpoint license not stated; DINOv3 License teacher | 🔒 zoo file `dinov3_vitb16` (Meta approval) for train **and** eval | stable | `_shared/detr_family` |
| [d-fine](d-fine/) | detect | Peterande/D-FINE @956d170 | Apache-2.0 (Objects365 variants: dataset terms) | — | stable | `_shared/detr_family` |
| [deimv2](deimv2/) | detect | Intellindust-AI-Lab/DEIMv2 @f005f50 | **Non-commercial only** (since 2026-08-24) | ✔ · 🔒 S–X backbones in the Model Zoo | stable | `_shared/detr_family` |
| [rf-detr](rf-detr/) | detect, segment | `rfdetr==1.12.1` | Apache-2.0; XL/2XL: PML-1.0 | per variant | stable | ✔ (box + polygon) |
| [ultralytics-yolo](ultralytics-yolo/) | detect, segment, classify, pose, obb | `ultralytics==8.4.215` | **AGPL-3.0** | ✔ | stable | ✔ (including obb) |
| [sam2](sam2/) | assist `segment` (assist only) | facebookresearch/sam2 @2b90b9f | Apache-2.0 | — | stable | ✔ (assist) |
| [hf-text](hf-text/) | text-classify, ner | transformers 5.19.0 (scripts pinned to the tag) | Apache-2.0; models per card | custom variant | stable | ✔ |
| [hf-llm-sft](hf-llm-sft/) | generate | `trl[peft]==1.18.1` | Apache-2.0; LLMs per card | ✔ | stable | ✔ (stream, adapter) |
| [hf-auto](hf-auto/) | every task with an HF pipeline (serve/import) | `transformers==5.19.0` | per model card | ✔ | stable | ✔ |
| [whisper](whisper/) | asr | transformers 5.19.0 + seq2seq script pinned to the tag | MIT | — | stable | ✔ |
| [vibevoice-asr](vibevoice-asr/) | asr (train only) | microsoft/VibeVoice @16fb2cb | MIT | — | experimental | ✖ not yet — no `serve` declared |
| [vibevoice-tts](vibevoice-tts/) | tts (train + batch predict) | vibevoice-community/VibeVoice @952326d | MIT | ✔ (research, no voice impersonation…) | experimental | ✖ not yet — no `serve` declared |
| [vibevoice-realtime](vibevoice-realtime/) | tts (batch predict only) | microsoft/VibeVoice @16fb2cb | MIT | ✔ | experimental | ✖ not yet — no `serve` declared |
| [tabular-gbdt](tabular-gbdt/) | tabular-classify, tabular-regress | sklearn, xgboost, lightgbm (script in the recipe) | BSD/Apache/MIT | — | stable | ✔ |
| [generic-onnx](generic-onnx/) | classify, detect, tabular (serve/import `.onnx`, with `inspect`) | `onnxruntime-gpu==1.32.0` | MIT (runtime) | — | stable | ✔ |

## Directory layout
- `_schema/` — JSON Schema of the manifest.
- `_tasks/tasks.yaml` — tasks, standard converters, standard metrics.
- `_shared/detr_family/` — `make_config.py` (generates train/eval configs: dataset, class count, epoch schedule scaling, `--val-split test` for eval) + the shared handler for D-FINE/DEIMv2/RT-DETRv4.
- `_shared/converters/` — recipe-provided converters (`vibevoice-asr-dir`, `vibevoice-tts-jsonl`), run inside the recipe image.
- `_shared/classes/` — shared class lists for `variants.classes` (e.g. `coco80`).

## Build
- The build context is the `recipes/` directory; every `COPY` in a Dockerfile uses paths relative to it: `docker build -f d-fine/Dockerfile recipes/`.
- Builtin recipes are built by CI and pushed to the registry (D-30); the platform adds a final layer with `mlp-shim`, `mlplatform-sdk`, `mlp-serve` (which provides the `mlp_serve.types` that handlers import).
- Dev machines: build one image at a time, with `nice`, never in parallel (CLAUDE.md).

## Conventions used in these manifests (standard defined in 20/08)
| Syntax/feature | Example |
|---|---|
| `{x?TEXT}` — inserts `TEXT` when the value is truthy; values that may be empty **must** use this form | `{params.use_amp?--use-amp}`, `{weights?-t {weights}}`, `{params.language?--language={params.language}}` |
| `{x\|a=…\|b=…\|*=…}` — choose by value | `{task\|text-classify=run_classification.py …\|ner=run_ner.py …}`, `{gpu.supports_bf16\|true=--bf16\|false=--fp16}` |
| `{{ }}` — literal braces | `--extra-json '{{"teacher_model.dinov3_weights_path": …}}'` (rt-detrv4) |
| `resume: {mode: append\|replace\|command}` | DETR: `replace -t {weights}` → `-r {checkpoint}`; YOLO: `command` |
| per-task `data.input`, per-task `variants.weights` | ultralytics-yolo, hf-text |
| `data.splitNames` | rf-detr (`val` → `valid`) |
| `requiresFiles` from the Model Zoo | rt-detrv4, deimv2 |
| `artifacts.best: {from, as}` + `files` + `baseWeights: "{weights.ref}"` | every training recipe; adapters: hf-llm-sft, vibevoice-*; inference-only recipes declare `best: {as}` |
| `train.fromScratch`, `export.outputs`, `predict.inputs`, `entrypoints.inspect` | DETR · DETR · vibevoice-tts/realtime · generic-onnx |
| Handler also runs a temporary model directory built from the variant (`config/variant.json`) | DETR, rf-detr, yolo, sam2, hf-auto |
| Eval-specific metrics (`entrypoints.eval.metrics`, `test/` prefix) | DETR, hf-text, whisper, rf-detr, yolo, tabular |
| `distributed: internal` + `{gpu_ids_csv}` | ultralytics-yolo |

## Next steps for each recipe
1. Build the image (one at a time) and fix the Dockerfile until it builds.
2. M17 R2 smoke test: convert a sample dataset → train a few steps → metrics arrive → model directory has the right layout → predict 1 sample.
3. Finish the 3 VibeVoice handlers (read the demo code in the built image), redeclare `serve`/`assist`, then raise the maturity.
4. Update `verification` and remove resolved `openItems`.
