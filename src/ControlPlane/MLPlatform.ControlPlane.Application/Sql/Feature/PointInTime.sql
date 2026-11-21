-- mlp:query PointInTime :many
-- Point-in-time join: for each (entity, time t) take the latest record with event_ts ≤ t that is still within TTL — no future data leakage.
SELECT s.idx, s.k AS entity_key, s.ts AS spine_ts, o.event_ts, o.values::text AS values
FROM unnest(@keys::text[], @times::timestamptz[]) WITH ORDINALITY AS s(k, ts, idx)
LEFT JOIN LATERAL (
  SELECT event_ts, values FROM feature.offline
  WHERE view_id = @view_id AND entity_key = s.k AND event_ts <= s.ts AND event_ts > s.ts - make_interval(hours => @ttl_hours)
  ORDER BY event_ts DESC LIMIT 1) o ON true
ORDER BY s.idx;
