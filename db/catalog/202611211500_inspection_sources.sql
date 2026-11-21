-- Per-pane upstream code reported by the recipe inspection (canvas "Code" level): {"pane": [{"name","file","line","code"}]}.
ALTER TABLE catalog.inspections ADD COLUMN IF NOT EXISTS sources jsonb NOT NULL DEFAULT '{}';
