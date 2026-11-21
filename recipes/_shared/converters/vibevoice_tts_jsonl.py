"""The `vibevoice-tts-jsonl` converter: tts-manifest (standard format) → JSONL for
vibevoice.finetune.train_vibevoice (fork vibevoice-community).

In (cookbook 20/08 §7, `tts-manifest`): {src}/{split}.jsonl, one per line
  {"audio": "...", "text": "...", "speaker": "...", "voice_prompt"?: "..."}
Out: {dst}/{split}.jsonl, one per line
  {"text": "Speaker 0: <text>", "audio": "<absolute path>", "voice_prompts": "<absolute>"?}
Fine-tuning supports a single speaker only → every line gets "Speaker 0:" (see the recipe's openItems).
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def fail(reason: str, **kw) -> None:
    print("MLP_ERROR " + json.dumps({"reason": reason, **kw}, ensure_ascii=False), file=sys.stderr)
    sys.exit(2)


def absolute(src: Path, p: str) -> str:
    q = Path(p)
    return str(q if q.is_absolute() else (src / q).resolve())


def main() -> None:
    a = argparse.ArgumentParser()
    a.add_argument("--src", type=Path, required=True)
    a.add_argument("--dst", type=Path, required=True)
    a.add_argument("--speaker-label", default="Speaker 0")
    args = a.parse_args()
    args.dst.mkdir(parents=True, exist_ok=True)
    counts = {}
    for split in ("train", "val", "test"):
        manifest = args.src / f"{split}.jsonl"
        if not manifest.exists():
            continue
        rows = []
        for line_no, line in enumerate(manifest.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip():
                continue
            r = json.loads(line)
            audio = absolute(args.src, r["audio"])
            if not Path(audio).exists():
                fail("audio_missing", split=split, line=line_no, path=audio)
            out = {"text": f"{args.speaker_label}: {r['text'].strip()}", "audio": audio}
            if r.get("voice_prompt"):
                out["voice_prompts"] = absolute(args.src, r["voice_prompt"])
            rows.append(json.dumps(out, ensure_ascii=False))
        (args.dst / f"{split}.jsonl").write_text("\n".join(rows) + "\n", encoding="utf-8")
        counts[split] = len(rows)
    if not counts.get("train"):
        fail("split_missing", split="train")
    print(json.dumps(counts))


if __name__ == "__main__":
    main()
