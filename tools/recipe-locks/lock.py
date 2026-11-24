"""Regenerate recipes/<name>/constraints.txt: resolve each recipe's pip installs (environment.pip in mlp-recipe.yaml) for Linux x86_64 with uv, keeping the
torch/CUDA packages of the base image (bases/*.txt, from `pip list --format=freeze` inside the image).

    uv tool run python tools/recipe-locks/lock.py [recipe ...]
"""
import os, re, shutil, subprocess, sys
from pathlib import Path
SP = Path(__file__).parent
ROOT = SP.parent.parent
UV = shutil.which("uv") or "uv"
B = {"251": ("base-pytorch_pytorch_2.5.1-cuda12.4-cudnn9-runtime.txt", "3.11"), "280": ("base-pytorch_pytorch_2.8.0-cuda12.8-cudnn9-runtime.txt", "3.11"),
     "260d": ("base-pytorch_pytorch_2.6.0-cuda12.4-cudnn9-devel.txt", "3.11"), "ngc2512": ("base-nvcr.io_nvidia_pytorch_25.12-py3.txt", "3.12"),
     "ngc2412": ("base-nvcr.io_nvidia_pytorch_24.12-py3.txt", "3.12"), "py311": ("base-python_3.11-slim.txt", "3.11"), "py312": (None, "3.12"), "ubuntu2404": (None, "3.12")}
def up(n): return [l.strip() for l in (SP / "upstream" / f"{n}.txt").read_text().splitlines() if l.strip() and not l.startswith("#")]
R = {
 "d-fine": ("251", up("dfine") + ["matplotlib", "opencv-python-headless", "onnx", "onnxsim", "transformers==4.48.3", "pycocotools"], []),
 "deimv2": ("251", up("deimv2") + ["opencv-python-headless", "onnx", "onnxsim", "gdown"], []),
 "rt-detrv4": ("251", up("rtv4") + up("dinov3") + ["opencv-python-headless", "onnx", "onnxsim", "gdown"], []),
 "sam2": ("251", ["sam-2 @ git+https://github.com/facebookresearch/sam2@2b90b9f5ceec907a1c18123530e92e794ad901a4", "opencv-python-headless"], ["sam-2"]),
 "hf-auto": ("251", ["transformers==4.48.3", "accelerate<1.4", "timm<1.1", "torchaudio==2.5.1", "pillow", "scipy"], []),
 "hf-llm-sft": ("280", ["trl[peft]==1.18.1", "transformers==5.23.0", "peft==0.21.3", "accelerate==1.16.0", "datasets==5.2.0", "bitsandbytes", "tensorboard"], []),
 "hf-text": ("280", ["transformers==5.19.0", "datasets", "evaluate==0.4.6", "seqeval", "accelerate", "tensorboard", "scikit-learn"], []),
 "whisper": ("280", ["transformers==5.19.0", "evaluate==0.4.6", "jiwer==4.0.0", "librosa", "accelerate", "tensorboard", "datasets[audio]", "soundfile", "torchcodec==0.7.0"], []),
 "rf-detr": ("280", ["rfdetr[train,onnx]==1.12.1", "tensorboard"], []),
 "ultralytics-yolo": ("280", ["ultralytics==8.4.215", "onnx", "onnxslim"], []),
 "vibevoice-tts": ("260d", ["vibevoice @ git+https://github.com/vibevoice-community/VibeVoice@952326ddb264062466a888cf32a5b2f4e803e16e", "tensorboard"], ["vibevoice"]),
 "vibevoice-asr": ("ngc2512", ["vibevoice @ git+https://github.com/microsoft/VibeVoice@16fb2cb1217c9934a886e1948ffb06120caa2df5", "peft", "tensorboard", "jiwer"], ["vibevoice"]),
 "vibevoice-realtime": ("ngc2412", ["vibevoice[streamingtts] @ git+https://github.com/microsoft/VibeVoice@16fb2cb1217c9934a886e1948ffb06120caa2df5"], ["vibevoice"]),
 "tabular-gbdt": ("py312", ["scikit-learn==1.9.1", "xgboost==3.4.1", "lightgbm==4.7.0", "pandas", "pyarrow", "joblib"], []),
 "generic-onnx": ("ubuntu2404", ["onnxruntime-gpu==1.22.0", "onnx==1.18.0", "numpy", "pillow", "psutil==7.0.0", "openvino==2025.0.0"], []),
}
BASE_IMAGE = {"251": "pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime", "280": "pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime",
    "260d": "pytorch/pytorch:2.6.0-cuda12.4-cudnn9-devel", "ngc2512": "nvcr.io/nvidia/pytorch:25.12-py3", "ngc2412": "nvcr.io/nvidia/pytorch:24.12-py3",
    "py311": "python:3.11-slim", "py312": "python:3.12-slim", "ubuntu2404": "nvidia/cuda:12.6.3-cudnn-runtime-ubuntu24.04 (Ubuntu python3)"}
