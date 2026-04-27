# Agent Instructions

This project uses **bd** (beads) for issue tracking. Run `bd onboard` to get started.

## Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work atomically
bd close <id>         # Complete work
bd sync               # Sync with git
```

## Non-Interactive Shell Commands

**ALWAYS use non-interactive flags** with file operations to avoid hanging on confirmation prompts.

Shell commands like `cp`, `mv`, and `rm` may be aliased to include `-i` (interactive) mode on some systems, causing the agent to hang indefinitely waiting for y/n input.

**Use these forms instead:**
```bash
# Force overwrite without prompting
cp -f source dest           # NOT: cp source dest
mv -f source dest           # NOT: mv source dest
rm -f file                  # NOT: rm file

# For recursive operations
rm -rf directory            # NOT: rm -r directory
cp -rf source dest          # NOT: cp -r source dest
```

**Other commands that may prompt:**
- `scp` - use `-o BatchMode=yes` for non-interactive
- `ssh` - use `-o BatchMode=yes` to fail instead of prompting
- `apt-get` - use `-y` flag
- `brew` - use `HOMEBREW_NO_AUTO_UPDATE=1` env var

<!-- BEGIN BEADS INTEGRATION -->
## Issue Tracking with bd (beads)

**IMPORTANT**: This project uses **bd (beads)** for ALL issue tracking. Do NOT use markdown TODOs, task lists, or other tracking methods.

### Why bd?

- Dependency-aware: Track blockers and relationships between issues
- Version-controlled: Built on Dolt with cell-level merge
- Agent-optimized: JSON output, ready work detection, discovered-from links
- Prevents duplicate tracking systems and confusion

### Quick Start

**Check for ready work:**

```bash
bd ready --json
```

**Create new issues:**

```bash
bd create "Issue title" --description="Detailed context" -t bug|feature|task -p 0-4 --json
bd create "Issue title" --description="What this issue is about" -p 1 --deps discovered-from:bd-123 --json
```

**Claim and update:**

```bash
bd update <id> --claim --json
bd update bd-42 --priority 1 --json
```

**Complete work:**

```bash
bd close bd-42 --reason "Completed" --json
```

### Issue Types

- `bug` - Something broken
- `feature` - New functionality
- `task` - Work item (tests, docs, refactoring)
- `epic` - Large feature with subtasks
- `chore` - Maintenance (dependencies, tooling)

### Priorities

- `0` - Critical (security, data loss, broken builds)
- `1` - High (major features, important bugs)
- `2` - Medium (default, nice-to-have)
- `3` - Low (polish, optimization)
- `4` - Backlog (future ideas)

### Workflow for AI Agents

1. **Check ready work**: `bd ready` shows unblocked issues
2. **Claim your task atomically**: `bd update <id> --claim`
3. **Work on it**: Implement, test, document
4. **Discover new work?** Create linked issue:
   - `bd create "Found bug" --description="Details about what was found" -p 1 --deps discovered-from:<parent-id>`
5. **Complete**: `bd close <id> --reason "Done"`

### Auto-Sync

bd automatically syncs with git:

- Exports to `.beads/issues.jsonl` after changes (5s debounce)
- Imports from JSONL when newer (e.g., after `git pull`)
- No manual export/import needed!

### Important Rules

- ✅ Use bd for ALL task tracking
- ✅ Always use `--json` flag for programmatic use
- ✅ Link discovered work with `discovered-from` dependencies
- ✅ Check `bd ready` before asking "what should I work on?"
- ❌ Do NOT create markdown TODO lists
- ❌ Do NOT use external issue trackers
- ❌ Do NOT duplicate tracking systems

For more details, see README.md and docs/QUICKSTART.md.

## Landing the Plane (Session Completion)

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd sync
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds

<!-- END BEADS INTEGRATION -->

## Diagnostic & Debug Endpoints (MANDATORY)

**CRITICAL**: This project exposes HTTP diagnostic endpoints on port **8181**. When debugging, investigating issues, or checking system state, you MUST use these endpoints instead of raw `docker exec psql` queries or log grepping. These endpoints are purpose-built for exactly the analysis you need.

### Base URL
```
http://localhost:8181
```

### Health Checks (no auth required)

| Endpoint | Method | Purpose | Example |
|----------|--------|---------|---------|
| `/healthchecks/ping` | GET | Liveness check - returns timestamp + "Pong!" | `curl http://localhost:8181/healthchecks/ping` |
| `/healthchecks/health` | GET | Full health report - PostgreSQL indexes, extensions, overall status. Returns 200 if healthy, 503 if not | `curl http://localhost:8181/healthchecks/health \| jq .` |

