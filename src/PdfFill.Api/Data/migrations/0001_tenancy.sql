-- 0001 tenancy: applications own templates, API keys are assigned to applications.
-- The old `applications` table holds grant applications (POC business data) and is renamed out of the way.

ALTER TABLE applications RENAME TO grant_applications;

CREATE TABLE applications (
  application_id TEXT PRIMARY KEY,                 -- uuid v4
  owner_user_id  TEXT NOT NULL REFERENCES users(user_id),
  name           TEXT NOT NULL,
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);
CREATE INDEX ix_applications_owner ON applications(owner_user_id);

CREATE TABLE api_keys (
  api_key_id    TEXT PRIMARY KEY,                  -- uuid v4
  owner_user_id TEXT NOT NULL REFERENCES users(user_id),
  name          TEXT NOT NULL,
  prefix        TEXT NOT NULL,                     -- first 12 chars of the secret, for display
  hash          TEXT NOT NULL UNIQUE,              -- sha-256 hex of the full secret
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

-- One application to hold everything that already exists, owned by the earliest owner user.
INSERT INTO applications (application_id, owner_user_id, name, created_at, updated_at)
SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))), 2)
         || '-a' || substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6))),
       user_id, 'Default', strftime('%Y-%m-%dT%H:%M:%SZ', 'now'), strftime('%Y-%m-%dT%H:%M:%SZ', 'now')
FROM users ORDER BY created_at LIMIT 1;

-- SQLite cannot add a NOT NULL foreign-key column in place, so rebuild templates with UUID ids.
CREATE TABLE templates_new (
  template_id    TEXT PRIMARY KEY,                 -- uuid v4
  application_id TEXT NOT NULL REFERENCES applications(application_id),
  name           TEXT NOT NULL,
  file_name      TEXT NOT NULL,
  pdf            BLOB NOT NULL,
  page_count     INTEGER NOT NULL,
  mapping_json   TEXT NOT NULL DEFAULT '{"fields":{}}',
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);

INSERT INTO templates_new (template_id, application_id, name, file_name, pdf, page_count, mapping_json, created_at, updated_at)
SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))), 2)
         || '-a' || substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6))),
       (SELECT application_id FROM applications WHERE name = 'Default'),
       t.name, t.file_name, t.pdf, t.page_count, t.mapping_json, t.created_at, t.updated_at
FROM templates t;

DROP TABLE templates;
ALTER TABLE templates_new RENAME TO templates;
CREATE INDEX ix_templates_application ON templates(application_id);
