-- Hermes phase 4: AI explanation stored on each check (who asked, which language, which AI run produced it).
ALTER TABLE label.hermes_checks ADD COLUMN IF NOT EXISTS explanation text;
ALTER TABLE label.hermes_checks ADD COLUMN IF NOT EXISTS explanation_lang text;
ALTER TABLE label.hermes_checks ADD COLUMN IF NOT EXISTS explanation_run_id uuid;
ALTER TABLE label.hermes_checks ADD COLUMN IF NOT EXISTS explained_at timestamptz;
