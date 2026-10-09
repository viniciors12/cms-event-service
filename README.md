# CMS Event Service

A .NET 9 service that ingests CMS webhook events (publish / unPublish / delete), keeps a local copy of the
entities in a relational database, and exposes that copy through a secured REST API.

```
 CMS ──POST /cms/events (batch, Basic auth: Cms account)──▶ validate ▶ apply version rules ▶ SQLite
                                                                                               │
 Users / Admins ◀──GET /entities (Basic auth: User or Admin account)── read-only context ◀─────┘
```

- **Stack:** ASP.NET Core controllers, EF Core 9, SQLite, xUnit.
- **Platforms:** Windows, macOS and Linux. SQLite is embedded, so there is nothing else to install.
- **API specification:** [docs/openapi.json](docs/openapi.json). Swagger UI is served at `/index.html` in Development.

## Quick start

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
git clone https://github.com/viniciors12/cms-event-service.git
cd cms-event-service

dotnet test                                  # unit + integration tests
dotnet run --project src/CmsEventService.Api # http://localhost:5184 (Development)
```

Then open **http://localhost:5184/index.html** (Swagger UI), press **Authorize** and use one of the accounts below.

The database file `cms.db` is created on first start in the directory the app runs from. To start clean, stop the
app and delete `cms.db*`.

**HTTPS.** Basic authentication sends credentials in every request, so use HTTPS anywhere but a local machine.
For local HTTPS run `dotnet dev-certs https --trust` once, then
`dotnet run --project src/CmsEventService.Api --launch-profile https` (https://localhost:7024).

## Accounts

`appsettings.Development.json` ships **development-only** accounts so the project runs out of the box:

| Account | Username | Password | Role |
|---|---|---|---|
| CMS (the organization sending events) | `cms-ingest-svc` | `343efcc7-4158-460c-b78b-be9b228b4bf5` | `Cms` |
| Admin | `admin@cms.local` | `73987aa8-bf41-42fa-843b-6c6812163158` | `Admin` |
| Regular user | `reader@cms.local` | `a729e858-e8e9-47dd-bcb9-49d13b618e33` | `User` |

The CMS account follows the assessment rules (username of 10-20 characters, password a GUID) and is a different
account from the API consumers. The app validates this at startup and **refuses to start** if the `Auth` section is
missing or invalid, so it can never run unprotected. `appsettings.json` deliberately ships empty credentials.

Outside Development, set your own through configuration or environment variables, for example:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export Auth__Cms__Username="my-cms-account"
export Auth__Cms__Password="$(uuidgen)"
export Auth__Users__0__Username="admin@example.com"
export Auth__Users__0__Password="<secret>"
export Auth__Users__0__Role="Admin"
```

## Try it

With the app running (HTTP profile):

```bash
# 1. As the CMS: send a batch of events
curl -X POST http://localhost:5184/cms/events \
  -u "cms-ingest-svc:343efcc7-4158-460c-b78b-be9b228b4bf5" \
  -H "Content-Type: application/json" \
  -d '[
    { "type": "publish",   "id": "article-1", "version": 1, "payload": { "title": "Travel guide" }, "timestamp": "2024-01-01T10:00:00Z" },
    { "type": "unPublish", "id": "article-2", "version": 4, "payload": { "title": "Draft" },        "timestamp": "2024-01-01T11:00:00Z" },
    { "type": "bogus",     "id": "article-3", "timestamp": "2024-01-01T12:00:00Z" }
  ]'

# 2. As a regular user: only article-1 is visible
curl -u "reader@cms.local:a729e858-e8e9-47dd-bcb9-49d13b618e33" http://localhost:5184/entities

# 3. As an admin: article-2 (unpublished) is visible too
curl -u "admin@cms.local:73987aa8-bf41-42fa-843b-6c6812163158" http://localhost:5184/entities

# 4. As an admin: hide article-1 from regular users without touching CMS data
curl -X POST -u "admin@cms.local:73987aa8-bf41-42fa-843b-6c6812163158" http://localhost:5184/entities/article-1/disable
```

The response to step 1 lists the outcome of **every** event (`Applied`, `Ignored`, `Rejected`, `Failed`); the
`bogus` event is rejected without affecting the others. On Windows PowerShell, use Swagger UI or `curl.exe`
with escaped quotes instead of the multi-line commands above.

