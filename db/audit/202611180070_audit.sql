-- M06 §3 audit (append-only, hash chain — 20/06 §6)
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE audit.events (
    seq          bigserial PRIMARY KEY,
    id           uuid        NOT NULL UNIQUE,
    at           timestamptz NOT NULL,
    workspace_id uuid,
    actor_id     uuid,
    action       text        NOT NULL,
    outcome      text        NOT NULL,     -- ok | denied | failed
    reason       text,
    prev_hash    text        NOT NULL,
    hash         text        NOT NULL
);
CREATE INDEX events_ws ON audit.events (workspace_id, at DESC);
-- No updates/deletes allowed (append-only)
CREATE FUNCTION audit.forbid_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit.events là append-only'; END $$;
CREATE TRIGGER events_immutable BEFORE UPDATE OR DELETE ON audit.events FOR EACH ROW EXECUTE FUNCTION audit.forbid_change();
