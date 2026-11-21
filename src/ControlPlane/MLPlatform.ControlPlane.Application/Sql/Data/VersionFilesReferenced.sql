-- mlp:query VersionFilesReferenced :scalar
-- mlp:no-workspace the url prefix is the version's own storage folder inside the workspace
-- Later versions point at the files of earlier ones instead of copying them: those objects must stay when the earlier version is deleted.
SELECT count(*) FROM data.version_files WHERE url LIKE @prefix_url || '%' AND version_id <> @version_id;
