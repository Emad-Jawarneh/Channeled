# 11 — Diagrams (ERD, DFD, UML)

Mermaid diagrams of the **as-built** system. Render in any Mermaid-capable viewer (GitHub, VS Code, docs site). Source of truth for relationships remains EF model + migrations (`ApplicationDbContext`).

---

## 1. Entity-relationship diagrams (ERD)

### 1.1 Core domain (teams → channels → items)

Logical ERD. Solid lines = enforced FK; dashed = logical reference without FK (or SET NULL / plain Guid).

```mermaid
erDiagram
  AspNetUsers ||--o{ team_members : "membership"
  AspNetUsers ||--o{ channel_members : "grant"
  AspNetUsers ||--o{ sessions : "has"
  AspNetUsers ||--o{ notifications : "inbox"
  AspNetUsers ||--o{ file_stars : "stars"
  AspNetUsers ||--o{ team_join_requests : "requests"
  AspNetUsers ||--o{ upload_sessions : "uploads"

  teams ||--|{ team_members : "has"
  teams ||--|{ channels : "contains"
  teams ||--o{ team_join_requests : "pending"
  teams ||--o{ team_invitations : "legacy"
  teams ||--o{ file_share_links : "scoped"

  channels ||--o{ channel_members : "restricted"
  channels ||--o{ channel_join_requests : "pending"
  channels ||--o{ channel_messages : "text"
  channels ||--o{ folders : "file scope"
  channels ||--o{ files : "file scope"
  channels ||--o{ upload_sessions : "target"

  AspNetUsers ||--o{ folders : "personal OwnerUserId"
  AspNetUsers ||--o{ files : "personal OwnerUserId"

  folders ||--o{ folders : "ParentId"
  folders ||--o{ files : "FolderId"
  trash_batches ||--o{ folders : "DeletedBatchId"
  trash_batches ||--o{ files : "DeletedBatchId"

  files ||--o{ file_stars : "starred"
  files ||--o{ message_file_refs : "attached"
  upload_sessions ||--|{ upload_parts : "parts"

  channel_messages ||--o{ channel_messages : "ReplyTo SET NULL"
  channel_messages ||--o{ message_mentions : "mentions"
  channel_messages ||--o{ message_file_refs : "refs"

  AspNetUsers {
    uuid Id PK
    string Email
    string UsernameSlug
    bool IsPlatformAdmin
    string Status
    string DeletePhase
    long PersonalQuotaBytes
    long PersonalUsageBytes
  }

  teams {
    uuid Id PK
    string Name
    string Slug UK
    long QuotaBytes
    long UsageBytes
    int JoinMode
    timestamptz ArchivedAt
  }

  team_members {
    uuid TeamId PK_FK
    uuid UserId PK_FK
    int Role
    timestamptz JoinedAt
  }

  channels {
    uuid Id PK
    uuid TeamId FK
    string Name
    int Kind
    int Visibility
    timestamptz ArchivedAt
  }

  channel_members {
    uuid ChannelId PK_FK
    uuid UserId PK_FK
    bool ReadOnly
  }

  folders {
    uuid Id PK
    uuid ChannelId "xor OwnerUserId"
    uuid OwnerUserId
    uuid ParentId FK
    uuid DeletedBatchId FK
    timestamptz DeletedAt
  }

  files {
    uuid Id PK
    uuid ChannelId "xor OwnerUserId"
    uuid OwnerUserId
    uuid FolderId FK
    uuid UploadedBy
    string StorageKey
    string Sha256
    uuid DeletedBatchId FK
    timestamptz DeletedAt
  }

  trash_batches {
    uuid Id PK
    timestamptz DeletedAt
    string Scope
    string State
    int ItemCount
  }
```

### 1.2 Chat, notifications, shares, jobs

