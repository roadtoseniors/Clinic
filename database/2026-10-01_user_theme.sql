-- Additive migration for the isolated course database; preserves existing data.
CREATE TABLE IF NOT EXISTS user_theme (
    account_id integer PRIMARY KEY REFERENCES account(id) ON DELETE CASCADE,
    theme varchar(5) NOT NULL DEFAULT 'light' CHECK (theme IN ('light', 'dark')),
    updated_at timestamptz NOT NULL DEFAULT now()
);

-- The Desktop preview connects directly to this test DB. The API should own this write in production.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinic_preview') THEN
        GRANT SELECT, INSERT ON user_theme TO clinic_preview;
        GRANT UPDATE (theme, updated_at) ON user_theme TO clinic_preview;
    END IF;
END $$;
