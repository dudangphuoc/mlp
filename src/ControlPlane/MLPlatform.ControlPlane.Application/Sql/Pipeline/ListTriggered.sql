-- mlp:query ListTriggered :many
-- Enabled pipelines whose schedule trigger is due, or whose trigger matches event @event_kind (empty = schedule only).
SELECT id, workspace_id, name, definition::text AS definition, trigger::text AS trigger, enabled, last_triggered_at, created_by, created_at, updated_at FROM pipeline.pipelines
WHERE enabled AND (
  (@event_kind = '' AND trigger->>'type' = 'interval' AND coalesce(last_triggered_at, created_at) + make_interval(mins => (trigger->>'minutes')::int) <= @now)
  OR (@event_kind <> '' AND trigger->>'type' = 'event' AND trigger->>'event' = @event_kind AND workspace_id = @workspace_id));
