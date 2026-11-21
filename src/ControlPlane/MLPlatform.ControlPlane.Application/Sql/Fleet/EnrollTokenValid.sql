-- mlp:query EnrollTokenValid :scalar
-- mlp:no-workspace the enroll token is the entry point before the machine belongs to any workspace (plug-and-play install flow)
SELECT count(*) FROM fleet.enrollments
WHERE token_hash = @token_hash AND used_at IS NULL AND expires_at > @now;
