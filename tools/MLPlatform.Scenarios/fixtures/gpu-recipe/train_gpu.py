"""Trains an MLP on CUDA with synthetic data: prints the GPU in use, writes jsonl metrics, a checkpoint per epoch, best.pt; supports resume."""
import argparse, json, os, time
import torch, torch.nn as nn

p = argparse.ArgumentParser()
p.add_argument("--out"); p.add_argument("--epochs", type=int, default=20); p.add_argument("--hidden", type=int, default=2048)
p.add_argument("--batch", type=int, default=4096); p.add_argument("--resume")
a = p.parse_args()
if not torch.cuda.is_available():
    print('MLP_ERROR {"reason":"no_gpu","message":"container cannot see a CUDA GPU"}', flush=True); raise SystemExit(2)
dev = torch.device("cuda")
print(f"GPU: {torch.cuda.get_device_name(0)} · CUDA {torch.version.cuda} · torch {torch.__version__}", flush=True)
g = torch.Generator().manual_seed(0)
X = torch.randn(200_000, 128, generator=g); w = torch.randn(128, 10, generator=g); y = (X @ w).argmax(1)
X, y = X.to(dev), y.to(dev); Xv, yv = X[-20_000:], y[-20_000:]; X, y = X[:-20_000], y[:-20_000]
model = nn.Sequential(nn.Linear(128, a.hidden), nn.ReLU(), nn.Linear(a.hidden, a.hidden), nn.ReLU(), nn.Linear(a.hidden, 10)).to(dev)
opt = torch.optim.AdamW(model.parameters(), lr=1e-3)
start = 0
if a.resume:
    ck = torch.load(a.resume, map_location=dev); model.load_state_dict(ck["model"]); opt.load_state_dict(ck["opt"]); start = ck["epoch"] + 1
    print(f"resuming from epoch {start}", flush=True)
best = 0.0
with open(os.path.join(a.out, "log.jsonl"), "a") as log:
    for e in range(start, a.epochs):
        t0 = time.time(); model.train(); perm = torch.randperm(len(X), device=dev); tot = 0.0
        for i in range(0, len(X), a.batch):
            idx = perm[i:i + a.batch]; loss = nn.functional.cross_entropy(model(X[idx]), y[idx])
            opt.zero_grad(); loss.backward(); opt.step(); tot += loss.item() * len(idx)
        torch.cuda.synchronize(); dt = time.time() - t0
        model.eval()
        with torch.no_grad(): acc = (model(Xv).argmax(1) == yv).float().mean().item()
        rec = {"epoch": e, "loss": tot / len(X), "acc": acc, "gpu_mem_gb": torch.cuda.max_memory_allocated() / 1e9, "samples_per_s": len(X) / dt}
        log.write(json.dumps(rec) + "\n"); log.flush(); print(json.dumps(rec), flush=True)
        torch.save({"model": model.state_dict(), "opt": opt.state_dict(), "epoch": e}, os.path.join(a.out, f"ckpt-{e}.pt"))
        if acc > best: best = acc; torch.save(model.state_dict(), os.path.join(a.out, "best.pt"))
print("done", flush=True)
