-- mlp:query HostMap :many
SELECT h.id, h.name, h.state, h.last_seen_at, h.metrics_at, h.last_metrics::text AS last_metrics, h.host_info,
  (SELECT count(*) FROM sched.job_attempts a WHERE a.state = 'suspect' AND h.id = ANY(a.host_ids)) AS recovering
FROM fleet.hosts h WHERE h.workspace_id = @workspace_id ORDER BY h.name;
