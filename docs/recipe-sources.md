# Nguồn của các recipe dựng sẵn

- Ngày rà soát: 2026-11-23.
- **Từ G7 (2026-11-24) không còn Dockerfile viết tay.** Dockerfile được sinh từ `environment` của manifest; mã nguồn repo gốc do server tải ở `source.commit` (đệm trong S3) rồi agent đưa vào build context; backbone ở `source.downloads` tải khi job chạy, không nằm trong image. Các mục bên dưới nhắc tới "Dockerfile" là mô tả trạng thái trước G7. Chi tiết: [recipe-source-sdk-plan.md](recipe-source-sdk-plan.md) mục 12.
- Cách làm: đọc cả 15 `recipes/*/mlp-recipe.yaml` và 15 `recipes/*/Dockerfile`, cùng `recipes/_shared/` và `recipes/_tasks/`. Sau đó kiểm trực tiếp trên GitHub, PyPI và Hugging Face rằng mọi commit, phiên bản gói, model và link trọng số được ghi đều tồn tại. Tất cả đều trả HTTP 200.
- **Không recipe nào được build hay chạy thử thật** (`recipes/README.md`, mục "Overall status").
- Mục 5 liệt kê những chỗ manifest và Dockerfile không khớp nhau, cùng những thứ chưa tải được. Các chỗ sửa được đã sửa cùng ngày (cột "Trạng thái").

## 1. Tóm tắt