```mermaid
erDiagram
  channels ||--o{ channel_messages : "hosts"
  channel_messages ||--o{ message_mentions : "tags"
  channel_messages ||--o{ message_file_refs : "links"
  files ||--o{ message_file_refs : "file"
  AspNetUsers ||--o{ notifications : "UserId inbox"
  teams ||--o{ notifications : "TeamId logical"
  channels ||--o{ notifications : "ChannelId logical"
  teams ||--o{ file_share_links : "TeamId"
  files ||--o{ file_share_links : "FileId SET NULL composite"
  AspNetUsers ||--o{ sessions : "UserId"
  AspNetUsers ||--o{ password_reset_tokens : "UserId"
  AspNetUsers ||--o{ pending_email_changes : "UserId"

  channel_messages {
    uuid Id PK
    uuid ChannelId FK
    uuid AuthorId
    string Body
    uuid ReplyToMessageId FK
    timestamptz DeletedAt
  }

  message_mentions {
    uuid MessageId PK_FK
    uuid UserId PK
  }

  notifications {
    uuid Id PK
    uuid UserId
    int Type
    uuid TeamId
    uuid ChannelId
    uuid MessageId
    uuid ActorId
  }

  file_share_links {
    uuid Id PK
    uuid TeamId
    uuid FileId "nullable SET NULL"
    uuid ChannelId
    string TokenHash
    int DownloadCount
    timestamptz RevokedAt
  }

  jobs {
    uuid Id PK
    string Type
    string State
    string DetailsJson
    int Attempts
    timestamptz AvailableAt
  }

  audit_events {
    uuid Id PK
    uuid ActorId "nullable no FK"
    uuid TeamId "nullable no FK"
    int EventType
    string TargetType
    string TargetId
    string DetailsJson
  }

  sessions {
    uuid Id PK
    uuid UserId FK
    timestamptz RevokedAt
  }
```

### 1.3 Cardinality & delete rules (summary)

| Parent → child | On delete | Notes |
|----------------|-----------|-------|
| Team → members, channels, join requests | CASCADE | Team delete removes graph |
| Channel → messages, channel_members | CASCADE | |
| Message → mentions, file_refs | CASCADE | |
| Message → ReplyTo | SET NULL | |
| Folder → Parent | RESTRICT | No orphan tree edges |
| File/Folder → trash_batches | RESTRICT | Clear FK before batch delete |
| Share link → file | SET NULL (file_id) | Composite FK via raw SQL |
| `audit_events.TeamId` / `ActorId` | *no FK* | Survives team/user delete |
| `files.UploadedBy` / `messages.AuthorId` | *no FK* | Chips show “Deleted user” |

Scope check: `((ChannelId IS NOT NULL AND OwnerUserId IS NULL) OR (ChannelId IS NULL AND OwnerUserId IS NOT NULL))`.

---

## 2. Data-flow diagrams (DFD)

### 2.1 Context diagram (level 0)

```mermaid
flowchart TB
  User((User / browser))
  Admin((Platform admin))
  Anon((Anonymous claimer))
  Op((Operator CLI))
  SMTP[(SMTP server)]
  Store[(Object store)]
  PG[(PostgreSQL)]

  subgraph Channeled["Channeled system"]
    API[Channeled.Api + SPA + jobs]
  end

  User -->|HTTPS cookie CSRF REST SignalR| API
  Admin -->|Admin REST| API
  Anon -->|Public share claim/ticket| API
  Op -->|CLI seed backup reconcile delete-user| API
  API -->|SQL| PG
  API -->|Get/Put/Delete objects multipart| Store
  API -.->|email_send jobs| SMTP
```

**External entities:** User, Platform admin, Anonymous share claimer, Operator.  
**Data stores (inside system boundary for L0):** PostgreSQL, object store. SMTP is external.

### 2.2 Level-1 process decomposition

