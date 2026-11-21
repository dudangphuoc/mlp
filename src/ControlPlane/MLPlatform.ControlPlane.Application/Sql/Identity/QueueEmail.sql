-- mlp:query QueueEmail :exec
INSERT INTO identity.email_outbox (id, to_email, subject, body, kind, created_at, not_before)
VALUES (@id, @to_email, @subject, @body, @kind, @now, @now);
