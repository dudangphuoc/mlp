-- Transactional email (P0-18, D-27): enqueued in the same transaction as the business operation; the EmailSender loop sends via SMTP with exponential retry; after 8 attempts → dead.
CREATE TABLE IF NOT EXISTS identity.email_outbox (
    id          uuid PRIMARY KEY,
    to_email    text        NOT NULL,
    subject     text        NOT NULL,
    body        text        NOT NULL,          -- {public_url} substituted at send time
    kind        text        NOT NULL,
    created_at  timestamptz NOT NULL,
    not_before  timestamptz NOT NULL,
    sent_at     timestamptz,
    attempts    int         NOT NULL DEFAULT 0,
    state       text        NOT NULL DEFAULT 'pending',   -- pending | sent | dead
    last_error  text
);
CREATE INDEX IF NOT EXISTS email_outbox_pending ON identity.email_outbox (not_before) WHERE state = 'pending';
