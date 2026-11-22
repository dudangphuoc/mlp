-- Structured details (compact JSON text, hashed byte for byte) for audited actions (file path, size, sha256, terminal shell/duration); part of the hash when present.
ALTER TABLE audit.events ADD COLUMN IF NOT EXISTS details text;
