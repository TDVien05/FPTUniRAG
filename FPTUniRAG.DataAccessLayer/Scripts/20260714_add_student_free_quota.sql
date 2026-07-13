CREATE TABLE IF NOT EXISTS student_free_quota_settings (
    setting_id smallint PRIMARY KEY DEFAULT 1,
    monthly_token_limit bigint NOT NULL DEFAULT 2000,
    updated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by uuid,
    CONSTRAINT student_free_quota_settings_singleton CHECK (setting_id = 1),
    CONSTRAINT student_free_quota_settings_positive_limit CHECK (monthly_token_limit > 0)
);

INSERT INTO student_free_quota_settings (setting_id, monthly_token_limit)
VALUES (1, 2000)
ON CONFLICT (setting_id) DO NOTHING;