```mermaid
flowchart LR
  U((User))
  A((Admin))
  N((Anon))
  O((Operator))

  P1[1 Auth and sessions]
  P2[2 Teams and membership]
  P3[3 Files and personal]
  P4[4 Chat and notifications]
  P5[5 Shares and downloads]
  P6[6 Admin and audit]
  P7[7 Background jobs]

  D1[(D1 Identity sessions audit)]
  D2[(D2 Teams channels ACL)]
  D3[(D3 Files folders trash)]
  D4[(D4 Messages notifications)]
  D5[(D5 Jobs backups)]
  DS[(DS Object store)]

  U --> P1 & P2 & P3 & P4 & P5
  A --> P6
  N --> P5
  O --> P6 & P7

  P1 <--> D1
  P2 <--> D2
  P2 --> D1
  P3 <--> D3
  P3 <--> D2
  P3 --> DS
  P3 --> D5
  P4 <--> D4
  P4 <--> D2
  P5 <--> D3
  P5 --> DS
  P5 <--> D2
  P6 <--> D1 & D2 & D3 & D5
  P7 <--> D5 & D3 & D1
  P7 --> DS
```

| # | Process | Primary stores |
|---|---------|----------------|
| 1 | Auth & sessions | Users, sessions, reset/email tokens, audit |
| 2 | Teams & membership | Teams, members, channels, join |
| 3 | Files & personal | Files, folders, trash, uploads, quotas |
| 4 | Chat & notifications | Messages, mentions, notifications |
| 5 | Shares & downloads | Share links, tickets, store streams |
| 6 | Admin & audit | Users, teams quotas, audit, backups |
| 7 | Background jobs | Jobs, trash purge, object_delete, mail |

### 2.3 Process 3 — upload (detailed DFD)

```mermaid
flowchart TB
  U((User))
  P31[3.1 Start upload]
  P32[3.2 Stream part]
  P33[3.3 Complete upload]
  P34[3.4 Authorize write]
  D2[(Channels ACL)]
  D3[(Files sessions)]
  D1[(Audit)]
  DS[(Object store)]

  U -->|start metadata| P31
  P31 --> P34
  P34 --> D2
  P31 -->|create session| D3
  P31 -->|create multipart| DS
  U -->|part bytes| P32
  P32 --> DS
  P32 --> D3
  U -->|complete + Idempotency-Key| P33
  P33 --> P34
  P33 -->|verify SHA finalize| DS
  P33 -->|insert file bump quota| D3
  P33 --> D1
```

### 2.4 Process 7 — trash purge & object delete

```mermaid
flowchart LR
  Cron[Hosted timer] --> P71[7.1 Select expired batches]
  P71 --> D3[(trash_batches files folders)]
  P71 --> P72[7.2 Purge batch rows]
  P72 --> D3
  P72 --> P73[7.3 Enqueue object_delete]
  P73 --> D5[(jobs)]
  P74[7.4 Claim job SKIP LOCKED] --> D5
  P74 --> P75[7.5 Delete keys in store]
  P75 --> DS[(Object store)]
```

### 2.5 Process 6 — admin user delete (CAS)

```mermaid
flowchart TB
  A((Admin)) --> P61[6.1 Preview sole-owned teams]
  P61 --> D2[(Teams members)]
  P61 --> D3[(Files counts)]
  A -->|password + resolutions| P62[6.2 Advance DeletePhase CAS]
  P62 --> D1[(Users sessions)]
  P62 -->|transfer or DeleteTeamInternal| D2
  P62 -->|discard personal| D3
  P62 --> DS[(Object store outbox)]
  P62 --> D4[(Notifications strip inbox)]
  Sweep[UserDelete sweep] --> P62
```

---

## 3. UML

### 3.1 Component diagram

```mermaid
flowchart TB
  subgraph Client
    SPA[React SPA]
  end

  subgraph ApiHost["Channeled.Api"]
    EP[Minimal API endpoints]
    Hub[SignalR ChannelHub]
    MW[Auth CSRF rate-limit]
    CLI[Operator CLI modes]
  end

  subgraph Application
    Ports[Service interfaces + DTOs + Options]
  end

  subgraph Domain
    Authz[Authorizer + roles + rules]
  end

  subgraph Infrastructure
    Svcs[Team File Chat Admin Personal …]
    EF[ApplicationDbContext / Identity]
    Store[IFileStore FS or S3]
    Jobs[Job processors + hosted services]
  end

  SPA --> MW --> EP
  SPA --> Hub
  CLI --> Svcs
  EP --> Ports
  Hub --> Ports
  Ports --> Svcs
  Svcs --> Authz
  Svcs --> EF
  Svcs --> Store
  Jobs --> EF
  Jobs --> Store
```

