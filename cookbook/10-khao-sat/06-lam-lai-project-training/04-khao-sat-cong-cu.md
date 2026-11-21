# 04 — Khảo sát công cụ: canvas node và công cụ train có giao diện

Vai: nhà phân tích sản phẩm. Nhãn nguồn: **[Tra]** = đã tra web trong phiên này (có URL); **[Nghề]** = biết từ thực tế, chưa tra lại; **[Đoán]** = suy luận; **[Repo]** = đã đọc trong repo. Không lặp lại khảo sát Ultralytics ở `cookbook/10-khao-sat/`.

## 0. Kết luận trước

1. Thứ làm n8n "dễ hiểu" **không phải canvas tự do** mà là bốn cơ chế: xem dữ liệu thật vào/ra của từng node, chạy riêng một bước, ghim dữ liệu, đánh dấu node đã cũ. Bốn thứ này chuyển được sang train; phần kéo dây tự do thì không.
2. Công cụ train thành công nhất với người không chuyên ML (Edge Impulse, Teachable Machine, Roboflow, SageMaker Canvas) đều dùng **luồng có khung**, không ai dùng canvas tự do. Công cụ canvas tự do (KNIME, Orange, Azure Designer, Dataiku) sống ở ML bảng/ETL, nơi mỗi node thật sự là một hàm chạy độc lập trong vài giây.
3. Bảy node trong đề xuất **không khớp ranh giới thực thi thật** của sản phẩm [Repo]. Đây là rủi ro lớn nhất: canvas đẹp nhưng nói sai về cái đang chạy.

## 1. Khảo sát theo công cụ

