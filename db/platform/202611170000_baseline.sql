-- Migration ledger + shared outbox (20/02 §5.4, §5.5)
CREATE SCHEMA IF NOT EXISTS platform;

CREATE TABLE IF NOT EXISTS platform.outbox (
    id            uuid PRIMARY KEY,
    type          text        NOT NULL,
    payload       bytea       NOT NULL,
    occurred_at   timestamptz NOT NULL,
    dispatched_at timestamptz,
    attempts      int         NOT NULL DEFAULT 0,
    state         text        NOT NULL DEFAULT 'pending'
);
CREATE INDEX IF NOT EXISTS outbox_pending ON platform.outbox (occurred_at) WHERE state = 'pending';