### 3.2 Package / layer diagram

```mermaid
flowchart TB
  Api["Channeled.Api"]
  App["Channeled.Application"]
  Inf["Channeled.Infrastructure"]
  Dom["Channeled.Domain"]

  Api --> App
  Api --> Inf
  Api --> Dom
  Inf --> App
  Inf --> Dom
  App --> Dom
```

### 3.3 Class diagram — authorization & teams (simplified)

```mermaid
classDiagram
  class Authorizer {
    +Authorize(AuthorizationContext) Decision
  }
  class AuthorizationContext {
    +Guid UserId
    +AuthAction Action
    +MemberRole? TeamRole
    +bool IsChannelMember
    +bool ChannelCanWrite
    +bool IsPersonalOwner
    +bool IsPlatformAdmin
  }
  class AuthAction {
    <<enumeration>>
    BrowseFiles
    UploadFile
    DeleteOwnFile
    DeleteAnyFile
    DeleteTeam
    PostChannelMessage
    AccessPersonal
    …
  }
  class MemberRole {
    <<enumeration>>
    Viewer
    Member
    Admin
    Owner
  }
  class ITeamService {
    <<interface>>
    CreateTeamAsync()
    DeleteTeamAsync()
    EnsureJoinLinkAsync()
    …
  }
  class TeamService {
    -ApplicationDbContext db
    -IAuthorizer authorizer
    -ChannelWriteGuard guard
    +DeleteTeamCoreAsync()
  }
  class ChannelWriteGuard {
    +EnsureSharedAsync()
    +LockOrderedAsync()
  }

  Authorizer ..> AuthorizationContext : reads
  Authorizer ..> AuthAction
  AuthorizationContext --> MemberRole
  TeamService ..|> ITeamService
  TeamService --> Authorizer : Authorize
  TeamService --> ChannelWriteGuard
```

### 3.4 Class diagram — files & trash (simplified)

```mermaid
classDiagram
  class IFileService {
    <<interface>>
    ListItemsAsync()
    SoftDeleteAsync()
    RestoreItemAsync()
    CompleteUploadAsync()
  }
  class FileService {
    -TrashCascade trash
    -ChannelWriteGuard guard
    -IFileStore store
  }
  class TrashCascade {
    +TrashFolderAsync()
    +TrashFileAsync()
    +HasTrashedAncestorAsync()
    +AllocateUniqueName()
  }
  class TrashBatchEntity {
    +Guid Id
    +DateTimeOffset DeletedAt
    +string State
    +int ItemCount
  }
  class FileEntity {
    +Guid Id
    +Guid? ChannelId
    +Guid? OwnerUserId
    +Guid? DeletedBatchId
    +string StorageKey
  }
  class FolderEntity {
    +Guid Id
    +Guid? ParentId
    +Guid? DeletedBatchId
  }
  class ITrashPurgeService {
    <<interface>>
    PurgeExpiredAsync()
    PurgeBatchAsync()
  }
  class ObjectDeleteOutbox {
    <<static>>
    +Enqueue()
  }

  FileService ..|> IFileService
  FileService --> TrashCascade
  FileEntity --> TrashBatchEntity : DeletedBatchId
  FolderEntity --> TrashBatchEntity : DeletedBatchId
  ITrashPurgeService ..> FileEntity
  ITrashPurgeService ..> ObjectDeleteOutbox
```

### 3.5 Sequence — authenticated download ticket

