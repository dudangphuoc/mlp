-- Round 1 (D-49): the train/val/test split belongs to the dataset version, not to a training run.
-- split_state: NULL = not looked at yet | none | folders (train/ val/ directories) | file (mlp-split.json).
ALTER TABLE data.dataset_versions ADD COLUMN split_state text;
ALTER TABLE data.dataset_versions ADD COLUMN split_counts jsonb;
ALTER TABLE data.dataset_versions ADD COLUMN val_sha256 text;
