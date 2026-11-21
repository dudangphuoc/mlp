-- mlp:query UseEnrollment :exec
UPDATE fleet.enrollments SET used_at = @now, host_id = @host_id WHERE id = @id;
