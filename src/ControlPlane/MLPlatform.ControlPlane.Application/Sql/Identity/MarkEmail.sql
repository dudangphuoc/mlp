-- mlp:query MarkEmail :exec
UPDATE identity.email_outbox
SET state = @state, attempts = attempts + 1, sent_at = CASE WHEN @state = 'sent' THEN @now END,
    not_before = @not_before, last_error = @error
WHERE id = @id;