| Công cụ | Cách giải bài "thấy và chỉnh quá trình" | Nguồn |
|---|---|---|
| **n8n** | "Execute step" chạy node đó cùng các node phía trước cần cho đầu vào. Ghim dữ liệu đầu ra để khỏi gọi lại nguồn; **lần chạy thật bỏ qua mọi dữ liệu ghim**. "Dirty node": node từng chạy xong nhưng kết quả đã cũ → viền đổi màu, tam giác vàng thay dấu tích xanh, rê chuột thấy **lý do** (đổi tham số, chèn/xoá node trước nó, bỏ ghim…). Ngăn chi tiết node ba cột: đầu vào · tham số · đầu ra [Nghề]. Lần chạy cũ mở lại trên chính canvas ở chế độ chỉ đọc, có nút đưa về trình soạn [Nghề]. | [Tra] docs.n8n.io/workflows/executions/dirty-nodes · docs.n8n.io/workflows/executions/manual-partial-and-production-executions · docs.n8n.io/data/data-pinning |
| **ComfyUI** | Đệm đầu ra node theo chữ ký đầu vào: đổi một node thì chỉ node đó và phía sau chạy lại (nguồn bên thứ ba + [Nghề], mức chắc: cao). Chạy một nhánh: chọn node đầu ra → nút tam giác. Ảnh xem trước và thanh tiến độ nằm ngay trên node [Nghề]. Tháng 8/2025 phải thêm **subgraph** để gói nhóm node vì workflow thành mì rối. | [Tra] docs.comfy.org/interface/features/partial-execution · docs.comfy.org/interface/features/subgraph · blog.comfy.org/p/subgraph-official-release |
| **Edge Impulse** | "Impulse" = khung cố định: khối đầu vào → khối xử lý → khối học → đầu ra. Mỗi khối có trang riêng; **Feature explorer** vẽ đặc trưng 2D/3D, bấm vào điểm ra mẫu gốc. Ước tính latency/RAM/flash theo thiết bị đích **trước khi triển khai**. EON Tuner tìm tổ hợp khối vừa ngân sách thiết bị. **Expert mode**: bật công tắc là thấy và sửa toàn bộ mã Keras của khối. | [Tra] docs.edgeimpulse.com/docs/edge-impulse-studio/learning-blocks/expert-mode · docs.edgeimpulse.com/studio/projects/dashboard/target-device · docs.edgeimpulse.com/nordic/edge-impulse-studio/processing-blocks/feature-explorer |
| **Dataiku** | Recipe trực quan và recipe code (Python/R/SQL) nằm chung một flow. Build có chiến lược: "chỉ phần đã đổi" (mặc định) hoặc ép dựng lại toàn bộ phía trước. Giới hạn đã ghi nhận: **không kiểm được schema đầu ra của recipe code nếu chưa chạy**; "chỉ phần đã đổi" không nhận ra thay đổi biến. | [Tra] doc.dataiku.com/dss/14/flow/building-datasets.html · doc.dataiku.com/dss/12/schemas/recipes.html |
| **KNIME** | Đèn giao thông dưới mỗi node: đỏ (chưa cấu hình) → vàng (sẵn sàng) → xanh (đã chạy), có dấu lỗi. **Reset một node thì mọi node phía sau tự về vàng và mất đầu ra**. Component/metanode để gói [Nghề]. | [Tra] docs.knime.com/latest/analytics_platform_user_guide/ |
| **Orange** | Widget tự cập nhật khi đầu vào đổi; widget Python Script nhận tín hiệu vào thành biến `in_data`…, có console riêng. Hợp dữ liệu nhỏ, phản hồi tức thì. | [Tra] orange3.readthedocs.io/projects/orange-visual-programming/en/latest/widgets/data/pythonscript.html |
| **Azure ML Designer** | Mặc định dùng lại đầu ra nếu component và tham số không đổi; ô "Regenerate output" để ép chạy lại. Có hẳn trang hướng dẫn **gỡ lỗi vì sao không được dùng lại**. Studio (classic) kéo-thả thuần đã ngừng 31/8/2024; Designer mới khuyên dùng component tự viết bằng code, hai loại component không tương thích nhau. | [Tra] learn.microsoft.com/en-us/azure/machine-learning/how-to-debug-pipeline-reuse-issues · learn.microsoft.com/en-us/lifecycle/products/machine-learning-studio-classic |
| **Kubeflow / Vertex Pipelines** | Đồ thị là **hình chiếu chỉ đọc của code**, không soạn trên canvas. Bước lấy từ đệm có biểu tượng riêng. Nhân bản run và so sánh run trong UI [Nghề]. Bẫy đã ghi nhận ở Vertex: đệm không nhận ra dữ liệu ngoài thay đổi. | [Tra] kubeflow.org/docs/components/pipelines/user-guides/core-functions/caching · docs.cloud.google.com/vertex-ai/docs/pipelines/configure-caching · xebia.com/blog/vertex-ai-pipelines-catching-cache-complexity |
| **SageMaker Canvas / Pipelines** | Canvas: hai nút **Quick build** (2–20 phút, dữ liệu lớn bị lấy mẫu) và **Standard build** (2–4 giờ); thời lượng nói trước khi bấm. Pipelines: có trình kéo-thả, đệm bước tắt mặc định và bắt buộc đặt hạn. | [Tra] docs.aws.amazon.com/sagemaker/latest/dg/canvas-build-model.html · docs.aws.amazon.com/sagemaker/latest/dg/pipelines-caching.html |
| **Roboflow** | Train: ba lựa chọn điểm xuất phát — checkpoint trước của tôi / checkpoint công khai (mặc định, khuyên dùng) / khởi tạo ngẫu nhiên (cảnh báo "đa số cho kết quả kém hơn"). Tiền xử lý + tăng cường chốt vào **dataset version** chứ không vào lần train [Nghề]. Workflows: cổng có kiểu ("kinds"), nối sai bị chặn lúc biên dịch. | [Tra] docs.roboflow.com/models/train/train-a-model · inference.roboflow.com/workflows/kinds |
| **HF AutoTrain** | Form theo task; có chế độ tham số cơ bản/đầy đủ và xem dạng JSON [Nghề, chưa xác nhận được qua tra cứu]; CLI nhận cấu hình YAML. | [Tra] huggingface.co/docs/autotrain/index |
| **NVIDIA TAO** | Một file spec YAML chia mục `dataset/model/train/evaluate/prune/export`; chuỗi hành động train → evaluate → prune → retrain → export. Đóng băng theo **khối**: `freeze_blocks` (tập ID hợp lệ **phụ thuộc backbone**), `freeze_bn`. | [Tra] docs.nvidia.com/tao/tao-toolkit/text/cv_finetuning/tensorflow_2/image_classification_tf2.html |
| **Teachable Machine / Lobe** | Ba thẻ cố định nối dây: Dữ liệu → Train → Xem thử. "Advanced" lộ epochs/batch/LR; "Under the hood" cho accuracy từng lớp, ma trận nhầm lẫn, loss theo epoch. Lobe đã ngừng phát triển [Nghề, mức chắc: trung bình — không tra được thông báo chính thức]. | Nguồn bên thứ ba: karsalfrink.github.io/ai-and-society/activities/week-3/workshop-model-training,-tuning-and-evaluation |
| **ClearML** | Clone một task → bản sao ở trạng thái **Draft sửa được** (tham số, và cả commit/diff mã [Nghề]) → Enqueue vào hàng đợi. Chọn hai task → COMPARE, chỗ khác nhau được tô. | [Tra] clear.ml/docs/latest/docs/webapp/webapp_exp_tuning |
| **W&B** | Lưu mã theo run, kèm `diff.patch` khi có thay đổi chưa commit; xem diff mã giữa hai run dạng một/hai cột. Bảng run, parallel coordinates, đồ thị lineage artifact [Nghề]. | [Tra] docs.wandb.ai/models/app/features/panels/code |

