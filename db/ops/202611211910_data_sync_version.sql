-- Which dataset version a sync brought onto the machine (subset = a trial's sample, not the whole version) — used to estimate,
-- before a run starts, how much of a version a machine already holds.
ALTER TABLE ops.data_syncs ADD COLUMN version_id uuid;
ALTER TABLE ops.data_syncs ADD COLUMN subset boolean NOT NULL DEFAULT false;
