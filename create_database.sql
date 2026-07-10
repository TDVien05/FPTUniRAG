-- PostgreSQL bootstrap script for FPT UniRAG Razor Pages
-- Usage example:
--   psql -U postgres -f create_database.sql

SELECT 'CREATE DATABASE prn222'
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_database
    WHERE datname = 'prn222'
)\gexec

\connect prn222;

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE TABLE IF NOT EXISTS users (
    user_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    full_name character varying(255) NOT NULL,
    email character varying(255) NOT NULL,
    password_hash text NOT NULL,
    role character varying(50) DEFAULT 'student',
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    is_blocked boolean NOT NULL DEFAULT false,
    student_code character varying(50),
    password_reset_token_hash text,
    password_reset_token_expires_at timestamp without time zone
);

CREATE UNIQUE INDEX IF NOT EXISTS users_email_key
    ON users (email);

CREATE UNIQUE INDEX IF NOT EXISTS users_student_code_key
    ON users (student_code)
    WHERE student_code IS NOT NULL;

CREATE TABLE IF NOT EXISTS subscription_plans (
    plan_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    plan_code character varying(50) NOT NULL,
    plan_name character varying(100) NOT NULL,
    description text,
    monthly_price numeric(12,2) NOT NULL DEFAULT 0,
    daily_token_limit bigint,
    weekly_token_limit bigint,
    monthly_token_limit bigint,
    has_unlimited_chat boolean NOT NULL DEFAULT false,
    has_advanced_models boolean NOT NULL DEFAULT false,
    has_priority_support boolean NOT NULL DEFAULT false,
    has_file_upload boolean NOT NULL DEFAULT true,
    has_history_export boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS subscription_plans_plan_code_key
    ON subscription_plans (plan_code);

CREATE TABLE IF NOT EXISTS student_subscriptions (
    student_subscription_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    subscription_status character varying(50) NOT NULL DEFAULT 'active',
    started_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamp without time zone,
    purchased_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    canceled_at timestamp without time zone,
    auto_renew boolean NOT NULL DEFAULT false,
    granted_by uuid,
    notes text
);

CREATE INDEX IF NOT EXISTS idx_student_subscriptions_user
    ON student_subscriptions (user_id);

CREATE INDEX IF NOT EXISTS idx_student_subscriptions_plan
    ON student_subscriptions (plan_id);

CREATE INDEX IF NOT EXISTS idx_student_subscriptions_status
    ON student_subscriptions (subscription_status);

CREATE UNIQUE INDEX IF NOT EXISTS student_subscriptions_one_active_plan_per_user
    ON student_subscriptions (user_id)
    WHERE subscription_status = 'active';

CREATE TABLE IF NOT EXISTS token_usage_logs (
    token_usage_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id uuid NOT NULL,
    session_id uuid,
    message_id uuid,
    plan_id uuid,
    feature_name character varying(100) NOT NULL DEFAULT 'student_chat',
    provider_name character varying(100),
    model_name character varying(100),
    prompt_tokens bigint NOT NULL DEFAULT 0,
    completion_tokens bigint NOT NULL DEFAULT 0,
    total_tokens bigint NOT NULL DEFAULT 0,
    request_count integer NOT NULL DEFAULT 1,
    used_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    metadata_json jsonb,
    CONSTRAINT ck_token_usage_logs_non_negative CHECK (
        prompt_tokens >= 0
        AND completion_tokens >= 0
        AND total_tokens >= 0
        AND request_count >= 0
    ),
    CONSTRAINT ck_token_usage_logs_total_matches CHECK (
        total_tokens = prompt_tokens + completion_tokens
    )
);

CREATE INDEX IF NOT EXISTS idx_token_usage_logs_user_used_at
    ON token_usage_logs (user_id, used_at DESC);

CREATE INDEX IF NOT EXISTS idx_token_usage_logs_plan
    ON token_usage_logs (plan_id);

CREATE INDEX IF NOT EXISTS idx_token_usage_logs_session
    ON token_usage_logs (session_id);

CREATE INDEX IF NOT EXISTS idx_token_usage_logs_message
    ON token_usage_logs (message_id);

CREATE TABLE IF NOT EXISTS teachers (
    teacher_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    full_name character varying(255) NOT NULL,
    email character varying(255),
    department character varying(255),
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS teachers_email_key
    ON teachers (email)
    WHERE email IS NOT NULL;

CREATE TABLE IF NOT EXISTS subjects (
    subject_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    subject_code character varying(50) NOT NULL,
    subject_name character varying(255) NOT NULL,
    description text,
    default_chunking_strategy character varying(50) NOT NULL DEFAULT 'fixed',
    default_fixed_chunk_size integer NOT NULL DEFAULT 800,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS subjects_subject_code_key
    ON subjects (subject_code);

CREATE TABLE IF NOT EXISTS chapters (
    chapter_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    subject_id uuid NOT NULL,
    chapter_title character varying(255) NOT NULL,
    chapter_order integer NOT NULL DEFAULT 1,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_chapters_subject
    ON chapters (subject_id);

CREATE UNIQUE INDEX IF NOT EXISTS chapters_subject_normalized_title_key
    ON chapters (subject_id, lower(btrim(chapter_title)));

CREATE TABLE IF NOT EXISTS teacher_subjects (
    teacher_subject_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    teacher_id uuid NOT NULL,
    subject_id uuid NOT NULL,
    is_head_of_department boolean NOT NULL DEFAULT false,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_teacher_subjects_teacher
    ON teacher_subjects (teacher_id);

CREATE INDEX IF NOT EXISTS idx_teacher_subjects_subject
    ON teacher_subjects (subject_id);

CREATE UNIQUE INDEX IF NOT EXISTS teacher_subjects_teacher_subject_key
    ON teacher_subjects (teacher_id, subject_id);

CREATE UNIQUE INDEX IF NOT EXISTS teacher_subjects_one_leader_per_subject
    ON teacher_subjects (subject_id, is_head_of_department)
    WHERE is_head_of_department;

CREATE TABLE IF NOT EXISTS sessions (
    session_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id uuid NOT NULL,
    subject_id uuid,
    started_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    ended_at timestamp without time zone
);

CREATE INDEX IF NOT EXISTS IX_sessions_user_id
    ON sessions (user_id);

CREATE INDEX IF NOT EXISTS idx_sessions_subject
    ON sessions (subject_id);

CREATE TABLE IF NOT EXISTS messages (
    message_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    session_id uuid NOT NULL,
    sender_role character varying(50) NOT NULL,
    message_content text NOT NULL,
    citations_json jsonb,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_messages_session
    ON messages (session_id);

CREATE TABLE IF NOT EXISTS documents (
    document_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    chapter_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    file_url text NOT NULL,
    file_type character varying(50),
    chunking_strategy character varying(50) NOT NULL,
    chunk_size integer NOT NULL,
    chunk_overlap integer NOT NULL,
    uploaded_by uuid,
    uploaded_teacher uuid,
    status character varying(50) DEFAULT 'pending',
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    subject_id uuid NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_documents_uploaded_by
    ON documents (uploaded_by);

CREATE INDEX IF NOT EXISTS IX_documents_uploaded_teacher
    ON documents (uploaded_teacher);

CREATE INDEX IF NOT EXISTS idx_documents_chapter
    ON documents (chapter_id);

CREATE INDEX IF NOT EXISTS idx_documents_subject
    ON documents (subject_id);

CREATE UNIQUE INDEX IF NOT EXISTS documents_chapter_id_key
    ON documents (chapter_id);

CREATE TABLE IF NOT EXISTS chunks (
    chunk_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    document_id uuid NOT NULL,
    chunk_index integer NOT NULL,
    content text NOT NULL,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_chunks_document
    ON chunks (document_id);

CREATE TABLE IF NOT EXISTS processing_jobs (
    job_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    document_id uuid NOT NULL,
    job_status character varying(50) DEFAULT 'queued',
    started_at timestamp without time zone,
    finished_at timestamp without time zone,
    error_message text
);

CREATE INDEX IF NOT EXISTS idx_processing_document
    ON processing_jobs (document_id);

CREATE TABLE IF NOT EXISTS test_questions (
    question_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    chapter_id uuid NOT NULL,
    question_text text NOT NULL,
    difficulty character varying(50),
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_questions_chapter
    ON test_questions (chapter_id);

CREATE TABLE IF NOT EXISTS benchmark_runs (
    benchmark_run_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    run_name character varying(255),
    executed_by uuid,
    started_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    completed_at timestamp without time zone
);

CREATE INDEX IF NOT EXISTS IX_benchmark_runs_executed_by
    ON benchmark_runs (executed_by);

CREATE TABLE IF NOT EXISTS benchmark_results (
    result_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    benchmark_run_id uuid NOT NULL,
    question_id uuid,
    score numeric(5,2),
    response_time_ms integer,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS IX_benchmark_results_benchmark_run_id
    ON benchmark_results (benchmark_run_id);

CREATE INDEX IF NOT EXISTS IX_benchmark_results_question_id
    ON benchmark_results (question_id);

INSERT INTO subscription_plans (
    plan_code,
    plan_name,
    description,
    monthly_price,
    daily_token_limit,
    weekly_token_limit,
    monthly_token_limit,
    has_unlimited_chat,
    has_advanced_models,
    has_priority_support,
    has_file_upload,
    has_history_export,
    is_active
)
VALUES
    (
        'go',
        'Go',
        'Entry package for light study sessions and quick topic checks.',
        49000,
        NULL,
        NULL,
        3000,
        false,
        false,
        false,
        true,
        false,
        true
    ),
    (
        'plus',
        'Plus',
        'Higher token allowance for longer study flows across multiple questions.',
        99000,
        NULL,
        NULL,
        7000,
        false,
        true,
        false,
        true,
        true,
        true
    ),
    (
        'pro',
        'Pro',
        'Premium package with stronger support features for deeper guided study.',
        149000,
        NULL,
        NULL,
        5000,
        false,
        true,
        true,
        true,
        true,
        true
    )
ON CONFLICT (plan_code) DO UPDATE
SET
    plan_name = EXCLUDED.plan_name,
    description = EXCLUDED.description,
    monthly_price = EXCLUDED.monthly_price,
    daily_token_limit = EXCLUDED.daily_token_limit,
    weekly_token_limit = EXCLUDED.weekly_token_limit,
    monthly_token_limit = EXCLUDED.monthly_token_limit,
    has_unlimited_chat = EXCLUDED.has_unlimited_chat,
    has_advanced_models = EXCLUDED.has_advanced_models,
    has_priority_support = EXCLUDED.has_priority_support,
    has_file_upload = EXCLUDED.has_file_upload,
    has_history_export = EXCLUDED.has_history_export,
    is_active = EXCLUDED.is_active;

ALTER TABLE chapters
    DROP CONSTRAINT IF EXISTS fk_chapter_subject;
ALTER TABLE chapters
    ADD CONSTRAINT fk_chapter_subject
    FOREIGN KEY (subject_id)
    REFERENCES subjects(subject_id)
    ON DELETE CASCADE;

ALTER TABLE teacher_subjects
    DROP CONSTRAINT IF EXISTS fk_teacher_subject_teacher;
ALTER TABLE teacher_subjects
    ADD CONSTRAINT fk_teacher_subject_teacher
    FOREIGN KEY (teacher_id)
    REFERENCES teachers(teacher_id)
    ON DELETE CASCADE;

ALTER TABLE teacher_subjects
    DROP CONSTRAINT IF EXISTS fk_teacher_subject_subject;
ALTER TABLE teacher_subjects
    ADD CONSTRAINT fk_teacher_subject_subject
    FOREIGN KEY (subject_id)
    REFERENCES subjects(subject_id)
    ON DELETE CASCADE;

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

ALTER TABLE sessions
    DROP CONSTRAINT IF EXISTS fk_session_user;
ALTER TABLE sessions
    ADD CONSTRAINT fk_session_user
    FOREIGN KEY (user_id)
    REFERENCES users(user_id);

ALTER TABLE student_subscriptions
    DROP CONSTRAINT IF EXISTS fk_student_subscription_user;
ALTER TABLE student_subscriptions
    ADD CONSTRAINT fk_student_subscription_user
    FOREIGN KEY (user_id)
    REFERENCES users(user_id)
    ON DELETE CASCADE;

ALTER TABLE student_subscriptions
    DROP CONSTRAINT IF EXISTS fk_student_subscription_plan;
ALTER TABLE student_subscriptions
    ADD CONSTRAINT fk_student_subscription_plan
    FOREIGN KEY (plan_id)
    REFERENCES subscription_plans(plan_id);

ALTER TABLE student_subscriptions
    DROP CONSTRAINT IF EXISTS fk_student_subscription_granted_by;
ALTER TABLE student_subscriptions
    ADD CONSTRAINT fk_student_subscription_granted_by
    FOREIGN KEY (granted_by)
    REFERENCES users(user_id)
    ON DELETE SET NULL;

ALTER TABLE sessions
    DROP CONSTRAINT IF EXISTS fk_session_subject;
ALTER TABLE sessions
    ADD CONSTRAINT fk_session_subject
    FOREIGN KEY (subject_id)
    REFERENCES subjects(subject_id)
    ON DELETE CASCADE;

ALTER TABLE messages
    DROP CONSTRAINT IF EXISTS fk_message_session;
ALTER TABLE messages
    ADD CONSTRAINT fk_message_session
    FOREIGN KEY (session_id)
    REFERENCES sessions(session_id)
    ON DELETE CASCADE;

ALTER TABLE token_usage_logs
    DROP CONSTRAINT IF EXISTS fk_token_usage_log_user;
ALTER TABLE token_usage_logs
    ADD CONSTRAINT fk_token_usage_log_user
    FOREIGN KEY (user_id)
    REFERENCES users(user_id)
    ON DELETE CASCADE;

ALTER TABLE token_usage_logs
    DROP CONSTRAINT IF EXISTS fk_token_usage_log_session;
ALTER TABLE token_usage_logs
    ADD CONSTRAINT fk_token_usage_log_session
    FOREIGN KEY (session_id)
    REFERENCES sessions(session_id)
    ON DELETE SET NULL;

ALTER TABLE token_usage_logs
    DROP CONSTRAINT IF EXISTS fk_token_usage_log_message;
ALTER TABLE token_usage_logs
    ADD CONSTRAINT fk_token_usage_log_message
    FOREIGN KEY (message_id)
    REFERENCES messages(message_id)
    ON DELETE SET NULL;

ALTER TABLE token_usage_logs
    DROP CONSTRAINT IF EXISTS fk_token_usage_log_plan;
ALTER TABLE token_usage_logs
    ADD CONSTRAINT fk_token_usage_log_plan
    FOREIGN KEY (plan_id)
    REFERENCES subscription_plans(plan_id)
    ON DELETE SET NULL;

ALTER TABLE documents
    DROP CONSTRAINT IF EXISTS fk_document_chapter;
ALTER TABLE documents
    ADD CONSTRAINT fk_document_chapter
    FOREIGN KEY (chapter_id)
    REFERENCES chapters(chapter_id)
    ON DELETE CASCADE;

ALTER TABLE documents
    DROP CONSTRAINT IF EXISTS fk_document_subject;
ALTER TABLE documents
    ADD CONSTRAINT fk_document_subject
    FOREIGN KEY (subject_id)
    REFERENCES subjects(subject_id)
    ON DELETE CASCADE;

ALTER TABLE documents
    DROP CONSTRAINT IF EXISTS fk_document_user;
ALTER TABLE documents
    ADD CONSTRAINT fk_document_user
    FOREIGN KEY (uploaded_by)
    REFERENCES users(user_id)
    ON DELETE SET NULL;

ALTER TABLE documents
    DROP CONSTRAINT IF EXISTS fk_document_teacher;
ALTER TABLE documents
    ADD CONSTRAINT fk_document_teacher
    FOREIGN KEY (uploaded_teacher)
    REFERENCES teachers(teacher_id)
    ON DELETE SET NULL;

ALTER TABLE chunks
    DROP CONSTRAINT IF EXISTS fk_chunk_document;
ALTER TABLE chunks
    ADD CONSTRAINT fk_chunk_document
    FOREIGN KEY (document_id)
    REFERENCES documents(document_id)
    ON DELETE CASCADE;

ALTER TABLE processing_jobs
    DROP CONSTRAINT IF EXISTS fk_processing_document;
ALTER TABLE processing_jobs
    ADD CONSTRAINT fk_processing_document
    FOREIGN KEY (document_id)
    REFERENCES documents(document_id)
    ON DELETE CASCADE;

ALTER TABLE test_questions
    DROP CONSTRAINT IF EXISTS fk_question_chapter;
ALTER TABLE test_questions
    ADD CONSTRAINT fk_question_chapter
    FOREIGN KEY (chapter_id)
    REFERENCES chapters(chapter_id)
    ON DELETE CASCADE;

ALTER TABLE benchmark_runs
    DROP CONSTRAINT IF EXISTS fk_benchmark_user;
ALTER TABLE benchmark_runs
    ADD CONSTRAINT fk_benchmark_user
    FOREIGN KEY (executed_by)
    REFERENCES users(user_id)
    ON DELETE SET NULL;

ALTER TABLE benchmark_results
    DROP CONSTRAINT IF EXISTS fk_result_run;
ALTER TABLE benchmark_results
    ADD CONSTRAINT fk_result_run
    FOREIGN KEY (benchmark_run_id)
    REFERENCES benchmark_runs(benchmark_run_id);

ALTER TABLE benchmark_results
    DROP CONSTRAINT IF EXISTS fk_result_question;
ALTER TABLE benchmark_results
    ADD CONSTRAINT fk_result_question
    FOREIGN KEY (question_id)
    REFERENCES test_questions(question_id)
    ON DELETE SET NULL;

CREATE OR REPLACE VIEW student_active_chat_entitlements AS
SELECT
    u.user_id,
    u.full_name,
    u.email,
    sp.plan_id,
    COALESCE(sp.plan_code, 'free') AS plan_code,
    COALESCE(sp.plan_name, 'Free') AS plan_name,
    sp.daily_token_limit,
    sp.weekly_token_limit,
    COALESCE(sp.monthly_token_limit, 2000) AS monthly_token_limit,
    COALESCE(sp.has_unlimited_chat, false) AS has_unlimited_chat,
    COALESCE(sp.has_advanced_models, false) AS has_advanced_models,
    COALESCE(sp.has_priority_support, false) AS has_priority_support,
    COALESCE(sp.has_file_upload, false) AS has_file_upload,
    COALESCE(sp.has_history_export, false) AS has_history_export,
    ss.started_at,
    ss.expires_at,
    COALESCE(ss.subscription_status, 'free') AS subscription_status
FROM users u
LEFT JOIN student_subscriptions ss
    ON ss.user_id = u.user_id
   AND ss.subscription_status = 'active'
LEFT JOIN subscription_plans sp
    ON sp.plan_id = ss.plan_id
WHERE u.role = 'student';

CREATE OR REPLACE VIEW student_token_usage_current_day AS
SELECT
    u.user_id,
    u.full_name,
    u.email,
    COALESCE(SUM(tul.prompt_tokens), 0) AS prompt_tokens_used_today,
    COALESCE(SUM(tul.completion_tokens), 0) AS completion_tokens_used_today,
    COALESCE(SUM(tul.total_tokens), 0) AS total_tokens_used_today,
    COALESCE(SUM(tul.request_count), 0) AS requests_today
FROM users u
LEFT JOIN token_usage_logs tul
    ON tul.user_id = u.user_id
   AND tul.used_at >= date_trunc('day', CURRENT_TIMESTAMP)
WHERE u.role = 'student'
GROUP BY u.user_id, u.full_name, u.email;

CREATE OR REPLACE VIEW student_token_usage_current_week AS
SELECT
    u.user_id,
    u.full_name,
    u.email,
    COALESCE(SUM(tul.prompt_tokens), 0) AS prompt_tokens_used_this_week,
    COALESCE(SUM(tul.completion_tokens), 0) AS completion_tokens_used_this_week,
    COALESCE(SUM(tul.total_tokens), 0) AS total_tokens_used_this_week,
    COALESCE(SUM(tul.request_count), 0) AS requests_this_week
FROM users u
LEFT JOIN token_usage_logs tul
    ON tul.user_id = u.user_id
   AND tul.used_at >= date_trunc('week', CURRENT_TIMESTAMP)
WHERE u.role = 'student'
GROUP BY u.user_id, u.full_name, u.email;

CREATE OR REPLACE VIEW student_token_usage_current_month AS
SELECT
    u.user_id,
    u.full_name,
    u.email,
    COALESCE(SUM(tul.prompt_tokens), 0) AS prompt_tokens_used_this_month,
    COALESCE(SUM(tul.completion_tokens), 0) AS completion_tokens_used_this_month,
    COALESCE(SUM(tul.total_tokens), 0) AS total_tokens_used_this_month,
    COALESCE(SUM(tul.request_count), 0) AS requests_this_month
FROM users u
LEFT JOIN token_usage_logs tul
    ON tul.user_id = u.user_id
   AND tul.used_at >= date_trunc('month', CURRENT_TIMESTAMP)
WHERE u.role = 'student'
GROUP BY u.user_id, u.full_name, u.email;
