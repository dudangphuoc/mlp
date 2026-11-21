-- mlp:query EventsAfter :many
SELECT e.id, e.at, e.source, e.resource_type, e.resource_id, e.kind, e.severity, e.message, e.data::text AS data
FROM ops.events e
WHERE ((e.resource_type = 'job' AND e.resource_id = @job_id)
   OR (e.resource_type = 'attempt' AND e.resource_id IN (SELECT id FROM sched.job_attempts WHERE job_id = @job_id)))
  AND e.received_at > @after
ORDER BY e.at, e.id LIMIT 500;
