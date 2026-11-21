-- mlp:query ClaimIdleWarnings :many
-- ≤ 10 minutes left before timeout and not yet warned during this idle period → mark as warned and return it so a notification is sent.
UPDATE nb.servers SET idle_warned_at = @now
WHERE state = 'running' AND idle_warned_at IS NULL
  AND last_activity_at + make_interval(mins => idle_timeout_min) - interval '10 minutes' < @now
RETURNING id, workspace_id, user_id, name;
