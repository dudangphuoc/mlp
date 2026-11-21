-- Labels that already exist next to an image (YOLO txt of an imported or exported version) are loaded into the editor the first time
-- the item is opened; this marks that it was done, so deleting every imported box does not bring them back.
ALTER TABLE label.item_states ADD COLUMN labels_imported boolean NOT NULL DEFAULT false;