Tiền lệ ngoài danh sách nhưng trực tiếp cho "chạy thử": PyTorch Lightning `fast_dev_run` (vài batch qua train/val để bắt lỗi; **tắt checkpoint và logger**) và `overfit_batches` (không thuộc nổi vài batch thì đừng train lớn) — [Tra] lightning.ai/docs/pytorch/1.6.2/common/debugging.html.

## 2. Mẫu đã chứng minh theo từng nhu cầu

**(a) Hiểu luồng đang làm gì.** Edge Impulse + Teachable Machine: khung 3–5 khối cố định, mỗi khối một hình ảnh của *dữ liệu thật sau bước đó*. Hiệu quả vì người dùng học một bản đồ duy nhất rồi dùng mãi. n8n: ngăn chi tiết ba cột vào · tham số · ra — hiểu node qua dữ liệu chứ không qua mô tả.

**(b) Thử rẻ trước khi chạy đắt.** SageMaker Canvas: hai nút, hai mức giá, thời lượng nói trước. Lightning: smoke + overfit vài batch. n8n: ghim dữ liệu + chạy một bước. Edge Impulse: ước tính tài nguyên trước. Hiệu quả vì biến "thử" thành hành động hạng nhất có tên, không phải mẹo.

**(c) Thấy tiến trình và metric.** ComfyUI/KNIME: trạng thái nằm trên node (đang chạy/xong/lỗi/đệm). Teachable Machine: metric bằng ngôn ngữ người dùng trước (đúng từng lớp, ảnh nào sai), đường loss sau. W&B/ClearML: mọi run chung trục để chồng lên nhau.

**(d) Tách fine-tune / train lại.** Roboflow: ba lựa chọn điểm xuất phát, có mặc định và cảnh báo hậu quả. TAO: đóng băng theo khối do họ model khai báo, không theo từng lớp. Hiệu quả vì hỏi "bắt đầu từ đâu" thay vì bắt người dùng hiểu thuật ngữ.

**(e) Sửa tới code mà không lạc.** Edge Impulse Expert mode: cùng một khối, lật công tắc là thấy mã mà form vừa sinh ra — không nhảy sang môi trường khác. Dataiku: recipe code là công dân ngang hàng trong flow. Orange/n8n: node code có hợp đồng vào/ra đặt tên sẵn và console riêng.

**(f) Fork và phiên bản.** ClearML: run đã chạy là bất biến; muốn sửa thì Clone → Draft → Enqueue. W&B: mỗi run tự mang diff mã. n8n: mở lần chạy cũ rồi đưa về trình soạn. Hiệu quả vì đơn vị fork là *lần chạy có kết quả*, thứ người dùng nhớ được ("bản đạt 0,52 hôm thứ ba").

## 3. Thất bại đã biết của canvas node trong ML

