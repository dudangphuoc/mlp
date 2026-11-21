-- M08 §3 (first slice): prebuilt profiles + notebook servers. Environments/runs/home snapshots added later.
CREATE SCHEMA IF NOT EXISTS nb;

CREATE TABLE nb.profiles (
    name          text PRIMARY KEY,
    display_name  text    NOT NULL,
    description   text    NOT NULL,
    image_cpu     text    NOT NULL,
    image_gpu     text    NOT NULL,
    version       text    NOT NULL,
    enabled       boolean NOT NULL DEFAULT true,
    sort          int     NOT NULL
);
INSERT INTO nb.profiles (name, display_name, description, image_cpu, image_gpu, version, sort) VALUES
 ('base',        'Base',         'numpy, pandas, polars, pyarrow, scipy, scikit-learn, matplotlib, seaborn, plotly', 'mlp/nb-base:0.1-cpu',        'mlp/nb-base:0.1-gpu',        '0.1', 1),
 ('pytorch-gpu', 'PyTorch',      'base + PyTorch, CUDA/cuDNN, torchvision, torchaudio, Lightning',                   'mlp/nb-pytorch:0.1-cpu',     'mlp/nb-pytorch:0.1-gpu',     '0.1', 2),
 ('cv',          'Thị giác máy', 'pytorch + opencv, albumentations, timm, torchmetrics, pycocotools, supervision',   'mlp/nb-cv:0.1-cpu',          'mlp/nb-cv:0.1-gpu',          '0.1', 3),
 ('nlp-llm',     'NLP / LLM',    'pytorch + transformers, datasets, peft, trl, accelerate, bitsandbytes, vLLM',      'mlp/nb-nlp-llm:0.1-cpu',     'mlp/nb-nlp-llm:0.1-gpu',     '0.1', 4),
 ('tabular',     'Dữ liệu bảng', 'base + xgboost, lightgbm, catboost, optuna, shap',                                 'mlp/nb-tabular:0.1-cpu',     'mlp/nb-tabular:0.1-gpu',     '0.1', 5),
 ('audio',       'Âm thanh',     'pytorch + torchaudio, librosa, soundfile, jiwer',                                  'mlp/nb-audio:0.1-cpu',       'mlp/nb-audio:0.1-gpu',       '0.1', 6),
 ('tensorflow',  'TensorFlow',   'base + TensorFlow/Keras',                                                          'mlp/nb-tensorflow:0.1-cpu',  'mlp/nb-tensorflow:0.1-gpu',  '0.1', 7);

CREATE TABLE nb.servers (
    id               uuid PRIMARY KEY,
    workspace_id     uuid        NOT NULL,
    user_id          uuid        NOT NULL,
    name             text        NOT NULL,
    profile          text        NOT NULL,
    image            text        NOT NULL,
    ide              text        NOT NULL,
    cpus             double precision NOT NULL,
    memory_gb        int         NOT NULL,
    gpus             int         NOT NULL,
    host_id          uuid,
    endpoint         text,                    -- 127.0.0.1:<port> on the machine (loopback only, R2) — the agent connects over the data channel
    job_id           uuid,
    state            text        NOT NULL,    -- queued | starting | running | stopping | stopped | failed
    state_reason     text,
    idle_timeout_min int         NOT NULL,
    last_activity_at timestamptz NOT NULL,
    created_at       timestamptz NOT NULL,
    started_at       timestamptz,
    stopped_at       timestamptz
);
CREATE INDEX ix_nb_servers_ws ON nb.servers (workspace_id, user_id);
CREATE INDEX ix_nb_servers_job ON nb.servers (job_id);