### Diagnostics (core debugging - use these FIRST)

| Endpoint | Method | Purpose | Response Fields | Example |
|----------|--------|---------|-----------------|---------|
| `/diagnostics/stats` | GET | **Database table stats** - row counts, sizes, total DB size, last ingestion time. REPLACES `SELECT COUNT(*) FROM ...` | `tables[].{name, rowCount, sizeBytes, sizeMb}`, `totalDatabaseSizeBytes`, `totalDatabaseSizeMb`, `lastIngestionTime` | `curl http://localhost:8181/diagnostics/stats \| jq '.tables[] \| {name, rowCount, sizeMb}'` |
| `/diagnostics/freshness` | GET | **Torrent freshness by category** - last updated timestamps, age in hours, torrent counts per category | `sources[].{name, lastUpdated, ageHours, torrentCount}`, `overallAgeHours`, `totalTorrents` | `curl http://localhost:8181/diagnostics/freshness \| jq .` |
| `/diagnostics/queue` | GET | **Ingestion queue status** - pending/processing/completed/failed counts, oldest 10 pending items with infohash and retry count | `pending`, `processing`, `completed`, `failed`, `oldestPending[].{id, infoHash, createdAt, retryCount}` | `curl http://localhost:8181/diagnostics/queue \| jq .` |
| `/diagnostics/misses` | GET | **Search miss tracking** - total misses across all torrents, top 20 most-missed titles with imdbId and category | `totalMisses`, `topMissed[].{title, missCount, imdbId, category}` | `curl http://localhost:8181/diagnostics/misses \| jq .` |
| `/diagnostics/cache` | GET | **Query cache statistics** - hit/miss rates, cache size, entries | Cache stats object | `curl http://localhost:8181/diagnostics/cache \| jq .` |

### Search & Content (verify data is queryable)

| Endpoint | Method | Auth | Purpose | Example |
|----------|--------|------|---------|---------|
| `/dmm/search` | POST | None | Search torrents by title - returns matching TorrentInfo[] | `curl -X POST http://localhost:8181/dmm/search -H 'Content-Type: application/json' -d '{"queryText":"Batman"}'` |
| `/dmm/filtered` | GET | None | Filtered search with season/episode/year/language/resolution/category/imdbId params | `curl 'http://localhost:8181/dmm/filtered?query=Batman&category=movie'` |
| `/dmm/on-demand-scrape` | GET | API Key | Trigger DMM sync job on-demand | `curl -H 'X-API-Key: test-api-key-123' http://localhost:8181/dmm/on-demand-scrape` |
| `/imdb/search` | GET | None | Search IMDB files by query/year/category | `curl 'http://localhost:8181/imdb/search?query=Batman&year=2022'` |

### Torrents (API key required)

| Endpoint | Method | Auth | Purpose | Example |
|----------|--------|------|---------|---------|
| `/torrents/all` | GET | API Key | Stream ALL torrents as JSON array (name, infoHash, size) | `curl -H 'X-API-Key: test-api-key-123' http://localhost:8181/torrents/all` |
| `/torrents/checkcached` | GET | API Key | Check if specific infohashes exist in DB | `curl -H 'X-API-Key: test-api-key-123' 'http://localhost:8181/torrents/checkcached?hashes=abc123,def456'` |

### Audit (query and file operation history)

