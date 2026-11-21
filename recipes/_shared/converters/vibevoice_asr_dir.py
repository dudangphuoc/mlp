"""The `vibevoice-asr-dir` converter: asr-manifest (standard format) → a directory of N.<audio> + N.json pairs for
microsoft/VibeVoice finetuning-asr/lora_finetune.py.

In (cookbook 20/08 §7, `asr-manifest`): {src}/{split}.jsonl, one per line
  {"audio": "<relative or absolute path>", "text": "...", "speaker"?: "...",
   "segments"?: [{"start", "end", "text", "speaker"?}], "context"?: ["..."]}
Out: {dst}/{split}/N.<ext> + N.json
  {"audio_duration", "audio_path": "N.<ext>", "segments": [{"speaker": int, "text", "start", "end"}],
   "customized_context"?: [...]}
No segments → one segment covering the whole file. String speakers → numbered 0..k in order of appearance in the file.
Data errors → MLP_ERROR + code 2.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

import soundfile as sf


def fail(reason: str, **kw) -> None:
    print("MLP_ERROR " + json.dumps({"reason": reason, **kw}, ensure_ascii=False), file=sys.stderr)
    sys.exit(2)


def convert_split(src: Path, dst: Path, split: str) -> int:
    manifest = src / f"{split}.jsonl"
    if not manifest.exists():
        return 0
    out = dst / split
    out.mkdir(parents=True, exist_ok=True)
    n = 0
    for line_no, line in enumerate(manifest.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        row = json.loads(line)
        audio = Path(row["audio"])
        audio = audio if audio.is_absolute() else src / audio
        if not audio.exists():
            fail("audio_missing", split=split, line=line_no, path=str(audio))
        duration = sf.info(str(audio)).duration
        target = out / f"{n}{audio.suffix.lower()}"
        shutil.copyfile(audio, target)
        speakers: dict[str, int] = {}
        def spk(v) -> int:
            key = str(v if v is not None else row.get("speaker", "0"))
            return speakers.setdefault(key, len(speakers))
        segs = row.get("segments") or [{"start": 0.0, "end": duration, "text": row["text"]}]
        doc = {"audio_duration": round(duration, 3), "audio_path": target.name,
               "segments": [{"speaker": spk(s.get("speaker")), "text": s["text"],
                             "start": float(s["start"]), "end": float(s["end"])} for s in segs]}
        if row.get("context"):
            doc["customized_context"] = row["context"]
        (out / f"{n}.json").write_text(json.dumps(doc, ensure_ascii=False), encoding="utf-8")
        n += 1
    return n


def main() -> None:
    a = argparse.ArgumentParser()
    a.add_argument("--src", type=Path, required=True)
    a.add_argument("--dst", type=Path, required=True)
    args = a.parse_args()
    counts = {s: convert_split(args.src, args.dst, s) for s in ("train", "val", "test")}
    if counts["train"] == 0:
        fail("split_missing", split="train")
    print(json.dumps(counts))


if __name__ == "__main__":
    main()
