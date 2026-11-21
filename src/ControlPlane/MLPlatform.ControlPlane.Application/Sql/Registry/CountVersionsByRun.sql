-- mlp:query CountVersionsByRun :scalar
SELECT count(*) FROM registry.versions WHERE run_id = @run_id;
