-- mlp:query UpsertBuiltinRecipe :exec
INSERT INTO catalog.recipes (id, workspace_id, name, display_name, family, description, maturity, tasks, license_code,
    license_weights, license_notes, requires_ack, gpu, min_vram_gb, source, manifest, manifest_sha256, state, selfcheck, updated_at)
VALUES (@id, NULL, @name, @display_name, @family, @description, @maturity, @tasks, @license_code, @license_weights, @license_notes,
    @requires_ack, @gpu, @min_vram_gb, @source, @manifest::jsonb, @manifest_sha256, @state, @selfcheck, @now)
ON CONFLICT (coalesce(workspace_id, '00000000-0000-0000-0000-000000000000'::uuid), name) DO UPDATE SET
    display_name = excluded.display_name, family = excluded.family, description = excluded.description,
    maturity = excluded.maturity, tasks = excluded.tasks, license_code = excluded.license_code,
    license_weights = excluded.license_weights, license_notes = excluded.license_notes, requires_ack = excluded.requires_ack,
    gpu = excluded.gpu, min_vram_gb = excluded.min_vram_gb, source = excluded.source, manifest = excluded.manifest,
    manifest_sha256 = excluded.manifest_sha256, state = excluded.state, selfcheck = excluded.selfcheck, updated_at = excluded.updated_at
WHERE catalog.recipes.manifest_sha256 <> excluded.manifest_sha256;
