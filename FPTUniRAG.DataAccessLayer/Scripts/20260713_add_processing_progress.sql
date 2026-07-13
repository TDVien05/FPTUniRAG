ALTER TABLE processing_jobs
    ADD COLUMN IF NOT EXISTS progress_percent integer NOT NULL DEFAULT 0;

ALTER TABLE processing_jobs
    ADD COLUMN IF NOT EXISTS processing_stage character varying(50);

UPDATE processing_jobs
SET progress_percent = CASE
        WHEN job_status = 'completed' THEN 100
        WHEN job_status = 'failed' THEN progress_percent
        ELSE 0
    END,
    processing_stage = CASE
        WHEN job_status = 'completed' THEN 'completed'
        WHEN job_status = 'failed' THEN 'failed'
        ELSE COALESCE(processing_stage, job_status)
    END
WHERE processing_stage IS NULL OR progress_percent = 0;