1. **Mì rối.** ComfyUI phải thêm subgraph sau nhiều năm; KNIME phải có metanode/component. Canvas tự do luôn kéo theo công cụ quản lý sự lộn xộn do chính nó sinh ra.
2. **Độ mịn sai.** Azure Studio classic (node = thuật toán dựng sẵn) bị thay bằng component viết bằng code. Node quá mịn thì không ai nối tay; quá thô thì canvas chỉ là 4 ô — chấp nhận 4 ô còn hơn giả vờ.
3. **Tự do giả.** Kubeflow/Vertex thừa nhận điều này bằng thiết kế: đồ thị là hình chiếu của code. Luồng train một model là đường thẳng; cho kéo dây ở đó là mời người dùng tạo đồ thị không chạy được.
4. **Đệm nói dối.** Vertex không nhận ra dữ liệu ngoài đổi; Dataiku không nhận ra biến đổi; Azure cần cả trang gỡ lỗi reuse. Khoá đệm thiếu một thành phần là người dùng nhận kết quả cũ mà tưởng mới.
5. **Thử một đằng, chạy một nẻo.** n8n bỏ dữ liệu ghim khi chạy thật; `fast_dev_run` tắt checkpoint và logger — tức smoke kiểu này **không bắt được** lỗi ở khâu lưu/xuất. [Đoán có cơ sở] đây đúng là lớp lỗi `pycocotools` đã gặp [Repo: đề bài].
6. **Node code phá kiểm tĩnh.** Dataiku ghi rõ: không biết schema đầu ra của recipe code nếu chưa chạy.
7. **Khó so sánh phiên bản.** Không công cụ canvas nào ở trên có diff đồ thị tốt; ClearML/W&B so sánh bằng **bảng tham số + diff văn bản**, không bằng hình.
8. **Người mới bị ngợp.** KNIME/Orange mở ra là canvas trắng + kho hàng trăm node. Edge Impulse/Roboflow mở ra là luồng đã chạy được.

**Có khung hay tự do — ai chọn gì:** có khung: Edge Impulse, Teachable Machine, Roboflow Train, SageMaker Canvas, AutoTrain, TAO (đều là công cụ *train*). Tự do: n8n, ComfyUI, KNIME, Orange, Dataiku, Azure Designer (tự động hoá, sinh ảnh, ML bảng — node chạy vài giây, đầu ra tuần tự hoá được). Chỉ đọc từ code: Kubeflow, Vertex. Không có tiền lệ thành công nào của canvas tự do cho train deep learning nhiều giờ.

## 4. Đối chiếu với đề xuất

**4.1 Bảy node không khớp thực thi thật.** [Repo] `src/Hosts/MLPlatform.Shim/Runner.cs`: một lần chạy = tải dữ liệu → tải trọng số → chuyển định dạng → khôi phục checkpoint → dựng lệnh → **chạy một lệnh train duy nhất** → gom metric → đóng gói thư mục model → tải lên.
- **"Chia tập"** là thuộc tính của dataset version (`cookbook/40-module/M04-data.md`: `items.split`, `RandomizeSplits`), không phải bước của run. Đổi split = tạo version mới.
- **"Tăng cường"** chạy *bên trong* tiến trình train. [Repo] `recipes/d-fine/mlp-recipe.yaml` và `recipes/ultralytics-yolo/mlp-recipe.yaml` không khai báo tham số tăng cường nào. Không thể "chạy riêng node này" hay chèn code giữa nó và Model.
- **"Đánh giá"** theo D-39 do nền tảng chạy qua replica sau khi train — ranh giới thật. **"Xuất"** là entrypoint riêng — ranh giới thật.
- **Thiếu trên canvas** đúng những chỗ đã gây hỏng và tốn thời gian: **Môi trường/image** (build 10–30 phút, thiếu `pycocotools`), **chuyển định dạng dữ liệu**, **máy/tài nguyên**, **checkpoint/resume**.

Hậu quả: người dùng thấy "Tăng cường" xanh nhưng lỗi nằm ở build image — canvas mù đúng chỗ cần sáng. Đề nghị: node = **ranh giới artifact thật** (thứ lưu được, đệm được, chạy lại riêng được): *Môi trường → Dữ liệu (version + split + chuyển định dạng) → Model & điểm xuất phát → Huấn luyện → Đánh giá → Xuất*. Tăng cường, lịch học, đóng băng là **mặt cắt bên trong node Huấn luyện**, hiển thị bằng batch thật lấy từ dataloader trong lần chạy khói. Node code chỉ chèn ở ranh giới thật: sau chuyển định dạng, hook trong train do recipe khai báo, sau đánh giá.