| Endpoint | Method | Purpose | Example |
|----------|--------|---------|---------|
| `/audit/queries/recent` | GET | Recent search queries with result counts and duration | `curl 'http://localhost:8181/audit/queries/recent?limit=20'` |
| `/audit/queries/top` | GET | Most frequent search queries | `curl 'http://localhost:8181/audit/queries/top?limit=20'` |
| `/audit/queries/range` | GET | Queries within a date range | `curl 'http://localhost:8181/audit/queries/range?start=2026-04-25&end=2026-04-26'` |
| `/api/audit/files/recent` | GET | Recent file audit logs (scrape operations) | `curl 'http://localhost:8181/api/audit/files/recent?limit=20'` |
| `/api/audit/files/by-operation` | GET | Filter file audits by operation type | `curl 'http://localhost:8181/api/audit/files/by-operation?operation=dmm-download&limit=20'` |

### Debug Workflow (use in this order)

1. **Is the service running?** → `/healthchecks/ping`
2. **Is the database healthy?** → `/healthchecks/health`
3. **What's in the database?** → `/diagnostics/stats` (table row counts + sizes)
4. **Is data fresh?** → `/diagnostics/freshness` (age of torrents by category)
5. **Is ingestion working?** → `/diagnostics/queue` (pending/failed items)
6. **Are searches returning results?** → `/dmm/search` with a known title
7. **What are users searching for?** → `/audit/queries/recent`
8. **What's failing to match?** → `/diagnostics/misses`

### Anti-Pattern (NEVER do this)

```bash
# WRONG - bypassing the diagnostic endpoints you built
docker exec zilean-db psql -U postgres -d zilean -c "SELECT COUNT(*) FROM \"Torrents\";"
docker exec zilean-db psql -U postgres -d zilean -c "SELECT * FROM pg_stat_user_tables;"
docker logs zilean 2>&1 | grep -E "something" | tail -50

# RIGHT - use the endpoints
curl -s http://localhost:8181/diagnostics/stats | jq '.tables[] | {name, rowCount, sizeMb}'
curl -s http://localhost:8181/diagnostics/freshness | jq .
curl -s http://localhost:8181/diagnostics/queue | jq .
```

**Rule**: If a diagnostic endpoint exists that answers your question, USE IT. Only fall back to raw psql or log grepping if the endpoint does not cover your specific need. If you find yourself needing data the endpoints don't provide, that's a signal to ADD a new diagnostic endpoint.

## Log Analysis (MANDATORY)

**CRITICAL**: Never use `docker logs zilean` for log analysis. The container output is truncated and mixed with stack traces. Instead, use the structured log files on the host:

### Log File Location
```
/mnt/nvme/comet/zilean/data/logs/
```

Files are named `zilean-YYYYMMDD.log` with daily rotation:
```bash
# List all log files
ls -la /mnt/nvme/comet/zilean/data/logs/

# Find today's log
ls -t /mnt/nvme/comet/zilean/data/logs/ | head -1

# Search for specific patterns in today's log
TODAY=$(ls -t /mnt/nvme/comet/zilean/data/logs/ | head -1)
rg "pattern" /mnt/nvme/comet/zilean/data/logs/$TODAY

# Search across ALL log files (for historical analysis)
rg "pattern" /mnt/nvme/comet/zilean/data/logs/

# Get last N lines of current log
tail -100 /mnt/nvme/comet/zilean/data/logs/$(ls -t /mnt/nvme/comet/zilean/data/logs/ | head -1)
```

### Log Format
Each line follows the pattern: `[HH:MM:SS] | LEVEL | "Source" | Message`

Common sources to grep for:
- `ProwlarrSyncJob` — Prowlarr ingestion activity
- `ProwlarrBackfill` — Backfill-specific operations
- `IngestionQueue` — Queue processing
- `TorrentInfoService` — Torrent storage
- `Imdb` — IMDb matching

### Anti-Pattern (NEVER do this)
```bash
# WRONG - truncated, mixed with stack traces, limited history
docker logs zilean 2>&1 | grep "something" | tail -50

# RIGHT - full history, structured, grep-friendly
TODAY=$(ls -t /mnt/nvme/comet/zilean/data/logs/ | head -1)
rg "something" /mnt/nvme/comet/zilean/data/logs/$TODAY
```
