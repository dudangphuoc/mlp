-- A job returned to the queue (waiting for a machine/resources) backs off 5 s so it does not block jobs behind it (head-of-line).
ALTER TABLE sched.jobs ADD COLUMN not_before timestamptz;
