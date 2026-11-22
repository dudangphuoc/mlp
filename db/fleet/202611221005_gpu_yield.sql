-- gpu_priority=yield and a container of another agent holds a GPU on this machine → no new GPU work here.
ALTER TABLE fleet.hosts ADD COLUMN IF NOT EXISTS gpu_yield boolean NOT NULL DEFAULT false;
