-- mlp:query RefreshOnline :scalar
-- Online = latest record per entity (fully replaces the view's previous rows).
WITH del AS (DELETE FROM feature.online WHERE view_id = @view_id RETURNING 1),
ins AS (
  INSERT INTO feature.online (view_id, entity_key, event_ts, values, updated_at)
  SELECT DISTINCT ON (entity_key) view_id, entity_key, event_ts, values, @now FROM feature.offline WHERE view_id = @view_id
  ORDER BY entity_key, event_ts DESC
  RETURNING 1)
SELECT count(*) FROM ins;
