-- Runs opened by the SDK from the developer's own code: no recipe, no job. They report where they ran, which code, and a heartbeat.
ALTER TABLE track.runs ADD COLUMN origin text NOT NULL DEFAULT 'recipe';
ALTER TABLE track.runs ADD COLUMN host_name text;
ALTER TABLE track.runs ADD COLUMN code jsonb;
ALTER TABLE track.runs ADD COLUMN exit_code int;
ALTER TABLE track.runs ADD COLUMN heartbeat_at timestamptz;
CREATE INDEX runs_code_live ON track.runs (heartbeat_at) WHERE origin = 'code' AND state = 'running';