```mermaid
sequenceDiagram
  actor User
  participant SPA
  participant API as DownloadEndpoints
  participant Tix as DownloadTicketService
  participant Authz as Authorizer
  participant DB as PostgreSQL
  participant Store as IFileStore

  User->>SPA: Download selection
  SPA->>API: POST /downloads/tickets (CSRF)
  API->>Tix: Issue tickets
  Tix->>Authz: Browse/download ACL
  Tix->>DB: Load file liveness
  Tix-->>SPA: Ticket URLs / per-item errors
  SPA->>API: GET /downloads/{ticket}
  API->>Tix: Validate DP payload user+sid window
  Tix->>Authz: Re-check ACL
  Tix->>Store: Open stream Range
  Store-->>User: Bytes
```

### 3.6 Sequence — multipart upload complete

```mermaid
sequenceDiagram
  participant SPA
  participant API as Uploads API
  participant FS as FileService
  participant Authz as Authorizer
  participant Guard as ChannelWriteGuard
  participant DB as PostgreSQL
  participant Store as IFileStore

  SPA->>API: POST /uploads (start)
  API->>FS: StartUpload
  FS->>Authz: UploadFile
  FS->>DB: UploadSession
  FS->>Store: CreateMultipart
  SPA->>API: PUT parts
  API->>Store: UploadPart stream
  SPA->>API: POST complete + Idempotency-Key
  API->>FS: CompleteUpload
  FS->>Guard: EnsureShared
  FS->>Store: CompleteMultipart + SHA
  FS->>DB: Insert file bump UsageBytes audit txn
  FS-->>SPA: FileItemDto
```

### 3.7 Sequence — team delete

```mermaid
sequenceDiagram
  actor Owner
  participant API as Teams API
  participant TS as TeamService
  participant Guard as ChannelWriteGuard
  participant DB as PostgreSQL
  participant Outbox as object_delete jobs

  Owner->>API: POST /teams/{slug}/delete confirm+password
  API->>TS: DeleteTeamAsync
  TS->>TS: COUNT caps reject if over
  TS->>DB: BEGIN lock_timeout statement_timeout
  TS->>DB: FOR UPDATE team
  TS->>Guard: LockOrdered channels asc
  loop each channel
    TS->>DB: Discard files/messages
    TS->>Outbox: Enqueue keys
  end
  TS->>DB: Revoke shares clear join delete notifs
  TS->>DB: Audit TeamDelete TeamId=null
  TS->>DB: DELETE team COMMIT
```

### 3.8 State machine — admin user `DeletePhase`

```mermaid
stateDiagram-v2
  [*] --> none
  none --> disabled: CAS disable + revoke sessions
  disabled --> teams: CAS after transfer/delete resolutions
  teams --> personal: CAS after DiscardPersonal
  personal --> memberships: CAS after strip memberships
  memberships --> [*]: UserManager.DeleteAsync

  disabled --> none: Admin re-enable cancel
  teams --> none: Admin re-enable cancel

  personal --> personal: Re-enable rejected
  memberships --> memberships: Sweep / admin retry only

  note right of personal
    Point of no return
    Sweep finishes stuck late phases
  end note
```

### 3.9 Deployment diagram

```mermaid
flowchart TB
  subgraph Host["Single machine native"]
    Proxy[Caddy / nginx TLS]
    ApiProc[channeled-api process]
    Pg[(PostgreSQL 15+)]
    Disk[(Filesystem object store)]
    Minio[(Optional MinIO)]
  end
  Browser[Browser] --> Proxy --> ApiProc
  ApiProc --> Pg
  ApiProc --> Disk
  ApiProc -.-> Minio
```

---

## 4. How to maintain these diagrams

| When you change… | Update |
|------------------|--------|
| New table / FK | §1 ERD + cardinality table |
| New external integration | §2.1 context DFD |
| New major use case | §2.2 process list + optional detailed DFD |
| New Application service / port | §3.1–3.4 UML |
| New multi-step lifecycle | Sequence + state (§3.5–3.8) |

Prefer editing this file over duplicating diagrams into feature docs; feature docs can link here (e.g. [07 — Deletion](07-deletion-lifecycles.md) ↔ §3.7–3.8).
