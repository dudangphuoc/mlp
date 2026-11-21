-- Token issued to a workload container at launch (shim → TrackingService/RuntimeService), separate from the holder token.
ALTER TABLE sched.job_attempts ADD COLUMN workload_token_hash text UNIQUE;
