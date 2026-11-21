# whisper — verification notes (2026-11-17)

| Fact | Source |
|---|---|
| openai/whisper MIT; commit `86098128c0b4f24f0e2aa2994de830614b474227` (2026-08-31) | https://api.github.com/repos/openai/whisper |
| Model names: tiny(.en), base(.en), small(.en), medium(.en), large-v1/v2/v3, large-v3-turbo, turbo | https://raw.githubusercontent.com/openai/whisper/main/whisper/__init__.py |
| Inference VRAM: tiny/base ~1 GB, small ~2 GB, medium ~5 GB, large ~10 GB, turbo ~6 GB; turbo is not trained for translation | README openai/whisper |
| `run_speech_recognition_seq2seq.py` was removed from `main` (commit `14d76370…`, PR #49357, 2026-10-13); still present at tag `v5.19.0` | https://raw.githubusercontent.com/huggingface/transformers/v5.19.0/examples/pytorch/speech-recognition/ |
| Script parameters: `--dataset_name` (required, no `--train_file`), `--audio_column_name`, `--text_column_name`, `--language`, `--task`, `--max_duration_in_seconds`, `--freeze_feature_encoder`… | script v5.19.0 |
| WER is only computed with `--predict_with_generate`; logged as `eval_wer`; `report_to` defaults to `none` → must be set to `tensorboard` | script + `training_args.py` |
| Local data: AudioFolder — each split directory holds a `metadata.jsonl` with `file_name` + a text column; the audio column is named `audio` | datasets `audio_dataset.mdx` |

The `hf-audiofolder` converter: from the platform's `asr` dataset (audio items + `transcript` annotations) it writes `{split}/metadata.jsonl` with `{"file_name": "...", "text": "..."}` and copies/symlinks the audio.
