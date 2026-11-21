"""Fake training: writes jsonl metrics + TensorBoard (hand-encoded TFRecord), a checkpoint per epoch, best.json; supports resume/failure."""
import argparse, json, os, struct, sys, time

def tf_event(step, tag, value):
    def varint(n):
        out = b""
        while True:
            b = n & 0x7F; n >>= 7
            out += bytes([b | (0x80 if n else 0)])
            if not n: return out
    def field(num, wt, payload):
        return varint((num << 3) | wt) + payload
    def ld(num, data):
        return field(num, 2, varint(len(data)) + data)
    value_msg = ld(1, tag.encode()) + field(2, 5, struct.pack("<f", value))
    summary = ld(1, value_msg)
    event = field(2, 0, varint(step)) + ld(5, summary)
    return struct.pack("<Q", len(event)) + b"\0\0\0\0" + event + b"\0\0\0\0"

p = argparse.ArgumentParser()
p.add_argument("--data"); p.add_argument("--out"); p.add_argument("--epochs", type=int, default=3)
p.add_argument("--init"); p.add_argument("--resume"); p.add_argument("--fail", default="")
p.add_argument("--sleep", type=float, default=0)
p.add_argument("--export"); p.add_argument("--format")
a = p.parse_args()
if a.export:
    with open(os.path.join(a.out, f"model.{a.format}"), "w") as f: f.write(open(a.export).read())
    sys.exit(0)
if a.fail == "user":
    print('MLP_ERROR {"reason":"bad_data","message":"target column missing"}', file=sys.stderr); sys.exit(2)
if a.fail == "oom":
    print("RuntimeError: CUDA out of memory", file=sys.stderr); sys.exit(1)
rows = 0
train_csv = os.path.join(a.data or "", "train.csv")
if a.data and os.path.exists(train_csv):
    rows = sum(1 for _ in open(train_csv)) - 1   # skip the header — proves the dataset version data reached the container
start = 0
if a.resume:
    start = json.load(open(a.resume))["epoch"] + 1
    print(f"resuming from epoch {start}")
os.makedirs(os.path.join(a.out, "tb"), exist_ok=True)
with open(os.path.join(a.out, "log.jsonl"), "a") as log, open(os.path.join(a.out, "tb", "events.out.tfevents.1.toy"), "ab") as tb:
    for e in range(start, a.epochs):
        loss, f1 = 1.0 / (e + 1), 0.5 + 0.1 * e
        log.write(json.dumps({"epoch": e, "loss": loss, "f1": f1, "per_class": [f1, f1 - 0.1], "data/rows": rows}) + "\n"); log.flush()
        tb.write(tf_event(e, "lr", 0.01 / (e + 1))); tb.flush()
        json.dump({"epoch": e}, open(os.path.join(a.out, f"ckpt-{e}.json"), "w"))
        if a.sleep: time.sleep(a.sleep)
json.dump({"weights": [1, 2, 3], "init": a.init}, open(os.path.join(a.out, "best.json"), "w"))
print("done")
