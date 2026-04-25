# Zilean Transformation: Implementation Plan

**Based on**: Architecture Audit (01-architecture-audit.md)
**Goal**: Transform Zilean into Postgres-first, low-RAM, incrementally improving search service with strong audit trails

---

## Wave 0: Foundation (No dependencies)

These tasks can start immediately and in parallel:

### Epic 1: Database Schema Extensions
- New tables: IngestionCheckpoints, IngestionQueue, QueryAudit, FileAuditLog
- New columns on Torrents: LastRefreshedAt, MissCount, RefreshPending
- Migrations for all new schema

### Epic 2: Entity Models & Configuration
- New entity classes for all new tables
- Configuration extensions for new settings
- DbContext updates

### Epic 3: Diagnostic Endpoint Scaffolding
- Health check framework
- Diagnostic endpoint base structure

---

## Wave 1: Postgres-First Search (depends on Wave 0)

### Epic 4: Remove Lucene.NET
- Remove Lucene.NET package references
- Delete ImdbLuceneMatchingService
- Remove Lucene-related configs
- Clean up Lucene imports/using statements

### Epic 5: PostgreSQL IMDb Matching
- New ImdbPostgresMatchingService using trigram matching
- SQL function for batch IMDb matching
- Integration with StoreTorrentInfo pipeline

### Epic 6: Aggressive Persistence
- PostgreSQL synchronous commit config during ingestion
- Connection pooling optimization
- Bulk insert tuning

---

## Wave 2: Low-RAM Ingestion (depends on Wave 1)

### Epic 7: Stream-Based Ingestion
- Replace bulk-load-all with streaming batches
- Memory-bounded batch processing
- Progress logging

### Epic 8: Checkpoint System
- IngestionCheckpoint entity + service
- Save/restore checkpoint logic
- Resume from last checkpoint on restart

### Epic 9: Ingestion Queue
- IngestionQueue entity + service
- Queue management (enqueue, dequeue, mark processed)
- Queue status tracking

---

## Wave 3: Incremental Improvement (depends on Wave 2)

### Epic 10: Refresh-on-Miss
- Miss tracking service
- Background refresh job
- Auto-refresh stale entries

---

## Wave 4: Audit Trails (depends on Wave 0, parallel with Wave 3)

### Epic 11: Query Audit
- QueryAudit entity + service
- Search endpoint instrumentation
- Audit query endpoint

### Epic 12: File Audit Log
- FileAuditLog entity + service
- File operation instrumentation
- Audit log query endpoint

---

## Wave 5: Diagnostic Endpoints (depends on Wave 4)

### Epic 13: Health & Diagnostics
- Full /health endpoint (DB, ingestion, indexes)
- /diagnostics/freshness
- /diagnostics/queue
- /diagnostics/misses
- /diagnostics/stats

---

## Wave 6: Polish (depends on all above)

### Epic 14: Anime-Specific Handling
- Anime category detection
- Complete series boost
- Subbed/dubbed preference

### Epic 15: Query Caching
- In-memory cache with TTL
- Cache invalidation on ingestion
- Cache hit/miss metrics

---

## Dependency Graph

```
Wave 0 (Foundation)
├── Epic 1: DB Schema Extensions
├── Epic 2: Entity Models & Config
└── Epic 3: Diagnostic Scaffolding
        │
        ▼
Wave 1 (Postgres-First Search)
├── Epic 4: Remove Lucene.NET
├── Epic 5: PostgreSQL IMDb Matching
└── Epic 6: Aggressive Persistence
        │
        ▼
Wave 2 (Low-RAM Ingestion)
├── Epic 7: Stream-Based Ingestion
├── Epic 8: Checkpoint System
└── Epic 9: Ingestion Queue
        │
        ▼
Wave 3 (Incremental) ──── Wave 4 (Audit) ──── Wave 5 (Diagnostics)
├── Epic 10               ├── Epic 11          └── Epic 13
│                         └── Epic 12
│
▼
Wave 6 (Polish)
├── Epic 14: Anime Handling
└── Epic 15: Query Caching
```

## Parallelization Strategy

- **Wave 0**: All 3 epics in parallel (no dependencies)
- **Wave 1**: All 3 epics in parallel (all depend on Wave 0)
- **Wave 2**: All 3 epics in parallel (all depend on Wave 1)
- **Wave 3 + Wave 4**: Can run in parallel (different concerns)
- **Wave 5**: Depends on Wave 4 (needs audit tables)
- **Wave 6**: Depends on all above