**4.2 Trên node hay trong ngăn chi tiết.** Mẫu chung của n8n/ComfyUI/KNIME: **trên node** chỉ có trạng thái (chưa cấu hình/sẵn sàng/đang chạy/xong/lỗi/cũ/lấy từ đệm), một dòng tóm tắt, một hình nhỏ (ComfyUI: ảnh; ở đây: lưới 4 ảnh sau tăng cường, sparkline loss, thanh tiến độ + ETA), và huy hiệu "đã sửa so với gốc". **Trong ngăn phải**: ba cột vào · cấu hình · ra, log, file cấu hình, mã. Không công cụ nào đặt form trên node mà sống sót qua 10 tham số (ComfyUI làm vậy và đó là nguồn mì rối).

**4.3 "Đổi node này thì phải chạy lại gì" và chi phí.** Tiền lệ: n8n (tam giác vàng + lý do), KNIME (reset lan xuống), Kubeflow (biểu tượng đệm), Azure (ô ép chạy lại). **Không công cụ nào hiển thị chi phí bằng thời gian** — chỉ SageMaker Canvas nói trước thời lượng và Edge Impulse ước tính tài nguyên. Đề xuất hiện tại nói "tô node đã đổi giữa hai run" nhưng **chưa nói đến lan truyền hiệu lực**: sửa Dockerfile phải làm mất hiệu lực image + chạy thử; sửa epochs thì không. Đây là chỗ sản phẩm có thể vượt tiền lệ: sau chạy khói đã có tốc độ đo được, nên mỗi thay đổi hiện được "phải làm lại: build image ~12 phút, chạy thử ~3 phút, train ~4 giờ".

**4.4 Điểm khác.** "Nút Train thật chỉ mở khi đúng bản đó đã chạy thử" — đúng hướng, nhưng phải định nghĩa "đúng bản đó" bằng khoá gồm digest image + mã + cấu hình + dataset version + trọng số bắt đầu; bài học đệm ở mục 3.4. Chạy khói phải đi qua **cùng đường mã** với chạy thật (lưu checkpoint, đánh giá, xuất, nạp lại) — đề xuất đã nêu, cần giữ là điều kiện cứng, khác với `fast_dev_run`. Đóng băng theo khối: TAO cho thấy danh sách khối phải do recipe khai báo theo từng backbone; [Repo: đề bài] bộ dựng cấu hình họ DETR chưa hỗ trợ → sơ đồ khối chưa có gì đứng sau.

## 5. Đề nghị

### Nên áp dụng (11)

1. **Canvas có khung, sinh ra từ recipe** (Edge Impulse, Teachable Machine; Kubeflow về nguồn sự thật). File recipe là gốc; canvas là hình chiếu. Không lưu đồ thị riêng.
2. **Node = ranh giới artifact thật**, thêm node Môi trường; bỏ Chia tập và Tăng cường khỏi hàng node (mục 4.1).
3. **Ngăn phải ba cột vào · cấu hình · ra với dữ liệu thật** (n8n). Đây là thứ trả lời câu "người dùng mù".
4. **Trạng thái "đã cũ" kèm lý do và lan xuống phía sau** (n8n dirty node + KNIME reset).
5. **Đệm theo khoá đầy đủ, hiện biểu tượng "lấy từ đệm", có nút ép chạy lại** (ComfyUI, Kubeflow, Azure). Image đã build và dữ liệu đã chuyển định dạng là hai thứ đáng đệm nhất.
6. **Hai nút hai mức giá, nói trước thời lượng** (SageMaker Canvas): "Chạy thử ~3 phút" và "Train thật ~4 giờ 10 phút, VRAM đỉnh ~7,2/12 GB".
7. **Chạy khói cùng đường mã + overfit vài ảnh** (Lightning, có sửa điểm yếu của nó).
8. **Công tắc lật cấu hình ↔ mã ngay trong node** (Edge Impulse Expert mode), kèm diff so với recipe gốc.
9. **Ba lựa chọn điểm xuất phát có mặc định và cảnh báo hậu quả** (Roboflow); đóng băng theo khối do recipe khai báo (TAO).
10. **Clone run → bản nháp → xếp hàng; run đã chạy là bất biến** (ClearML). Trả lời câu hỏi mở: project giữ *một bản nháp đang làm việc*, mỗi run chốt một bản bất biến, "nấu lại từ run #12" khôi phục bản nháp từ run đó. Fork theo project và theo run không loại trừ nhau.
11. **So sánh bằng bảng tham số khác nhau + diff văn bản; canvas chỉ tô node có khác biệt** (ClearML, W&B). Lần chạy cũ mở lại trên chính canvas ở chế độ chỉ đọc (n8n).

