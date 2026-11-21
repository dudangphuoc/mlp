-- mlp:query ClaimNextJob :one
UPDATE sched.jobs SET state = 'scheduling', attempt_count = attempt_count + 1
WHERE id = (SELECT id FROM sched.jobs WHERE state = 'queued' AND (not_before IS NULL OR not_before <= now())
            ORDER BY priority DESC, queued_at FOR UPDATE SKIP LOCKED LIMIT 1)
RETURNING id, workspace_id, kind, name, spec::text AS spec, attempt_count, max_attempts;
