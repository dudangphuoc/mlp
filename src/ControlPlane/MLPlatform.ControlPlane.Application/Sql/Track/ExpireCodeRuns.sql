-- mlp:query ExpireCodeRuns :exec
-- mlp:no-workspace background expiry across all workspaces
-- A code run on the dev machine whose process stopped sending heartbeats is marked failed; runs on cluster machines follow their job.
UPDATE track.runs SET state = 'failed', ended_at = @now
WHERE origin = 'code' AND job_id IS NULL AND state = 'running' AND heartbeat_at < @cutoff;
