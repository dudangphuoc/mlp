-- mlp:query ActiveAgentUpgrades :many
-- mlp:no-workspace api restarted: resume tracking every unfinished job of the instance
SELECT id, workspace_id, host_id, to_version, state FROM fleet.agent_upgrades WHERE state IN ('queued', 'running');
