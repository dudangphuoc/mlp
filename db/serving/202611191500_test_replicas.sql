-- Test replicas (model playground): a deployment started from "Test model" stops itself after idle_stop_min without use.
ALTER TABLE serving.deployments ADD COLUMN purpose text NOT NULL DEFAULT 'service';   -- service | test
ALTER TABLE serving.deployments ADD COLUMN idle_stop_min int;                          -- null = never stops by itself
ALTER TABLE serving.deployments ADD COLUMN last_used_at timestamptz;
