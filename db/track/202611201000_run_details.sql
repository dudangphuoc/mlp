-- M09 depth pass (D-41): a run is identifiable (#number per project), remembers what it trained on and with what resources,
-- so the run list can show it and "train again" can prefill the form.
ALTER TABLE track.runs ADD COLUMN number int;
ALTER TABLE track.runs ADD COLUMN dataset_version_id uuid;
ALTER TABLE track.runs ADD COLUMN from_scratch boolean NOT NULL DEFAULT false;
ALTER TABLE track.runs ADD COLUMN gpus int NOT NULL DEFAULT 0;
ALTER TABLE track.runs ADD COLUMN cpus double precision NOT NULL DEFAULT 1;

UPDATE track.runs r SET number = n.rn
FROM (SELECT id, row_number() OVER (PARTITION BY project_id ORDER BY created_at, id) AS rn FROM track.runs) n
WHERE n.id = r.id;
ALTER TABLE track.runs ALTER COLUMN number SET NOT NULL;
CREATE UNIQUE INDEX runs_project_number ON track.runs (project_id, number);

-- Existing runs: recover dataset version and resources from the job spec. Best effort — an unreadable spec leaves the defaults.
DO $$
BEGIN
  UPDATE track.runs r SET
    dataset_version_id = NULLIF((j.spec->'env'->>'MLP_JOB_SPEC')::jsonb->>'datasetVersionId', '')::uuid,
    from_scratch = jsonb_typeof((j.spec->'env'->>'MLP_JOB_SPEC')::jsonb->'weights') IS DISTINCT FROM 'string',
    gpus = COALESCE((j.spec->>'gpus')::int, 0),
    cpus = COALESCE((j.spec->>'cpus')::double precision, 1)
  FROM sched.jobs j
  WHERE j.id = r.job_id AND j.spec->'env' ? 'MLP_JOB_SPEC';
EXCEPTION WHEN others THEN
  RAISE NOTICE 'run backfill skipped: %', SQLERRM;
END $$;