norm = lambda n: re.sub(r"[-_.]+", "-", n).lower()
def base_pins(f):
    pins = {}
    if not f: return pins
    for l in (SP / "bases" / f).read_text().splitlines():
        m = re.match(r"^([A-Za-z0-9_.\-]+)==(\S+)$", l.strip())
        if m: pins[norm(m.group(1))] = (m.group(1), m.group(2))
    return pins
def pypi_version(name, v):
    v = v.split("+")[0]
    if name in ("torch", "torchvision", "torchaudio", "triton"): v = re.sub(r"(a|b|rc|\.dev)\d*$", "", v)
    return v
def constraint(name, version):
    if re.search(r"(a|b|rc)\d+(\+|$)", version.split("+")[0] + "+") and norm(name) in ("torch", "torchvision", "torchaudio", "triton"):
        return f"{name}<{pypi_version(norm(name), version)}"
    return f"{name}=={pypi_version(norm(name), version)}"


only = sys.argv[1:] or list(R)
for r in only:
    base, reqs, local = R[r]
    bf, py = B[base]
    pins = base_pins(bf)
    work = SP / ".work" / r; work.mkdir(parents=True, exist_ok=True)
    (work / "in.txt").write_text("\n".join(reqs) + "\n")
    locked = lambda k: k in ("torch", "torchvision", "torchaudio", "triton", "flash-attn") or k.startswith("nvidia-")
    usable = lambda n, v: not re.search(r"[^0-9a-z.]", pypi_version(norm(n), v))
    env = dict(os.environ, SAM2_BUILD_CUDA="0", UV_CACHE_DIR=str(SP / ".cache"))
    for strict in (True, False):
        (work / "cons.txt").write_text("\n".join(constraint(n, v) for k, (n, v) in pins.items()
            if (strict or locked(k)) and usable(n, v)) + "\n")
        cmd = [UV, "pip", "compile", str(work / "in.txt"), "-c", str(work / "cons.txt"), "--python-version", py, "--python-platform", "x86_64-manylinux_2_28",
               "--no-header", "--no-annotate", "--quiet", "-o", str(work / "out.txt")]
        p = subprocess.run(["nice", "-n", "19", *cmd], capture_output=True, text=True, env=env, timeout=1800)
        if p.returncode == 0: break
    if p.returncode:
        print(f"FAIL {r}\n{p.stderr[-1500:]}"); continue
    resolved = {}
    for l in (work / "out.txt").read_text().splitlines():
        m = re.match(r"^([A-Za-z0-9_.\-\[\]]+)==(\S+)", l.strip())
        if m: resolved[norm(re.sub(r"\[.*\]", "", m.group(1)))] = (re.sub(r"\[.*\]", "", m.group(1)), m.group(2))
    final, kept, added = [], 0, 0
    for k, (n, v) in sorted(resolved.items()):
        if k in local: continue
        if k in pins and pypi_version(k, pins[k][1]) == v.split("+")[0]: final.append(f"{pins[k][0]}=={pins[k][1]}"); kept += 1
        elif k in pins and locked(k): final.append(f"{pins[k][0]}=={pins[k][1]}"); kept += 1
        else: final.append(f"{n}=={v}"); added += 1
    if r == "generic-onnx": final += ["tensorrt-cu12==10.7.0", "tensorrt-cu12-libs==10.7.0", "tensorrt-cu12-bindings==10.7.0", "nvidia-cuda-runtime-cu12==12.9.79"]
    if r == "vibevoice-tts": final += ["flash-attn==2.7.4.post1", "einops==0.8.2"]
    head = f"# Exact versions for every pip install of {r} (PIP_CONSTRAINT); base image {BASE_IMAGE[base]}.\n# Regenerate with tools/recipe-locks/lock.py after changing environment.pip in mlp-recipe.yaml.\n"
    (ROOT / "recipes" / r / "constraints.txt").write_text(head + "\n".join(sorted(set(final), key=str.lower)) + "\n")
    print(f"OK   {r}: {len(final)} pins ({added} added or changed by the recipe, {kept} as in the base image, {'base kept' if strict else 'base loosened'}), python {py}")
