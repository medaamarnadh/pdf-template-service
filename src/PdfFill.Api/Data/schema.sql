-- POC schema. Column names deliberately match the mapping JSON keys
-- (or are aliased to them in the repository queries).
CREATE TABLE applicants (
  applicant_id     INTEGER PRIMARY KEY,
  business_name    TEXT NOT NULL,
  ein              TEXT NOT NULL,
  address          TEXT NOT NULL,
  city TEXT NOT NULL, state TEXT NOT NULL, zip TEXT NOT NULL,
  website          TEXT,
  naics            TEXT,
  entity_type      TEXT NOT NULL,     -- LLC | CORP | SOLEPROP | PARTNERSHIP | NONPROFIT
  year_established INTEGER NOT NULL,
  minority_owned   INTEGER NOT NULL,  -- 1/0  -> radio Yes/No
  veteran_owned    INTEGER NOT NULL,
  rural_location   INTEGER NOT NULL,
  employees_ft INTEGER NOT NULL, employees_pt INTEGER NOT NULL
);

CREATE TABLE contacts (
  applicant_id  INTEGER NOT NULL REFERENCES applicants(applicant_id),
  contact_name  TEXT NOT NULL,
  contact_title TEXT NOT NULL,
  phone TEXT NOT NULL, email TEXT NOT NULL
);

CREATE TABLE grant_applications (
  application_id  INTEGER PRIMARY KEY,
  application_no  TEXT NOT NULL,
  applicant_id    INTEGER NOT NULL REFERENCES applicants(applicant_id),
  project_title   TEXT NOT NULL,
  project_type    TEXT NOT NULL,      -- New | Expansion   (radio export values)
  project_start TEXT NOT NULL, project_end TEXT NOT NULL,
  jobs_created    INTEGER NOT NULL,
  project_summary TEXT, project_impact TEXT,
  reviewer_notes  TEXT, review_score INTEGER, review_decision TEXT   -- Approve | Defer | Decline
);

-- one row per fiscal year, up to 3 -> rev_fy{n}_*
CREATE TABLE revenue_quarters (
  application_id INTEGER NOT NULL REFERENCES grant_applications(application_id),
  row_no  INTEGER NOT NULL,
  label   TEXT NOT NULL,             -- e.g. 'FY 2024'
  q1 INTEGER, q2 INTEGER, q3 INTEGER, q4 INTEGER,
  PRIMARY KEY (application_id, row_no)
);

-- up to 8 rows -> budget{n}_*
CREATE TABLE budget_lines (
  application_id INTEGER NOT NULL REFERENCES grant_applications(application_id),
  row_no INTEGER NOT NULL,
  category TEXT NOT NULL, requested INTEGER NOT NULL, match INTEGER NOT NULL, justification TEXT,
  PRIMARY KEY (application_id, row_no)
);

-- up to 4 rows -> source{n}_*
CREATE TABLE funding_sources (
  application_id INTEGER NOT NULL REFERENCES grant_applications(application_id),
  row_no INTEGER NOT NULL,
  name TEXT NOT NULL, amount INTEGER NOT NULL, secured INTEGER NOT NULL,
  PRIMARY KEY (application_id, row_no)
);

-- one row per compliance item -> chk_<code>
CREATE TABLE compliance_items (
  application_id INTEGER NOT NULL REFERENCES grant_applications(application_id),
  code TEXT NOT NULL,                -- registered | taxes | nodebarment | insurance | audit | reporting
  confirmed INTEGER NOT NULL,
  PRIMARY KEY (application_id, code)
);

CREATE TABLE signatures (
  application_id INTEGER NOT NULL REFERENCES grant_applications(application_id),
  role TEXT NOT NULL,                -- applicant | preparer | reviewer
  signer_name TEXT NOT NULL,
  signed_on TEXT NOT NULL,
  image_png BLOB NOT NULL,
  PRIMARY KEY (application_id, role)
);

-- ---- Portal users (authentication) ------------------------------------------
CREATE TABLE users (
  user_id              TEXT PRIMARY KEY CHECK (length(user_id) = 36),
  email                TEXT NOT NULL UNIQUE COLLATE NOCASE,
  password_hash        TEXT NOT NULL,          -- pbkdf2$iterations$salt$hash (see Services/PasswordHasher.cs)
  role                 TEXT NOT NULL CHECK (role IN ('owner','admin','developer','viewer')),
  must_change_password INTEGER NOT NULL DEFAULT 0,
  created_at           TEXT NOT NULL,
  last_login_at        TEXT
);

-- ---- Tenancy: portal users own applications; applications own templates ----
CREATE TABLE applications (
  application_id TEXT PRIMARY KEY,
  owner_user_id  TEXT NOT NULL REFERENCES users(user_id),
  name           TEXT NOT NULL,
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);
CREATE INDEX ix_applications_owner ON applications(owner_user_id);

-- ---- Template service (REST resources) -------------------------------------
CREATE TABLE templates (
  template_id    TEXT PRIMARY KEY,
  application_id TEXT NOT NULL REFERENCES applications(application_id),
  name           TEXT NOT NULL,
  file_name      TEXT NOT NULL,
  pdf            BLOB NOT NULL,
  page_count     INTEGER NOT NULL,
  mapping_json   TEXT NOT NULL DEFAULT '{"fields":{}}',
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);
CREATE INDEX ix_templates_application ON templates(application_id);

CREATE TABLE api_keys (
  api_key_id    TEXT PRIMARY KEY,
  owner_user_id TEXT NOT NULL REFERENCES users(user_id),
  name          TEXT NOT NULL,
  prefix        TEXT NOT NULL,
  hash          TEXT NOT NULL UNIQUE,
  created_at    TEXT NOT NULL,
  last_used_at  TEXT,
  revoked_at    TEXT
);
CREATE INDEX ix_api_keys_owner ON api_keys(owner_user_id);

CREATE TABLE api_key_applications (
  api_key_id     TEXT NOT NULL REFERENCES api_keys(api_key_id) ON DELETE CASCADE,
  application_id TEXT NOT NULL REFERENCES applications(application_id) ON DELETE CASCADE,
  PRIMARY KEY (api_key_id, application_id)
);

PRAGMA user_version = 1;
