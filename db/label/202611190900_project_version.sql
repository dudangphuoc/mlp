-- Labeling on a dataset version (copied or reference) instead of only the uploaded files, and the AI auto-accept threshold.
ALTER TABLE label.projects ADD COLUMN IF NOT EXISTS version_id uuid REFERENCES data.dataset_versions(id);
ALTER TABLE label.projects ADD COLUMN IF NOT EXISTS auto_accept_threshold double precision NOT NULL DEFAULT 0;   -- 0 = off
