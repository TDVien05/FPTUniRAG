CREATE TABLE IF NOT EXISTS embedding_settings (
    setting_id smallint PRIMARY KEY DEFAULT 1,
    embedding_model character varying(255) NOT NULL,
    embedding_dimensions integer NOT NULL,
    updated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by uuid,
    CONSTRAINT embedding_settings_singleton CHECK (setting_id = 1)
);
