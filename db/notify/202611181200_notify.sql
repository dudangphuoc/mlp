-- M16 §3 (P1: in_app channel). Kept for 90 days (a background cleanup job deletes older rows).
CREATE SCHEMA IF NOT EXISTS notify;

CREATE TABLE notify.notifications (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    user_id      uuid        NOT NULL,
    kind         text        NOT NULL,
    title        text        NOT NULL,
    body         text        NOT NULL DEFAULT '',
    link         text        NOT NULL,          -- R2: link only, never sensitive data
    resource_uri text        NOT NULL,
    severity     text        NOT NULL,          -- info | success | warning | error
    count        int         NOT NULL DEFAULT 1, -- R1: merges same type + same resource within 5 minutes
    created_at   timestamptz NOT NULL,
    updated_at   timestamptz NOT NULL,
    read_at      timestamptz
);
CREATE INDEX ix_notify_user ON notify.notifications (user_id, workspace_id, updated_at DESC);

CREATE TABLE notify.preferences (
    user_id      uuid    NOT NULL,
    workspace_id uuid    NOT NULL,
    kind         text    NOT NULL,
    channel      text    NOT NULL,
    enabled      boolean NOT NULL,
    PRIMARY KEY (user_id, workspace_id, kind, channel)
);

-- M08 R4: warning before auto-shutdown — sent only once per idle period.
ALTER TABLE nb.servers ADD COLUMN idle_warned_at timestamptz;
