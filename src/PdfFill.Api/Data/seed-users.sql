-- First portal user. The password is stored ONLY as a PBKDF2-SHA256 hash (never plaintext).
-- The account is flagged must_change_password = 1: the portal asks for a new password at first login.
-- Temporary bootstrap password: ChangeMe-Seed-2026!  (must_change_password = 1 forces a new one at first sign-in).
-- To add another user, hash a password with: dotnet run --project src/PdfFill.Api -- hash-password "<password>"
INSERT INTO users (user_id, email, password_hash, role, must_change_password, created_at)
VALUES ('208bc8f0-91b8-415a-a835-91bd22bf1dbf', 'demo@example.com', 'pbkdf2$600000$BrVAUZQbWie8qJ6TVUtpLA==$mCaa7UTUTPMlosTXra87x9fZzkJkaQMiv2KkpUsboi8=', 'owner', 1, '2026-09-20T16:59:13Z');