### Nên tránh (5)

1. **Kéo dây tự do trong luồng train** — tự do giả, sinh đồ thị không chạy được.
2. **Node cho thứ không chạy riêng được** (Tăng cường, Chia tập) — canvas nói sai sự thật.
3. **Form tham số nằm trên thân node** — phình node, mì rối.
4. **Chạy thử bằng đường mã rút gọn** (bỏ lưu/xuất/đánh giá) — bỏ sót đúng lớp lỗi đắt nhất.
5. **Node code tự do không hợp đồng vào/ra** — phá kiểm tĩnh, phá đệm; và gộp tầng nối nhiều run (M14) vào cùng canvas này.

### Thư viện canvas cho React

**@xyflow/react (React Flow)** là lựa chọn hợp lý: MIT, node là component React thường nên nhúng được ECharts (đã có trong `web/package.json` [Repo]) và ảnh thu nhỏ; có sẵn pan/zoom, minimap, handle có kiểm tra kết nối. Giới hạn [Tra: reactflow.dev/learn/advanced-use/performance · reactflow.dev/learn/layouting/layouting]:
- Node vẽ bằng DOM: phải memo hoá node/edge, không đọc cả mảng `nodes` trong component; CSS đổ bóng/animation làm chậm khi nhiều node. Với 6–15 node thì không đáng lo; biểu đồ cập nhật trực tiếp trong node cần giới hạn tần suất.
- Không có tự xếp bố cục: phải thêm dagre/elk; dagre lỗi với sub-flow có cạnh nối ra ngoài. Luồng có khung thì bố cục tính tay được, không cần thư viện.
- Undo/redo, copy/paste, gom nhóm thu gọn phải tự làm [Nghề].
- [Đoán] Với luồng thẳng 6 node, có thể tự dựng bằng CSS grid + SVG; chỉ nên lấy React Flow nếu tái dùng cho canvas Pipelines/Agents (M14) — nơi thật sự cần đồ thị tự do. Tương thích React 19.3 chưa kiểm, cần thử trước khi chốt.
- `web/package.json` chưa có trình soạn mã [Repo]; cần thêm một (CodeMirror 6 nhẹ hơn Monaco [Nghề]) cho lớp cấu hình và mã.

## 10 điều quan trọng nhất

1. **Bảy node không khớp thực thi thật**: Chia tập thuộc dataset version, Tăng cường nằm trong lệnh train; còn Môi trường/image — nơi đã gây hỏng — lại vắng mặt. Vẽ lại node theo ranh giới artifact.
2. **Giá trị của n8n là dữ liệu thật ở từng node**, không phải canvas tự do. Ngăn ba cột vào · cấu hình · ra là thứ phải có đầu tiên.
3. **Chọn canvas có khung**: mọi công cụ train thành công đều có khung; không có tiền lệ canvas tự do cho train deep learning.
4. **Chạy thử phải đi cùng đường mã với chạy thật**; smoke kiểu rút gọn bỏ sót lỗi lưu/xuất/thiếu thư viện.
5. **Trạng thái "đã cũ" + lý do + lan truyền + chi phí bằng phút** — đề xuất chưa có; là chỗ vượt được mọi tiền lệ.
6. **Khoá "bản đã chạy thử" phải gồm digest image, mã, cấu hình, dataset version, trọng số bắt đầu**; khoá thiếu là đệm nói dối.
7. **Clone run → nháp → xếp hàng, run bất biến** giải luôn câu hỏi fork theo project hay theo run: cả hai, với một bản nháp mỗi project.
8. **Công tắc cấu hình ↔ mã ngay trong node, kèm diff so với gốc**; node code chỉ ở ranh giới thật và có hợp đồng vào/ra.
9. **Đóng băng theo khối cần recipe khai báo danh sách khối**; hiện chưa có gì đứng sau sơ đồ khối của họ DETR.
10. **So sánh phiên bản bằng bảng và diff văn bản, không bằng hình đồ thị**; canvas chỉ đánh dấu node khác biệt.
