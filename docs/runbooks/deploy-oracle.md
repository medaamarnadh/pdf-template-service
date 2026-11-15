# Runbook — deploy to Oracle Cloud Always Free

Target: one Ampere A1 VM (Arm, up to 4 OCPU / 24 GB always free), Docker Compose, Caddy for TLS, SQLite on the VM disk.
Cost: $0. A card is required at signup for a small verification hold.

## 1. Create the VM (once)
1. Sign up at cloud.oracle.com; choose a home region you won't need to change.
2. Compute → Instances → Create. Image: **Ubuntu 24.04 (aarch64)**. Shape: **VM.Standard.A1.Flex**, 2–4 OCPU, 6–12 GB RAM (all within Always Free). Add your SSH public key. Boot volume 50 GB.
   - "Out of capacity" is common for A1 in busy regions: retry later or try another availability domain.
3. Networking → your VCN → Subnet → **Security List** → add ingress rules: TCP 80 and TCP 443 from 0.0.0.0/0 (22 is already open).
4. Optional: reserve the public IP (Networking → Reserved IPs) so it survives instance recreation, and create a DNS A record `pdf.example.com → <ip>`.

## 2. Publish an image first (once per release)
The compose file pulls `ghcr.io/medaamarnadh/pdf-template-service:latest`, which exists only after a release tag is
pushed — CI builds the image but discards it. Cut a release before deploying:
```bash
git tag v0.1.0 && git push origin v0.1.0     # runs release.yml (~15-20 min; arm64 builds under QEMU)
```
Then make the package pullable from the VM: GitHub → Profile → Packages → `pdf-template-service` → Package settings →
**Change visibility → Public**. The image contains no secrets (`JWT_KEY` is injected at runtime from `.env`) and the
source repo stays private. To keep the package private instead, run `docker login ghcr.io -u <user>` on the VM with a
PAT scoped `read:packages` before `docker compose pull`.

## 3. Prepare the VM (once)
The repo is **private**, so an unauthenticated `git clone` on the VM fails. Copy the three files it needs instead:
```bash
# from your workstation, inside the repo
scp deploy/oracle/setup.sh deploy/oracle/docker-compose.yml deploy/oracle/Caddyfile ubuntu@<ip>:~
ssh ubuntu@<ip> 'bash ~/setup.sh'
ssh ubuntu@<ip>                  # re-login picks up the docker group
```

## 4. Deploy
```bash
mv ~/docker-compose.yml ~/Caddyfile ~/pdf-template-service/
cd ~/pdf-template-service
echo "JWT_KEY=$(openssl rand -base64 48)" > .env            # portal token signing key, keep private
sed -i 's/pdf.example.com/<your-dns-name>/' Caddyfile      # or ":80" for IP-only, no TLS
docker compose pull && docker compose up -d
docker compose logs -f pdf-engine       # wait for "Now listening"
```
Check: `https://<your-dns-name>/docs` (public API reference) and `/` (portal). The published image is multi-arch, so
`docker compose pull` picks the arm64 layer automatically.

## 5. Update to a new release

**Back up first — this is not optional.** The first start of a release that ships a schema migration rewrites
`poc.db` in place (e.g. every template gets re-keyed to a UUID) and cannot be undone. An older image will not
understand the migrated schema, so the only way back is restoring the copy you take now:
```bash
cd ~/pdf-template-service
docker compose stop pdf-engine
cp data/poc.db data/poc.db.pre-upgrade-$(date +%F)
docker compose pull && docker compose up -d
curl -fsS http://localhost:8080/health   # confirm status "healthy" and the schema_version you expect
```
Roll back by stopping the stack, copying `data/poc.db.pre-upgrade-<date>` back over `data/poc.db`, and starting
the previous image tag — pin a tag in `docker-compose.yml` (`:v0.2.0`) for controlled upgrades either way.

## 6. Data and backups
- SQLite file and rendered outputs live in `~/pdf-template-service/data` (mounted at `/data`).
- Backup nightly with cron: `0 3 * * * sqlite3 ~/pdf-template-service/data/poc.db ".backup ~/backups/poc-$(date +\%F).db"` (install `sqlite3`), and copy `~/backups` to Oracle Object Storage (10 GB free) with the OCI CLI.
- Restore: stop the stack, replace `data/poc.db`, start.

## 7. Keep it free
- Stay on A1.Flex within 4 OCPU / 24 GB total and ≤ 200 GB block storage.
- Oracle reclaims **idle** Always Free instances (very low CPU/network for 7 days). A weekly render or the health check traffic keeps it active; if reclaimed, recreate from this runbook — data is on the boot volume only if you keep the volume, so take backups.
- Do not upgrade the account to paid unless you want to; an upgraded account keeps Always Free resources free.

## 8. Operations
| Task | Command |
|---|---|
| Logs | `docker compose logs -f pdf-engine` |
| Restart | `docker compose restart pdf-engine` |
| Disk usage | `du -sh data` ; prune outputs older than 30 days: `find data -name '*.pdf' -mtime +30 -delete` |
| Renew TLS | automatic (Caddy) |
| Rotate limits | edit `RateLimiting__*` in compose, `docker compose up -d` |

## Security notes
- Only 22/80/443 are open; the API is reachable only through Caddy.
- SSH key auth only; consider `fail2ban` and unattended security upgrades (`sudo apt install unattended-upgrades`).
- Set `Docs__ExposeInternal=false` (already) so management endpoints are not in the public reference.
- Portal login protects every endpoint; the seeded owner must change the temporary password at first sign-in. Until spec 01 adds per-application API keys, render calls also use a portal token. Optionally restrict the portal further in the Caddyfile to your IP:
  ```
  @portal not path /api/v1/render /api/v1/validate /docs /openapi/*
  handle @portal { @notme not remote_ip <your-ip>; respond @notme 403 }
  ```
