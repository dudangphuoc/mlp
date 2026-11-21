-- mlp:query InsertStageTransition :exec
INSERT INTO registry.stage_transitions (id, version_id, from_stage, to_stage, actor_id, reason, at)
VALUES (@id, @version_id, @from_stage, @to_stage, @actor_id, @reason, @now);