| Recipe | Code lấy từ | Ghim ở | Trọng số lấy từ | License code |
|---|---|---|---|---|
| d-fine | [Peterande/D-FINE](https://github.com/Peterande/D-FINE) | commit `956d1709` | GitHub Releases `Peterande/storage` | Apache-2.0 |
| deimv2 | [Intellindust-AI-Lab/DEIMv2](https://github.com/Intellindust-AI-Lab/DEIMv2) | commit `f005f50c` | Google Drive | **Chỉ phi thương mại** |
| rt-detrv4 | [AIMIA-PKU/RT-DETRv4](https://github.com/AIMIA-PKU/RT-DETRv4) + [facebookresearch/dinov3](https://github.com/facebookresearch/dinov3) | commit `55fefaae` + `6876159a` | Google Drive + DINOv3 (cần Meta duyệt) | Apache-2.0 (+ DINOv3 License) |
| sam2 | [facebookresearch/sam2](https://github.com/facebookresearch/sam2) | commit `2b90b9f5` | `dl.fbaipublicfiles.com` | Apache-2.0 |
| vibevoice-asr | [microsoft/VibeVoice](https://github.com/microsoft/VibeVoice) | commit `16fb2cb1` | Hugging Face | MIT |
| vibevoice-realtime | [microsoft/VibeVoice](https://github.com/microsoft/VibeVoice) | commit `16fb2cb1` | Hugging Face | MIT (chỉ nghiên cứu) |
| vibevoice-tts | [vibevoice-community/VibeVoice](https://github.com/vibevoice-community/VibeVoice) (fork) | commit `952326dd` | Hugging Face | MIT (chỉ nghiên cứu) |
| rf-detr | PyPI `rfdetr` ([roboflow/rf-detr](https://github.com/roboflow/rf-detr)) | `1.12.1` | Thư viện tự tải | Apache-2.0; XL/2XL: PML-1.0 |
| ultralytics-yolo | PyPI `ultralytics` ([ultralytics/ultralytics](https://github.com/ultralytics/ultralytics)) | `8.4.215` | Thư viện tự tải từ GitHub Releases `ultralytics/assets` | **AGPL-3.0** |
| hf-text | PyPI `transformers` + script ví dụ của [huggingface/transformers](https://github.com/huggingface/transformers) | `5.19.0`, script ở commit `c4c4605f` (= tag `v5.19.0`) | Hugging Face | Apache-2.0 |
| whisper | PyPI `transformers` + script ví dụ của [huggingface/transformers](https://github.com/huggingface/transformers) | `5.19.0`, script ở commit `c4c4605f` (= tag `v5.19.0`) | Hugging Face (`openai/whisper-*`) | MIT / Apache-2.0 |
| hf-llm-sft | PyPI `trl[peft]` ([huggingface/trl](https://github.com/huggingface/trl)) | `1.18.1` | Hugging Face (model người dùng chọn) | Apache-2.0 |
| hf-auto | PyPI `transformers` | `4.48.3` | Hugging Face (model người dùng nhập) | Apache-2.0 |
| tabular-gbdt | PyPI `scikit-learn`, `xgboost`, `lightgbm` + script trong repo này | `1.9.1`, `3.4.1`, `4.7.0` (Python 3.12) | Không có (train từ đầu) | BSD-3 / Apache-2.0 / MIT |
| generic-onnx | PyPI `onnxruntime-gpu` + script trong repo này | `1.22.0` | Model người dùng nhập | MIT |

Tổng cộng có 7 repo git được clone ở commit cố định: 6 repo của recipe, cộng `dinov3` chỉ xuất hiện trong Dockerfile của rt-detrv4. 8 recipe chỉ dùng gói PyPI.

## 2. Chi tiết từng recipe

### 2.1 Recipe clone repo git

#### d-fine
- **Code:** `git clone https://github.com/Peterande/D-FINE.git` rồi `git checkout 956d1709314c2c6a4df6f34de232054578a7449f` (commit ngày 2026-08-19).
- **Phụ thuộc:** `requirements.txt` của repo (không ghim phiên bản), cộng `matplotlib`, `opencv-python-headless`, `onnx`, `onnxsim`, `transformers==4.48.3`, `pycocotools`.
- **Backbone tải vào image:** `PPHGNetV2_B0..B6_stage1.pth` từ `https://github.com/Peterande/storage/releases/download/dfinev1.0/`. Dockerfile kiểm SHA-256 từng file; mã băm ghi ở `source.downloads` của manifest.
- **Trọng số 9 biến thể:** cũng từ `Peterande/storage/releases/download/dfinev1.0/`.
  - n, s, m, l, x: `dfine_*_coco.pth`.
  - s, m, l, x bản obj2coco: `dfine_*_obj2coco*.pth`. Các bản này chịu thêm điều khoản của bộ dữ liệu Objects365.
- **Image nền:** `pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime`.

#### deimv2
- **Code:** `https://github.com/Intellindust-AI-Lab/DEIMv2.git` @ `f005f50ce565e63f6c298feccb51901a98c3d035` (2026-10-23).
- **Phụ thuộc:** `requirements.txt` của repo, cộng `opencv-python-headless`, `onnx`, `onnxsim`, `gdown`.
- **Backbone tải vào image:** `PPHGNetV2_B0..B6_stage1.pth`, cùng nguồn với D-FINE, có kiểm SHA-256.
- **Trọng số 8 biến thể (atto, femto, pico, n, s, m, l, x):** **Google Drive** (`gdrive://<id>`).
- **File Model Zoo cần thêm:**
  - s: `deimv2_vitt_distill`
  - m: `deimv2_vittplus_distill`
  - l: `dinov3_vits16`
  - x: `dinov3_vits16plus`
- **License:** từ 2026-08-24 (commit `bb64e5e`), cả code lẫn trọng số **chỉ dùng phi thương mại**. Biến thể L/X còn chịu thêm DINOv3 License.
- **Image nền:** `pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime`.

#### rt-detrv4
- **Code:**
  - `https://github.com/AIMIA-PKU/RT-DETRv4.git` @ `55fefaaed7efe2a5f72d0a18fd4e05965e35c292` (2026-07-06).
  - `https://github.com/facebookresearch/dinov3.git` @ `6876159a11b4df116f30f667f8c9888617df0751`, ghi ở `source.extraRepos` của manifest.
- **Phụ thuộc:** `requirements.txt` của cả hai repo, cộng `opencv-python-headless`, `onnx`, `onnxsim`, `gdown`.
- **Backbone tải vào image:** `PPHGNetV2_B0..B6_stage1.pth` (Peterande/storage), có kiểm SHA-256.
- **Trọng số 4 biến thể (s, m, l, x):** **Google Drive**.
- **File Model Zoo cần thêm:** `dinov3_vitb16` (teacher DINOv3 ViT-B/16), bắt buộc cho cả train lẫn eval. File này chỉ tải được sau khi Meta duyệt.
- **Image nền:** `pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime`.

#### sam2
- **Code:** `https://github.com/facebookresearch/sam2.git` @ `2b90b9f5ceec907a1c18123530e92e794ad901a4` (2024-12-16), cài bằng `pip install -e .` cùng `opencv-python-headless`.
- **Trọng số:** `https://dl.fbaipublicfiles.com/segment_anything_2/092824/sam2.1_hiera_{tiny,small,base_plus,large}.pt`.
- **Image nền:** `pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime`.

#### vibevoice-asr
- **Code:** `https://github.com/microsoft/VibeVoice.git` @ `16fb2cb1217c9934a886e1948ffb06120caa2df5` (2026-10-08).
- **Phụ thuộc:** `pip install -e .`, cộng `peft`, `tensorboard`, `jiwer`, và `flash-attn` nếu image chưa có.
- **Trọng số:** `hf://microsoft/VibeVoice-ASR`.
- **Image nền:** `nvcr.io/nvidia/pytorch:25.12-py3` (NVIDIA NGC).

#### vibevoice-realtime
- **Code:** cùng repo và commit với vibevoice-asr, cài `pip install -e ".[streamingtts]"` và `flash-attn`.
- **Trọng số:** `hf://microsoft/VibeVoice-Realtime-0.5B`.
- **Lưu ý từ model card:** chỉ tiếng Anh, khuyến nghị chỉ dùng cho nghiên cứu, âm thanh sinh ra có watermark.
- **Image nền:** `nvcr.io/nvidia/pytorch:24.12-py3`.

#### vibevoice-tts
- **Code:** fork cộng đồng `https://github.com/vibevoice-community/VibeVoice.git` @ `952326ddb264062466a888cf32a5b2f4e803e16e` (2026-08-29), cài `pip install -e .`, `tensorboard`, `flash-attn`.
- **Trọng số:** `hf://vibevoice/VibeVoice-1.5B`, `hf://vibevoice/VibeVoice-7B`.
- **Lưu ý:** chính fork ghi việc fine-tune là "highly experimental".
- **Image nền:** `pytorch/pytorch:2.6.0-cuda12.4-cudnn9-devel`.

### 2.2 Recipe chỉ dùng gói PyPI

#### rf-detr
- **Code:** PyPI `rfdetr[train,onnx]==1.12.1`, cộng `tensorboard`. Repo gốc: `roboflow/rf-detr`. Thêm `run_rfdetr.py` và `handler.py` viết trong repo này.
- **Trọng số 12 biến thể** (nano…2xlarge, seg-nano…seg-2xlarge): manifest để `null`, **thư viện tự tải** khi chạy, không ghim sha.
- **License:** Apache-2.0. Riêng XL và 2XL là PML-1.0: cần `rfdetr[plus]`, và tự host thương mại cần Roboflow Enterprise.
- **Image nền:** `pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime`.

#### ultralytics-yolo
- **Code:** PyPI `ultralytics==8.4.215`, cộng `onnx`, `onnxslim`.
- **Trọng số:** 10 biến thể (`yolo26{n,s,m,l,x}`, `yolo11{n,s,m,l,x}`), mỗi biến thể có 5 task (detect, segment, classify, pose, obb). Manifest chỉ ghi tên file trần như `yolo11n.pt`; **thư viện tự tải** từ GitHub Releases `ultralytics/assets`.
- **License:** **AGPL-3.0** cho cả code lẫn trọng số. Dùng trong sản phẩm phải công bố mã nguồn hoặc mua license Enterprise.
- **Image nền:** `pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime`.

#### hf-text
- **Code:** PyPI `transformers==5.19.0`, `evaluate==0.4.6`, cộng `datasets`, `seqeval`, `accelerate`, `tensorboard`, `scikit-learn` (không ghim).
- **Script tải lúc build image**, ghim theo commit `c4c4605f961877852b89b97478536bb3a26b6fd1` (commit của tag `v5.19.0`):
  - `https://raw.githubusercontent.com/huggingface/transformers/c4c4605f…/examples/pytorch/text-classification/run_classification.py`
  - `https://raw.githubusercontent.com/huggingface/transformers/c4c4605f…/examples/pytorch/token-classification/run_ner.py`
- **Trọng số:** `hf://FacebookAI/xlm-roberta-base` (MIT), `hf://google-bert/bert-base-multilingual-cased` (Apache-2.0), hoặc model người dùng tự chọn.
- **Image nền:** `pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime`.

#### whisper
- **Code:** PyPI `transformers==5.19.0`, `evaluate==0.4.6`, `jiwer==4.0.0`, cộng `librosa`, `accelerate`, `tensorboard`, `datasets[audio]`, `soundfile`.
- **Script tải lúc build image:** `https://raw.githubusercontent.com/huggingface/transformers/c4c4605f…/examples/pytorch/speech-recognition/run_speech_recognition_seq2seq.py` (commit của tag `v5.19.0`).
- **Trọng số:** `hf://openai/whisper-{tiny,base,small,medium,large-v3,large-v3-turbo}`.
- Trang chủ trong manifest đã đổi sang `huggingface/transformers`, vì code chạy là script của transformers; trọng số vẫn là `openai/whisper-*`.
- **Image nền:** `pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime`.

#### hf-llm-sft
- **Code:** PyPI `trl[peft]==1.18.1`, `transformers==5.23.0`, `peft==0.21.3`, `accelerate==1.16.0`, `datasets==5.2.0`, cộng `bitsandbytes`, `tensorboard`.
- **Trọng số:** `hf://{params.base_model}`, tức model LLM người dùng chọn; license theo model card của model đó.
- **Image nền:** `pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime`.

#### hf-auto
- **Code:** PyPI `transformers==4.48.3`, cộng `accelerate<1.4`, `timm<1.1`, `torchaudio==2.5.1`, `pillow`, `scipy`.
- **Trọng số:** `hf://{params.model_id}`, tức model người dùng nhập.
- **Image nền:** `pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime`.

#### tabular-gbdt
- **Code:** PyPI `scikit-learn==1.9.1`, `xgboost==3.4.1`, `lightgbm==4.7.0`, cộng `pandas`, `pyarrow`, `joblib`; `train.py` và `handler.py` viết trong repo này.
- **Trọng số:** không có (train từ đầu).
- **Image nền:** `python:3.12-slim` (trước là 3.11, nhưng `xgboost==3.4.1` đòi Python ≥ 3.12 nên image cũ không build được).

#### generic-onnx
- **Code:** PyPI `onnxruntime-gpu==1.22.0`, `onnx==1.18.0`, `openvino==2025.0.0`, `tensorrt-cu12==10.7.0`, `psutil==7.0.0`, `numpy`, `pillow`; `handler.py` và `inspect_onnx.py` viết trong repo này.
- **Trọng số:** file `.onnx` người dùng nhập.
- **Image nền:** `nvidia/cuda:12.6.3-cudnn-runtime-ubuntu24.04`.

### 2.3 Thư mục dùng chung (code viết trong repo này, không lấy từ ngoài)

| Thư mục | Nội dung |
|---|---|
| `recipes/_shared/detr_family/` | `make_config.py`, `handler.py`, `inspect_recipe.py` dùng chung cho d-fine, deimv2, rt-detrv4 |
| `recipes/_shared/converters/` | `to_coco.py`, `vibevoice_asr_dir.py`, `vibevoice_tts_jsonl.py` |
| `recipes/_shared/classes/` | `coco80.json` |
| `recipes/_tasks/tasks.yaml` | Danh mục task, bộ chuyển định dạng, metric chuẩn |
| `recipes/_schema/` | JSON Schema của manifest |

## 3. Mọi nơi bên ngoài được tải về

| Nơi | Dùng cho |
|---|---|
| github.com (git clone) | D-FINE, DEIMv2, RT-DETRv4, dinov3, sam2, microsoft/VibeVoice, vibevoice-community/VibeVoice |
| github.com Releases `Peterande/storage` | Trọng số D-FINE; backbone HGNetV2 cho d-fine, deimv2, rt-detrv4 |
| github.com Releases `ultralytics/assets` | Trọng số YOLO (thư viện tự tải) |
| raw.githubusercontent.com `huggingface/transformers` @ commit `c4c4605f` (= tag `v5.19.0`) | Script train của hf-text và whisper |
| dl.fbaipublicfiles.com | Trọng số SAM 2.1 |
| Google Drive | 12 biến thể: DEIMv2 ×8, RT-DETRv4 ×4 |
| Hugging Face Hub | whisper ×6, hf-text ×2, VibeVoice ×4, cùng model người dùng chọn cho hf-llm-sft và hf-auto |
| Kho của Roboflow (thư viện tự tải) | Trọng số RF-DETR |
| Model Zoo (cần tải tay, Meta duyệt) | `dinov3_vitb16`, `dinov3_vits16`, `dinov3_vits16plus`, `deimv2_vitt_distill`, `deimv2_vittplus_distill` |
| PyPI | Mọi gói pip ở mục 2 |
| Docker Hub | `pytorch/pytorch` (2.5.1, 2.6.0-devel, 2.8.0), `python:3.12-slim`, `nvidia/cuda:12.6.3` |
| NVIDIA NGC (`nvcr.io`) | `nvidia/pytorch:25.12-py3`, `nvidia/pytorch:24.12-py3` |

## 4. Đã kiểm tồn tại (2026-11-23)

| Loại | Kết quả |
|---|---|
| 7 commit git (gồm `dinov3@6876159a`) | Đều có trên GitHub |
| 20 phiên bản gói PyPI được ghim | Đều có trên PyPI |
| 12 model Hugging Face ghi sẵn trong manifest | Đều có trên Hub |
| Link mẫu: `dfine_n_coco.pth`, `dfine_l_obj2coco_e25.pth`, `PPHGNetV2_B0_stage1.pth`, `sam2.1_hiera_tiny.pt`, `yolo11n.pt` | Đều tải được |
| 12 link Google Drive | **Chưa kiểm**: nền tảng chưa có bộ tải `gdrive://` |
| File Model Zoo | **Chưa kiểm**: cần tài khoản đã được Meta duyệt |

## 5. Chỗ lệch và chỗ còn thiếu (kết quả rà soát)

Manifest có thêm ba trường tuỳ chọn trong `source` (đã thêm vào `recipes/_schema/mlp-recipe.schema.json`) để ghi những gì Dockerfile làm ngoài nguồn chính:
- `extraRepos`: repo git phụ kèm commit;
- `downloads`: file tải lúc build, có SHA-256 khi Dockerfile kiểm;
- `packages`: gói pip cài thêm.

Sau khi sửa, `mlp recipe check recipes` báo OK cả 15 recipe.

| # | Recipe | Vấn đề | Trạng thái |
|---|---|---|---|
| 1 | generic-onnx | Manifest ghi `onnxruntime-gpu==1.32.0`, Dockerfile cài `1.22.0` cùng `onnx`, `openvino`, `tensorrt`, `psutil` không có trong manifest | **Đã sửa:** manifest ghi đúng gói Dockerfile cài. Giữ `1.22.0` vì đi cùng TensorRT 10.7 và CUDA 12.6 của image; lên 1.32 cần build thử |
| 2 | hf-auto | `recipes/README.md` ghi `transformers==5.19.0`, thực tế `4.48.3` | **Đã sửa** README; manifest ghi đủ gói đi kèm |
| 3 | rt-detrv4 | Repo `dinov3@6876159a` chỉ có trong Dockerfile | **Đã sửa:** ghi ở `source.extraRepos` |
| 4 | d-fine, deimv2, rt-detrv4 | Backbone HGNetV2 tải vào image nhưng manifest không ghi, cũng không kiểm nội dung | **Đã sửa:** ghi URL, đường dẫn, SHA-256 ở `source.downloads`; Dockerfile chạy `sha256sum -c`, file sai thì build dừng |
| 5 | deimv2, rt-detrv4 | 12 biến thể trọng số trên Google Drive, nền tảng chưa tải được | **Chưa sửa:** theo Q8 phải chép các file đó sang một repo model trên Hugging Face hoặc nơi có URL `https://` cố định. Việc này là tải dữ liệu lên dịch vụ bên ngoài, cần chủ dự án chọn tài khoản và đồng ý |
| 6 | deimv2 (s, m, l, x), rt-detrv4 | Cần file Model Zoo (DINOv3 cần Meta duyệt) | **Chưa sửa:** cần tài khoản được Meta duyệt; Model Zoo chưa hiện thực |
| 7 | whisper, hf-text, vibevoice-* | Biến thể `hf://` chưa được shim tải | **Chưa sửa:** đây là thiếu tính năng (phần recipe của G3), không phải chỗ lệch giữa hai file |
| 8 | Nhiều recipe | Gói không ghim phiên bản | **Đã ghim:** mỗi recipe có `constraints.txt`, Dockerfile đặt `PIP_CONSTRAINT` trước lệnh `pip install` đầu tiên nên mọi gói (kể cả `requirements.txt` của repo gốc) cài đúng phiên bản. Cách làm ở mục 6 |
| 9 | hf-llm-sft, whisper, rf-detr, ultralytics-yolo | Dockerfile cài gói mà manifest không ghi | **Đã sửa:** thêm `bitsandbytes`; `datasets[audio]`, `soundfile`; `tensorboard`; `onnx`, `onnxslim` |
| 10 | rf-detr, ultralytics-yolo | Trọng số do thư viện tự tải theo tên, không ghim SHA | **Chưa sửa:** đổi sang URL có SHA-256 làm đổi cách recipe chạy (thư viện tự tìm file theo tên), cần sửa cùng shim và thử train thật |
| 11 | hf-text, whisper | Script train lấy theo tag | **Đã sửa:** ghim theo commit `c4c4605f` (commit của tag `v5.19.0`), đã kiểm 3 URL tải được; ghi ở `source.downloads` |
| 12 | whisper | Trang chủ ghi `openai/whisper` | **Đã sửa:** đổi sang `huggingface/transformers` kèm chú thích |

Image của các recipe có Dockerfile đã sửa (d-fine, deimv2, rt-detrv4, hf-text, whisper) sẽ có mã băm mới, nên máy cụm dựng lại ở lần dùng đầu. Chưa recipe nào từng được dựng, nên không ảnh hưởng máy nào.

## 6. Ghim phiên bản gói (2026-11-23)

### Cách làm

Không build image thật. Các image `pytorch/pytorch` chỉ có bản amd64, máy dev là ARM nên phải giả lập, rất chậm và nóng máy; còn lệnh SSH sang máy GPU Windows (amd64) bị chặn quyền. Thay vào đó:

1. Kéo từng image nền bản amd64, chỉ chạy `python -V` và `pip list --format=freeze`, lưu vào `tools/recipe-locks/bases/`.
2. `tools/recipe-locks/lock.py` dùng `uv pip compile` giải toàn bộ lệnh `pip install` của từng Dockerfile:
   - đích Linux x86_64 (`x86_64-manylinux_2_28`), đúng Python của image nền;
   - lần đầu khoá **mọi** gói có sẵn của image nền;
   - nếu xung đột, chỉ khoá torch, torchvision, torchaudio, triton, `nvidia-*` và `flash-attn`; các gói khác được nâng hoặc hạ như `pip` sẽ làm.
3. Ghi `recipes/<recipe>/constraints.txt`. Gói có sẵn trong image giữ nguyên phiên bản thật (kể cả bản torch riêng của NVIDIA như `2.10.0a0+…nv25.12`). Gói cài từ mã nguồn của recipe (`sam-2`, `vibevoice`) không ghim, vì chúng đã ghim theo commit.
4. Dockerfile: `COPY <recipe>/constraints.txt` và `ENV PIP_CONSTRAINT=…` ngay trước lệnh `pip install` đầu tiên.
5. Lớp nền tảng api thêm vào image (`grpcio`, `grpcio-tools`, `protobuf`) chạy với `PIP_CONSTRAINT=` rỗng, để không xung đột với bản ghim của recipe.

Tạo lại sau khi sửa gói của một Dockerfile:

```bash
uv run --no-project python tools/recipe-locks/lock.py whisper
```

### Kết quả

| Recipe | Python | Số gói ghim | Gói của image nền |
|---|---|---|---|
| d-fine | 3.11 | 66 | giữ nguyên |
| deimv2 | 3.11 | 75 | giữ nguyên |
| rt-detrv4 | 3.11 | 87 | giữ nguyên |
| sam2 | 3.11 | 34 | giữ nguyên |
| hf-auto | 3.11 | 44 | giữ nguyên |
| hf-llm-sft | 3.11 | 74 | nới (transformers 5 cần `huggingface-hub` và `click` mới hơn bản có sẵn) |
| hf-text | 3.11 | 91 | nới (như trên) |
| whisper | 3.11 | 106 | nới (như trên) |
| rf-detr | 3.11 | 112 | giữ nguyên |
| ultralytics-yolo | 3.11 | 59 | giữ nguyên |
| vibevoice-tts | 3.11 | 136 | giữ nguyên |
| vibevoice-asr | 3.12 | 129 | nới (VibeVoice cần `transformers<5`, `huggingface-hub<1`, image có bản mới hơn) |
| vibevoice-realtime | 3.12 | 116 | giữ nguyên |
| tabular-gbdt | 3.12 | 14 | (image không có gói nào) |
| generic-onnx | 3.12 | 18 | (image không có Python) |

### Lỗi và rủi ro tìm thấy trong lúc ghim

| Recipe | Phát hiện | Đã làm |
|---|---|---|
| tabular-gbdt | `xgboost==3.4.1` đòi Python ≥ 3.12 nhưng image dùng `python:3.11-slim`: image **không build được** | Đổi sang `python:3.12-slim`, manifest ghi Python 3.12 |
| whisper | `datasets[audio]` kéo `torchcodec` bản mới nhất (0.17), mà gói này phải khớp torch (torch 2.8 ↔ torchcodec 0.7) | Ghim `torchcodec==0.7.0` |
| vibevoice-tts | `flash-attn` được build từ mã nguồn, không ghim | Ghim `flash-attn==2.7.4.post1` (hỗ trợ torch 2.6) |
| generic-onnx | Các gói TensorRT trên PyPI chỉ là bản giữ chỗ, tải bản thật từ `pypi.nvidia.com` | Ghim tay `tensorrt-cu12`, `-libs`, `-bindings` ở `10.7.0`; phụ thuộc của chúng do NVIDIA quyết định |

### Kết quả build thật (máy GPU, 2026-11-23/24)

Đã build cả 15 image trên máy GPU (amd64), rồi so `pip list` trong image với `constraints.txt`.

| Recipe | Build | Dung lượng | Gói lệch bản ghim |
|---|---|---|---|
| d-fine, deimv2, rt-detrv4, hf-auto | được | — | 0 |
| hf-llm-sft, hf-text, whisper, rf-detr, ultralytics-yolo | được | 4,5 GB (yolo) | 0 |
| tabular-gbdt | được | nhỏ | 0 |
| generic-onnx | được | 4,6 GB | 0 |
| vibevoice-tts | được | 8,3 GB | 0 |
| sam2 | được sau khi thêm `--no-build-isolation` | 3,6 GB | 0 |
| vibevoice-realtime | được sau khi hạ `accelerate` xuống 1.15.0 | 11,6 GB | 0 |
| vibevoice-asr | được | 10,0 GB | 0 |

- Hai image `nvcr.io/nvidia/pytorch` cài CUDA và triton ngoài pip, nên các bản ghim `nvidia-*-cu12` và `triton` không xuất hiện trong `pip list`. Chúng chỉ có tác dụng chặn pip kéo bản khác.
- Hai bản ghim thêm sau lần build (`einops==0.8.2` cho vibevoice-tts, `nvidia-cuda-runtime-cu12==12.9.79` cho generic-onnx) chỉ ghi lại đúng bản đã có trong image, chưa build lại với file mới.
- Thư mục build và các image trên máy GPU đã xoá sau khi kiểm.

## 7. Repo, model và dữ liệu dùng để thử trong G0–G5 (không phải recipe)

| Dùng ở | Nguồn | Mục đích |
|---|---|---|
| G0 | `python:3.12-slim` (Docker Hub); debugpy 1.8.20 đi kèm extension VS Code | Container đóng vai máy cụm; debug |
| G1, G2a | Repo git cục bộ tự tạo trong thư mục nháp (`fire-train`) | Script mẫu; remote giả `github.com/blogic/fire-train` chỉ để kiểm việc gỡ token khỏi URL |
| G2a | PyPI `six==1.16.0` | Kiểm việc dựng môi trường từ `requirements.txt` |
| G2b | [octocat/Spoon-Knife](https://github.com/octocat/Spoon-Knife) @ `d0dd1f61` | Kiểm việc server tự kéo code từ GitHub |
| G3 | [hf-internal-testing/tiny-random-bert](https://huggingface.co/hf-internal-testing/tiny-random-bert) @ `f171d7ba`; `meta-llama/Llama-3.2-1B` | Model nhỏ để tải và dùng lại; model gated để kiểm báo lỗi (không tải được, đúng như mong đợi) |
| G5 | [karpathy/micrograd](https://github.com/karpathy/micrograd) @ `7bc720e9` (master) và `c34adac4` | Chạy repo từ web theo branch và theo commit |
| Kịch bản kiểm thử của dự án | `tools/MLPlatform.Scenarios/fixtures/toy-recipe`, `gpu-recipe` | Recipe giả, chỉ dùng cho kiểm thử nội bộ |
