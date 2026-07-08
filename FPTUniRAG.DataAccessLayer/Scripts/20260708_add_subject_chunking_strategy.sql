ALTER TABLE subjects
    ADD COLUMN IF NOT EXISTS default_chunking_strategy character varying(50) NOT NULL DEFAULT 'fixed';

ALTER TABLE subjects
    ADD COLUMN IF NOT EXISTS default_fixed_chunk_size integer NOT NULL DEFAULT 800;

UPDATE subjects
SET default_chunking_strategy = 'fixed'
WHERE default_chunking_strategy IS NULL
   OR btrim(default_chunking_strategy) = '';

UPDATE subjects
SET default_fixed_chunk_size = 800
WHERE default_fixed_chunk_size IS NULL
   OR default_fixed_chunk_size <= 0;

ALTER TABLE subjects
    DROP CONSTRAINT IF EXISTS ck_subjects_default_chunking_strategy;

ALTER TABLE subjects
    ADD CONSTRAINT ck_subjects_default_chunking_strategy
    CHECK (lower(default_chunking_strategy) IN ('fixed', 'semantic'));

ALTER TABLE subjects
    DROP CONSTRAINT IF EXISTS ck_subjects_default_fixed_chunk_size;

ALTER TABLE subjects
    ADD CONSTRAINT ck_subjects_default_fixed_chunk_size
    CHECK (default_fixed_chunk_size > 0);
