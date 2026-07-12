ALTER TABLE student_subscriptions
    ADD COLUMN IF NOT EXISTS stripe_subscription_id character varying(100);