## API

| Method and path | Who | Description |
|---|---|---|
| `POST /cms/events` | `Cms` | Ingest a batch (up to 1000 events, 10 MB). Always 200 with a per-event result; 400 only if the request itself is unusable. |
| `GET /entities?page=1&pageSize=20` | `User`, `Admin` | Paged list ordered by id (`pageSize` 1-100). |
| `GET /entities/{id}` | `User`, `Admin` | One entity. |
| `POST /entities/{id}/disable` | `Admin` | Local override: hide the entity from regular users. |
| `POST /entities/{id}/enable` | `Admin` | Revert the override. |

- **Visibility.** Regular users see entities that are published and not disabled. Admins see everything, from the
  same endpoints. A hidden entity is a **404** for regular users, so its existence is not revealed.
- **Read-only for everyone.** No role can edit entity data. The admin override only sets a flag in this service; it
  never changes what the CMS sent, and later CMS events do not undo it. A CMS `delete` does remove it with the rest
  of the entity, so an entity re-created after a delete starts enabled.
- **Errors.** 400, 401, 403, 404 and 500 are returned as RFC 9457 problem details. Unhandled errors outside
  Development are a generic 500 that does not expose internals.
- Every endpoint requires authentication (a fallback policy), including any added later. A wrong role is a 403.

## How events are processed

An event is `{ type, id, version, payload, timestamp }`. `type` is case-insensitive; `add` and `update` are treated as
`publish`. `delete` needs only `id` and `timestamp`.

| Event | Effect |
|---|---|
| `publish` | Creates or updates the entity at that version and makes it visible. |
| `unPublish` | Stores the entity at that version (it carries the payload) and hides it. Data is kept. |
| `delete` | Hard-deletes the entity and leaves a tombstone (see below). |

Versions only move forward, so duplicates and out-of-order delivery are safe:

- **Obsolete events are ignored.** A `publish`/`unPublish` is ignored when its version is lower than the stored one,
  or equal with an older timestamp. The same event sent twice is therefore harmless.
- **The unPublish corner case.** A version can be edited in the CMS and unpublished without ever being published, so
  the service never saw it. Because `unPublish` carries the payload, it is stored as is: stored v1 + `unPublish` v2
  results in v2, hidden. It does **not** fall back to v1. The same happens when the entity does not exist yet.
- **Re-publishing.** A `publish` of the same version with a newer timestamp makes an unpublished entity visible again.
- **Version wins over timestamp.** An `unPublish` of a version older than the stored one is ignored even if its
  timestamp is newer: the CMS always unpublishes its current version, so such an event cannot be the latest state.
- **Delete and tombstones.** Deleting removes all entity data, but a small tombstone (id + delete timestamp) is kept so
  that a late `publish` older than the delete cannot bring the entity back. A `publish` newer than the delete is a
  genuine re-creation and clears the tombstone. A `delete` older than the stored state is ignored.

**Validation** (per event): known type; `id` of 1-100 characters from `A-Z a-z 0-9 . _ : -`; strict ISO-8601
`timestamp` (UTC when it has no offset) no more than 5 minutes ahead of the server clock; integer `version` >= 1 and a
JSON-object `payload` of at most 256 K characters for everything except `delete`. Ids are trimmed. An invalid event is
rejected on its own and does not fail the batch. Future timestamps are rejected because ordering relies on them: a
single event dated years ahead would make every later event for that entity look stale and freeze it for good.

## Design decisions and trade-offs

**Synchronous processing.** Events are applied inside the request instead of being queued. The CMS gets the outcome of
every event immediately and can retry exactly what failed; ordering is guaranteed without extra machinery; and the cost
is small and bounded (a 1000-event batch is about half a second, see below). A queue would return "accepted" and move
failures out of band, needing a status endpoint, deduplication and ordering logic, plus infrastructure. If volume
grows, the natural step is a `Channel<T>` with a `BackgroundService`, or a broker, accepting eventual consistency.

**One transaction per batch, isolated per event.** The whole batch commits once, but each event is saved inside its
own EF Core savepoint, so a database error on one event is rolled back alone while the rest still commit. If the final
commit fails, the request fails with a 500 and nothing is applied; retrying is safe because processing is idempotent.

