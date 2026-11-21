-- mlp:query SlugExists :scalar
SELECT EXISTS (SELECT 1 FROM identity.workspaces WHERE slug = @slug);
