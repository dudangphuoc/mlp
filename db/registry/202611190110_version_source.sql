-- M11: model version source (run | upload | hf_hub) + reference (repo@revision).
ALTER TABLE registry.versions ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'run';
ALTER TABLE registry.versions ADD COLUMN IF NOT EXISTS source_ref text;
