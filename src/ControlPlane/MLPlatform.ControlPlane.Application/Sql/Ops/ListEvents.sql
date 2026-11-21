-- mlp:query ListEvents :many
-- Timeline of a job: events of the job + all of its attempts (+ the machine running the attempt during the same period).
SELECT e.id, e.at, e.source, e.resource_type, e.resource_id, e.kind, e.severity, e.message, e.data::text AS data
FROM ops.events e
WHERE (e.resource_type = 'job' AND e.resource_id = @job_id)
   OR (e.resource_type = 'attempt' AND e.resource_id IN (SELECT id FROM sched.job_attempts WHERE job_id = @job_id))
ORDER BY e.at, e.id LIMIT 1000;
