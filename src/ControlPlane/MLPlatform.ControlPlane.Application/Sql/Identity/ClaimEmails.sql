-- mlp:query ClaimEmails :many
SELECT id, to_email, subject, body, kind, attempts FROM identity.email_outbox
WHERE state = 'pending' AND not_before <= @now
ORDER BY not_before LIMIT 20 FOR UPDATE SKIP LOCKED;