**Concurrent batches.** Applying an event reads the entity and then writes it. SQLite serializes writers, so two
batches never interleave here. For a server database, `LatestVersion` and `LastEventTimestamp` are optimistic
concurrency tokens: every applied event moves one of them forward, so a batch that loaded an older state gets that
event reported as `Failed` (and retried by the CMS) instead of overwriting a newer event. The admin override is a single
`UPDATE` of its own column, so it never conflicts with ingestion and is never lost to it.

**Separate read and write contexts.** `WriteDbContext` handles ingestion and the admin override and creates the schema.
`ReadDbContext` serves the API: it does not track entities and refuses to save. Setting `ConnectionStrings:ReadOnly`
points it at its own connection (the default opens SQLite with `Mode=ReadOnly`) or, in another database, a replica.

**Read queries.** No tracking, stable ordering by primary key, bounded pages, and a composite index on
`(IsPublished, IsDisabledByAdmin, Id)` that serves the count of what users can see. On 50,000 rows with 1 KB payloads
that count went from 107 ms to 4 ms. Paging uses offset, which is simple but gets slower for very deep pages (about
67 ms at offset 20,000); keyset pagination would be the next step.

**Measured ingestion cost** (1000 events on a file database, warm):

| | Creating | Updating |
|---|---|---|
| One commit per event, all entities tracked | 4.4-5.0 s | 1.9-2.8 s |
| One transaction per batch, tracker cleared after each save | 0.4-0.5 s | 0.5 s |

**Tombstones** cost one small row per deleted entity and are never purged. In production they could be removed after
the longest delay with which the CMS can still resend an old event.

**Other choices.** SQLite keeps setup to zero and is portable; the schema is created with `EnsureCreated`, so a model
change means deleting `cms.db` (a real deployment would use migrations). Credentials are compared in constant time and
the Basic handler never logs credentials or usernames. The CMS and API consumers are different accounts with different
roles, so the CMS cannot read data and users cannot inject events.

## Observability

Structured logs for every event with its index, type, entity id, version, outcome and reason: processed events and
ignored ones at Information, rejected ones at Warning, failures at Error (with the exception). Each batch also logs its
totals. Failed authentication is logged as a warning without the credentials. Console scopes are
enabled so logs can be correlated by trace id, and EF SQL logging is kept at Warning.

## Project layout

```
src/CmsEventService.Api
  Controllers/    CmsEventsController (ingestion), EntitiesController (read API + admin override)
  Application/    EventValidator, CmsEventProcessor, EntityAdminService, store/reader interfaces
  Domain/         CmsEntity, DeletedEntity: the version and visibility rules, with no infrastructure
  Infrastructure/ EF Core contexts, ICmsEventStore and IEntityReader implementations
  Auth/           Basic authentication handler, credential validation, roles and policies
  Contracts/      API response types
  OpenApi/        OpenAPI document description
tests/CmsEventService.Tests
  Unit/           validator, domain rules, credential and options validation
  Integration/    event processing on SQLite, Basic auth, roles, read API, paging, read/write split, concurrency
docs/openapi.json
```

## Tests

```bash
dotnet test
```

Unit tests need no database. Integration tests run the real application in memory (with `WebApplicationFactory`)
against SQLite, and cover the event rules, valid and invalid Basic credentials, role checks, visibility, the admin
override and paging. One test keeps `docs/openapi.json` in step with the API; after changing an endpoint, regenerate it
with `UPDATE_OPENAPI=1 dotnet test --filter OpenApiDocumentTests` (on Windows PowerShell:
`$env:UPDATE_OPENAPI=1; dotnet test --filter OpenApiDocumentTests`).

## Known limitations and next steps

- Tombstones are never purged and the schema has no migrations (see above).
- Offset pagination degrades for very deep pages; use keyset pagination for large datasets.
- Accounts live in configuration. A real system would use hashed credentials or an identity provider, HTTPS
  enforcement (HSTS) and rate limiting on failed logins.
- Only the latest version of each entity is stored; there is no event or version history.
- SQLite allows one writer at a time. Concurrent batches queue behind each other (see "Concurrent batches"), which is fine at this scale; a
  server database would be the next step.
