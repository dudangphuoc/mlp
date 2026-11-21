-- M15 §3 (first slice): versioned prompt registry, per-deployment guardrails + pricing, token counting, eval suites.
CREATE SCHEMA IF NOT EXISTS llmops;

CREATE TABLE llmops.prompts (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    name         text        NOT NULL,
    description  text        NOT NULL,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (workspace_id, name)
);

CREATE TABLE llmops.prompt_versions (
    prompt_id     uuid        NOT NULL REFERENCES llmops.prompts(id),
    version       int         NOT NULL,
    system        text        NOT NULL,          -- system prompt (may be empty)
    template      text        NOT NULL,          -- user content, variables written as {{name}}
    variables     jsonb       NOT NULL,          -- variable names extracted from template + system
    deployment_id uuid,                          -- default model (M12 LLM deployment)
    temperature   double precision NOT NULL,
    max_tokens    int         NOT NULL,
    note          text        NOT NULL,
    created_by    uuid        NOT NULL,
    created_at    timestamptz NOT NULL,
    PRIMARY KEY (prompt_id, version)
);

CREATE TABLE llmops.settings (
    deployment_id     uuid PRIMARY KEY,
    workspace_id      uuid        NOT NULL,
    pii_mode          text        NOT NULL DEFAULT 'off',   -- off | redact | block
    block_injection   boolean     NOT NULL DEFAULT false,
    blocked_terms     jsonb       NOT NULL DEFAULT '[]',
    check_output      boolean     NOT NULL DEFAULT false,
    price_per_1k      double precision NOT NULL DEFAULT 0,  -- estimated cost per 1,000 tokens (observational only, D-02)
    updated_by        uuid        NOT NULL,
    updated_at        timestamptz NOT NULL
);

CREATE TABLE llmops.usage (
    id                uuid PRIMARY KEY,
    workspace_id      uuid        NOT NULL,
    deployment_id     uuid        NOT NULL,
    prompt_id         uuid,
    prompt_version    int,
    source            text        NOT NULL,   -- gateway | prompt | eval
    at                timestamptz NOT NULL,
    prompt_tokens     int         NOT NULL,
    completion_tokens int         NOT NULL,
    latency_ms        double precision NOT NULL,
    cost              double precision NOT NULL,
    blocked           boolean     NOT NULL,
    flags             jsonb       NOT NULL    -- ["pii:email", "injection", "term", …]
);
CREATE INDEX ix_usage_ws ON llmops.usage (workspace_id, at DESC);

CREATE TABLE llmops.eval_sets (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    name         text        NOT NULL,
    cases        jsonb       NOT NULL,        -- [{vars:{…}, expect:{contains?, not_contains?, equals?, regex?, max_chars?}}]
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (workspace_id, name)
);

CREATE TABLE llmops.eval_runs (
    id             uuid PRIMARY KEY,
    workspace_id   uuid        NOT NULL,
    set_id         uuid        NOT NULL REFERENCES llmops.eval_sets(id),
    prompt_id      uuid        NOT NULL,
    prompt_version int         NOT NULL,
    score          double precision NOT NULL,   -- pass rate of cases
    results        jsonb       NOT NULL,        -- [{index, output, passed, checks:{…}, tokens, latency_ms}]
    created_by     uuid        NOT NULL,
    created_at     timestamptz NOT NULL
);
