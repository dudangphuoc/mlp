-- M13 §3 (first slice): prediction log + late-arriving ground truth, baseline from a dataset version, drift/quality snapshots.
CREATE SCHEMA IF NOT EXISTS monitor;

CREATE TABLE monitor.predictions (
    id            uuid PRIMARY KEY,
    deployment_id uuid        NOT NULL,
    model_version int         NOT NULL,
    at            timestamptz NOT NULL,
    latency_ms    double precision NOT NULL,
    features      jsonb       NOT NULL,          -- tabular: column values; text/image: summary features only (length, size)
    prediction    jsonb       NOT NULL,          -- {label, score} | {value}
    ground_truth  jsonb,                         -- {label} | {value}, joined later via /feedback
    gt_at         timestamptz
);
CREATE INDEX ix_predictions_dep ON monitor.predictions (deployment_id, at DESC);

CREATE TABLE monitor.configs (
    deployment_id      uuid PRIMARY KEY,
    workspace_id       uuid        NOT NULL,
    dataset_version_id uuid,
    label_column       text        NOT NULL DEFAULT 'label',
    baseline           jsonb,                    -- per-feature statistics (decile bins, samples, frequencies) + label distribution
    baseline_rows      bigint      NOT NULL DEFAULT 0,
    psi_warn           double precision NOT NULL DEFAULT 0.1,
    psi_alert          double precision NOT NULL DEFAULT 0.25,
    window_size        int         NOT NULL DEFAULT 500,
    min_samples        int         NOT NULL DEFAULT 20,
    enabled            boolean     NOT NULL DEFAULT true,
    updated_by         uuid        NOT NULL,
    updated_at         timestamptz NOT NULL
);

CREATE TABLE monitor.snapshots (
    id            uuid PRIMARY KEY,
    deployment_id uuid        NOT NULL,
    at            timestamptz NOT NULL,
    samples       int         NOT NULL,
    status        text        NOT NULL,          -- ok | warn | alert
    drift         jsonb       NOT NULL,          -- [{feature, kind, psi, ks, status}] + prediction drift
    quality       jsonb       NOT NULL           -- {labeled, accuracy | mae}
);
CREATE INDEX ix_snapshots_dep ON monitor.snapshots (deployment_id, at DESC);
