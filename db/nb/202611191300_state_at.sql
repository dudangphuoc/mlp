-- When the notebook entered its current state: lets the reaper finish "stopping" notebooks the machine never confirmed.
ALTER TABLE nb.servers ADD COLUMN IF NOT EXISTS state_at timestamptz;
UPDATE nb.servers SET state_at = coalesce(stopped_at, started_at, created_at) WHERE state_at IS NULL;
