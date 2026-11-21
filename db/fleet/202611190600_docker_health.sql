-- Docker on the machine (agent reports every 15 s): daemon not running → machine is "degraded" and takes no new work.
ALTER TABLE fleet.hosts ADD COLUMN IF NOT EXISTS docker_ok boolean NOT NULL DEFAULT true;
ALTER TABLE fleet.hosts ADD COLUMN IF NOT EXISTS docker_error text;
