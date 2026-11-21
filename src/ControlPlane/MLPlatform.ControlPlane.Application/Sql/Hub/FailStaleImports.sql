-- mlp:query FailStaleImports :scalar
-- mlp:no-workspace api restarted: unfinished jobs (HTTP stream download interrupted) cannot resume → mark failed so the user imports again
WITH failed AS (
    UPDATE hub.imports SET state = 'failed', message = 'The api restarted during the import — please import again', updated_at = @now, finished_at = @now
    WHERE state IN ('queued', 'running') RETURNING id
)
SELECT count(*) FROM failed;
