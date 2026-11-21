-- English data for rows seeded in Vietnamese by earlier migrations (the UI translates English to Vietnamese).
UPDATE nb.profiles SET display_name = 'Computer Vision' WHERE name = 'cv';
UPDATE nb.profiles SET display_name = 'Tabular Data' WHERE name = 'tabular';
UPDATE nb.profiles SET display_name = 'Audio' WHERE name = 'audio';
CREATE OR REPLACE FUNCTION audit.forbid_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit.events is append-only'; END $$;
