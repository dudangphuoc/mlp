-- Run model directory (D-28) under mlp-artifacts/{ws}/runs/{run}/model/
CREATE TABLE track.run_artifacts (
    run_id     uuid   NOT NULL REFERENCES track.runs(id),
    path       text   NOT NULL,
    size_bytes bigint NOT NULL,
    PRIMARY KEY (run_id, path)
);
