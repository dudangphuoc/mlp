-- mlp:query ListWorkspaceSlugs :many
-- mlp:no-workspace public subdomain routing needs the slug ↔ id mapping of every workspace
SELECT id, slug FROM identity.workspaces;
