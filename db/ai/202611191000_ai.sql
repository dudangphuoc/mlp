-- AI providers (Hermes phase 1): connections to model servers, declared models with capabilities, one default model per role,
-- and a record of EVERY AI run (tests, decisions, chats) so the UI can show what the AI did, with which model, input, output and why.
CREATE SCHEMA IF NOT EXISTS ai;

CREATE TABLE ai.connections (
    id              uuid PRIMARY KEY,
    name            text        NOT NULL,
    provider_type   text        NOT NULL,             -- openai-compatible | openai | anthropic | deepseek | systemone | typesafe
    endpoint        text        NOT NULL,
    secret_enc      text,                             -- API key, AES-GCM (SecretBox); never returned to clients
    max_concurrency int         NOT NULL DEFAULT 4,   -- enforced per connection
    rpm_limit       int         NOT NULL DEFAULT 0,   -- requests per minute, 0 = unlimited; enforced
    timeout_ms      int         NOT NULL DEFAULT 60000,
    is_active       boolean     NOT NULL DEFAULT true,
    created_by      uuid        NOT NULL,
    created_at      timestamptz NOT NULL,
    updated_at      timestamptz NOT NULL,
    UNIQUE (name)
);

CREATE TABLE ai.models (
    id                 uuid PRIMARY KEY,
    connection_id      uuid        NOT NULL REFERENCES ai.connections(id) ON DELETE CASCADE,
    model_id           text        NOT NULL,          -- name sent to the provider
    display_name       text        NOT NULL DEFAULT '',
    roles              text[]      NOT NULL,          -- analyst | decision | judge | vision | labeler | embed | draft
    inputs             text[]      NOT NULL,          -- text | image | audio | video | pdf
    outputs            text[]      NOT NULL,          -- text | json | embedding | boxes | masks
    skills             text[]      NOT NULL,          -- streaming | tools | structured | reasoning
    tier               text        NOT NULL DEFAULT 'standard',   -- small | standard | frontier
    input_usd_per_1m   double precision,
    output_usd_per_1m  double precision,
    max_tokens         int         NOT NULL DEFAULT 4096,
    is_active          boolean     NOT NULL DEFAULT true,
    health             text        NOT NULL DEFAULT 'unknown',    -- unknown | ok | degraded | failed (updated by real runs, not only tests)
    health_reason      text,
    health_at          timestamptz,
    consecutive_failures int       NOT NULL DEFAULT 0,
    last_test_run_id   uuid,
    created_at         timestamptz NOT NULL,
    updated_at         timestamptz NOT NULL,
    UNIQUE (connection_id, model_id)
);

-- One explicit default model per role (no hidden defaults elsewhere).
CREATE TABLE ai.role_defaults (
    role       text PRIMARY KEY,
    model_id   uuid        NOT NULL REFERENCES ai.models(id) ON DELETE CASCADE,
    set_by     uuid        NOT NULL,
    set_at     timestamptz NOT NULL
);

CREATE TABLE ai.runs (
    id            uuid PRIMARY KEY,
    workspace_id  uuid,                               -- null = instance-level (e.g. model tests)
    kind          text        NOT NULL,               -- test | decision | chat
    purpose       text        NOT NULL,               -- human-readable: "Model test", "Label review"…
    subject       text,                               -- what it was about, e.g. labeling item path
    trigger       text        NOT NULL,               -- who/what started it: "user:<name>", "event:item-submitted", "auto-recovery"
    actor_id      uuid,
    state         text        NOT NULL,               -- running | succeeded | failed
    fallback      text,                               -- set when a fallback was used, with the reason
    model_ref     uuid,
    model_label   text,                               -- "connection / model"
    selection     jsonb       NOT NULL DEFAULT '{}',  -- every candidate model with why it was picked or excluded
    input         jsonb       NOT NULL DEFAULT '{}',  -- exactly what was sent (prompt / decision state + questions)
    output        jsonb       NOT NULL DEFAULT '{}',  -- exactly what came back (text / choice + confidence + probabilities)
    decided_by    text,
    confidence    double precision,
    latency_ms    int,
    tokens_in     int,
    tokens_out    int,
    cost_usd      double precision,
    attempts      jsonb       NOT NULL DEFAULT '[]',
    error_code    text,
    error         text,
    started_at    timestamptz NOT NULL,
    finished_at   timestamptz
);
CREATE INDEX ix_ai_runs_ws ON ai.runs (workspace_id, started_at DESC);
CREATE INDEX ix_ai_runs_state ON ai.runs (state) WHERE state = 'running';
CREATE INDEX ix_ai_runs_model ON ai.runs (model_ref, started_at DESC);

CREATE TABLE ai.audit (
    id        uuid PRIMARY KEY,
    actor_id  uuid        NOT NULL,
    action    text        NOT NULL,
    target    text        NOT NULL,
    before    jsonb,
    after     jsonb,
    at        timestamptz NOT NULL
);
CREATE INDEX ix_ai_audit_at ON ai.audit (at DESC);
