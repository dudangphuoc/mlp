-- mlp:query InsertOffline :exec
INSERT INTO feature.offline (view_id, entity_key, event_ts, values)
SELECT @view_id, k, t, v::jsonb FROM unnest(@keys::text[], @times::timestamptz[], @vals::text[]) AS x(k, t, v);
