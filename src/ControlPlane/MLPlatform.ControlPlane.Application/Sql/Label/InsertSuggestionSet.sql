-- mlp:query InsertSuggestionSet :exec
INSERT INTO label.suggestion_sets (id, workspace_id, project_id, path, revision, source_kind, source_id, source_label, external, state, detail, items, unmapped, ai_run_id, latency_ms, job_id, created_by, created_at)
VALUES (@id, @workspace_id, @project_id, @path, @revision, @source_kind, @source_id, @source_label, @external, @state, @detail, @items_json::jsonb, @unmapped_json::jsonb, @ai_run_id, @latency_ms, @job_id, @created_by, @now);
