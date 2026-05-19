---
name: elsa-workflows
description: "Complete developer reference for the Elsa Workflows platform - MozartWorkflows (.NET 8 Elsa 2.12.0 backend + dashboard) and Ocelot API Gateway. Use when asked about architecture, endpoints, auth, activities, SQL, notifications, storage, rules engine, or any file in D:/Workflows."
user-invocable: true
allowed-tools:
  - Read
  - Grep
  - Glob
  - Bash(dotnet *)
  - Bash(ls *)
  - Bash(find *)
  - Edit
  - Write
---

# /elsa-workflows — Elsa Workflows Platform Reference

Arguments passed: `$ARGUMENTS`

This skill provides complete context for the **Elsa Workflows** platform — the combined name for:

| Project | Path | Port | Purpose |
|---------|------|------|---------|
| **MozartWorkflows** | `D:\Workflows\MozartWorkflows\` | 7001 (HTTPS) | Elsa 2.12.0 backend + dashboard |
| **Ocelot** | `D:\Workflows\Ocelot\` | 6001 (HTTPS) | API Gateway (routes → port 7001) |

---

## 1. MOZARTWORKFLOWS — PROJECT STRUCTURE

```
D:\Workflows\MozartWorkflows\
├── Controllers/           REST API endpoints (7 controllers)
├── Dtos/                  Data Transfer Objects (11 files)
├── Elsa/
│   ├── Activities/        34 custom Elsa activities
│   ├── Constants/         EmailSettings.cs, Roles.cs
│   └── NotificationHandlers/  4 notification handlers
├── Extensions/            JWT auth, Elsa DI, DB factory, policy provider
├── Handlers/              Webhook & event processing
├── Models/                Domain models (16 files)
├── Notifications/         Email/Push/SignalR notification system (11 files)
├── Pages/                 Login.cshtml, _Host.cshtml (53KB dashboard)
├── PhoneCall/             Twilio integration
├── Services/              22 service implementations + 15 interfaces
├── Storage/               Multi-cloud storage (S3, Azure, GCS, OmniDocs)
├── Program.cs             ASP.NET Core 8 startup & DI
├── SqlQueries.cs          Multi-DB SQL abstraction (4-dialect partial class)
├── SqlQueries.SqlServer.cs
├── SqlQueries.MySql.cs
├── SqlQueries.PostgreSql.cs
├── SqlQueries.Oracle.cs
├── appsettings.json
└── nlog.config
```

---

## 2. CONTROLLERS & ENDPOINTS

### AuthController (`/api/auth`) — No `[Authorize]`
```
POST /api/auth/login
  Body: { username, password }
  Returns: { token (JWT), username, userId, isAdmin, expiresAt }
  Note: JWT stored by client in localStorage as 'elsa_jwt'

POST /api/auth/forgot-password
  Body: { username }
  Returns: generic message (no user-enumeration leak)
  Resets password to "Password123" using CreateSaltKey(5) + SHA1 hash
```

### UserManagementController (`/api/user-mgmt`) — `[Authorize(AuthenticationSchemes = "Bearer")]`
```
GET  /api/user-mgmt/profile               → { username, userId, isAdmin }
POST /api/user-mgmt/change-password       → Body: { currentPassword, newPassword, confirmPassword }
GET  /api/user-mgmt/users                 → Array<DashboardUser>  [Admin only]
POST /api/user-mgmt/users                 → Body: { username, email, isAdmin }  [Admin only]
PUT  /api/user-mgmt/users/{id}/toggle-active  [Admin only]
PUT  /api/user-mgmt/users/{id}/toggle-admin   [Admin only]
POST /api/user-mgmt/users/{id}/reset-password [Admin only]
DELETE /api/user-mgmt/users/{id}              [Admin only]
```

### WorkflowChangeAuditController (`/api/workflow-change-audit`) — `[Authorize(AuthenticationSchemes = "Bearer")]`
```
GET    /api/workflow-change-audit?top=200
GET    /api/workflow-change-audit/paged?page&pageSize&workflowFilter&changeTypeFilter
GET    /api/workflow-change-audit/{definitionId}
DELETE /api/workflow-change-audit/selected  Body: { ids: long[] }  [Admin only]
```

### MeetingController (`/api/meeting`)
```
POST   /api/meeting/CreateMeeting
POST   /api/meeting/UpdateMeeting/{meetingId}
DELETE /api/meeting/{meetingId}
```

### Other Controllers
- `WebHookController` — Generic webhook reception
- `TestController` — Debug endpoint
- `DbTestController` — Database connectivity check

---

## 3. AUTHENTICATION & AUTHORIZATION

### Dual Authentication Scheme ("Mixed" Policy)
Registered in `Extensions/RegisterJwt.cs`:

```
/api/*        → Bearer (JWT)    — 401 on failure
/elsa-api/*   → Bearer (JWT)    — 401 on failure
All other     → Cookies         — redirect to /elsa-login on failure
```

### Login Flow (`Pages/Login.cshtml` + `Login.cshtml.cs`)
1. User POSTs credentials (JS fetch to `/api/auth/login`)
2. `AuthController` queries `ElsaDashboardUsers` table
3. `PasswordHasher.ValidatePassword(plain, salt, sha1hash)` — SHA1 algorithm
4. JWT issued with claims: `userId`, `ClaimTypes.NameIdentifier`, `ClaimTypes.Name`, `username`, `IsAdmin`, `isAdmin`
5. Client stores JWT in `localStorage['elsa_jwt']`

### Cookie Login (`Login.cshtml.cs`)
- Also supports form POST (for server-side rendering)
- Calls `SignInAsync("Cookies")` with 8-hour expiry
- Inserts row into `ElsaLoginAudit` table

### Logout Flow (`Program.cs` LogoutHandler)
1. POST/GET `/logout` → LogoutHandler
2. Updates `ElsaLoginAudit`: `IsLogout=1`, `LogoutTime=now`
3. `ISessionService.RevokeAllUserSessions(userId)`
4. Sets `MemoryCache["loggedout:{userId}"]` flag
5. `SignOutAsync("Cookies")`
6. `localStorage.clear()` + `sessionStorage.clear()` via `doLogout()` JS function

### Password Hashing (`Services/PasswordHasher.cs`)
```csharp
CreateSaltKey(int size)     // RNGCryptoServiceProvider → Base64 salt
CreatePasswordHash(string pwd, string salt)  // SHA1 hex — matches login JS
ValidatePassword(string plain, string salt, string hash)  // comparison
```
**Note**: There is also a PBKDF2/JSON method (`HashPassword`/`VerifyPassword`) for older tokens — NOT used for dashboard users.

---

## 4. DATABASE & SQL ABSTRACTION

### Multi-Provider Pattern
```csharp
// Detect provider from IDbConnection type
SqlQueries.ParameterPrefix(conn)     // "@" or "?"
SqlQueries.GetLoginQuery(conn)       // provider-specific SQL
SqlQueries.GetLoginAuditInsertQuery(conn)

// All SQL Server constants
SqlQueries.SqlServer.LoginUser
SqlQueries.SqlServer.InsertLoginAudit
SqlQueries.SqlServer.UpdateLogoutAudit
SqlQueries.SqlServer.GetUserById
SqlQueries.SqlServer.GetAllUsers
SqlQueries.SqlServer.CreateUser
SqlQueries.SqlServer.GetUserPasswordHash
SqlQueries.SqlServer.UpdateUserPassword
SqlQueries.SqlServer.UpdateUserPasswordByUsername
SqlQueries.SqlServer.SetUserActive
SqlQueries.SqlServer.SetUserAdmin
SqlQueries.SqlServer.DeleteUser
SqlQueries.SqlServer.GetUserRoleByPolicyName
SqlQueries.SqlServer.GetUsers
SqlQueries.SqlServer.GetConfigurationItem
SqlQueries.SqlServer.InsertPendingNotification
SqlQueries.SqlServer.GetPendingPushNotifications
SqlQueries.SqlServer.MarkNotificationSent
SqlQueries.SqlServer.EnsureIsAdminColumn
```

### Key Tables
| Table | Key Columns |
|-------|-------------|
| `ElsaDashboardUsers` | Id, UserId, Username, Email, PasswordHash, Salt, Active, IsAdmin, CreatedAt |
| `ElsaLoginAudit` | Id, UserId, EmailOrUsername, LoginTime, IpAddress, UserAgent, IsSuccessful, FailureReason, IsLogout, LogoutTime |
| `UserRole` | Name, ApplicationId, Active |
| `[User]` | Id, UserName, Email, UserRoleId |
| `Configuration` | ConfigurationName, ConfigurationValue |
| `PendingNotifications` | Id, UserId, Message, Subject, Mode, IsSent, CreatedAt |
| `WorkflowChangeAudit` | Id, DefinitionId, WorkflowName, Version, ChangeType, ChangedBy, ChangedByUserId, ChangedAt, ActivityCount, ChangeDetails, IpAddress |

### DbConnectionFactory (`Extensions/DbConnectionFactory.cs`)
- Reads `DatabaseProvider` from config: `"SqlServer"` | `"MySql"` | `"PostgreSql"` | `"Oracle"`
- Returns open `IDbConnection`

---

## 5. SERVICES REFERENCE

### IUserManagementService / UserManagementService
```csharp
Task<DashboardUser?> GetByIdAsync(int id)
Task<IEnumerable<DashboardUser>> GetAllAsync()
Task<DashboardUser> CreateAsync(string username, string email, bool isAdmin)
Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
Task ResetPasswordAsync(int id)                       // resets to "Password123"
Task<bool> ResetPasswordByUsernameAsync(string username)  // forgot-password path
Task SetActiveAsync(int id, bool active)
Task SetAdminAsync(int id, bool isAdmin)
Task DeleteAsync(int id)
Task EnsureSchemaAsync()                              // adds IsAdmin column if missing
```

### IDbService / DapperDbService
```csharp
Task<IEnumerable<T>> GetAllAsync<T>(string query, object? parameters = null)
Task<T?> GetAsync<T>(string query, object? parameters = null)
Task ExecuteAsync(string query, object? parameters = null)
Task<T?> ExecuteStoredProcedureAsync<T>(string storedProcedure, object? parameters)
```

### IRuleService / RuleServiceImpl
```csharp
Task<IEnumerable<RuleDto>> GetAllRulesAsync()
Task<RuleDto?> GetRuleByIdAsync(int id)
Task<string?> GetWorkflowNameAsync(int applicationId)
Task CreateRuleAsync(RuleDto rule)
Task UpdateRuleAsync(RuleDto rule)
Task DeleteRuleAsync(int id)
```

### IWorkflowChangeAuditService
```csharp
Task EnsureTableExistsAsync()
Task<IEnumerable<WorkflowChangeAudit>> GetRecentAsync(int top = 200)
Task<PagedResult<WorkflowChangeAudit>> GetPagedAsync(int page, int pageSize, string? workflowFilter, string? changeTypeFilter)
Task<IEnumerable<WorkflowChangeAudit>> GetByDefinitionAsync(string definitionId)
Task DeleteAsync(IEnumerable<long> ids)
```

### ConfigManager
```csharp
string GetConfigurationItem(string configurationName)  // reads from DB, cached 15min
```

---

## 6. ELSA 2.12.0 CUSTOM ACTIVITIES

All located in `D:\Workflows\MozartWorkflows\Elsa\Activities\`

### Notification & Communication
| Activity | Purpose |
|----------|---------|
| `SendEmail` | SMTP email with base64 attachments |
| `SendEmailOTP` | OTP via email (Gmail SMTP) |
| `SendOTP` | OTP via Gupshup SMS |
| `SendNotificationActivity` | Push/email orchestrated dispatch |
| `SendSignalRMessageActivity` | Real-time SignalR message to user |
| `TwilioMakeCall` | Outbound voice call via Twilio |

### File & Document Handling
| Activity | Purpose |
|----------|---------|
| `UploadDocument` / `UploadFileActivity` | Upload to cloud storage |
| `GetFile` / `GetUplodedDocument` | Retrieve from storage |
| `DownloadFileActivity` | Direct download |
| `DownloadAndZipFilesActivity` | Batch download + ZIP |
| `DeleteFile` | Remove from cloud |
| `SaveDocumentMetadata` | Persist file metadata to DB |

### Data & Reporting
| Activity | Purpose |
|----------|---------|
| `GenerateExcelFeedFile` | Generate single Excel |
| `GenerateMultipleExcelFeedFilesAndZip` | Batch Excel + ZIP |
| `GenerateGIUploadErrorExcel` | Error report spreadsheet |
| `ConvertDataset` | Data format conversion |
| `RetrieveEmails` | Email retrieval (IMAP/POP3) |
| `AzureOcrAndClassifyActivity` | OCR + document classification |

### Rules Engine
| Activity | Purpose |
|----------|---------|
| `RuleEvaluationActivity` | Evaluate rules, return scored results |
| `RuleExecutionActivity` | Execute rule actions (email/SMS/state) |

### Group Insurance (GI) Specialized
| Activity | Purpose |
|----------|---------|
| `BaseGIEndorsementRowwiseUpload` | Base class for GI uploads |
| `ProcessGroupInsuranceBulkUpload` | Large file GI member processing |
| `ProcessGIEndorsementRowwiseOffboardDelete` | Member offboarding |

### Database
| Activity | Purpose |
|----------|---------|
| `ExecuteMySqlCommand` | Execute MySQL DML |
| `ExecuteMySqlQuery` | Execute MySQL SELECT |

### Utility
| Activity | Purpose |
|----------|---------|
| `GenerateOTP` | Random N-digit OTP |
| `CreateTeamsMeeting` | Schedule Teams meeting via Graph API |
| `SetCacheData` / `GetCacheData` | Distributed cache operations |
| `FlushPendingNotificationsActivity` | Batch flush notification queue |
| `SendHttpFileRequest` | HTTP multipart file upload |

### Notification Handlers (`Elsa/NotificationHandlers/`)
- `ActivityNotificationHandler` — Logs activity execution
- `WorkflowDefinitionChangeHandler` — Persists definition changes to `WorkflowChangeAudit`
- `SequenceManager` — Activity sequence tracking
- `NLoggerManager` — Logging coordination

---

## 7. NOTIFICATIONS SYSTEM

### Architecture
```
INotificationOrchestrator (NotificationOrchestrator)
    ├── IEmailNotificationService (EmailNotificationService) → SMTP
    ├── IPushNotificationService (PushNotificationService) → SignalR/browser push
    └── INotificationRepository (NotificationRepository) → PendingNotifications table
```

### Flow
1. Activity calls `INotificationOrchestrator.SendAsync(userId, message, subject, mode)`
2. Orchestrator routes: `mode == "Email"` → SMTP; else → push
3. Push: saves to `PendingNotifications` table; `PendingNotificationFlusher` batches delivery
4. SignalR real-time: `ISignalRService.SendMessageAsync(userId, message)`

### IOnlineUserTracker
- `InMemoryUserTracker` singleton tracks connected SignalR connections per userId
- Hub: `NotificationHub` at `/notificationHub`

---

## 8. MULTI-CLOUD STORAGE

```
IStorageService (interface — Upload, Download, Delete, List)
    StorageServiceFactory (reads StorageProviderConfig from DB)
        ├── AwsS3StorageService
        ├── AzureBlobStorageService
        ├── GcsStorageService
        └── OmniDocsStorageService
```
Provider selected at runtime from `StorageProviderConfig` table.

---

## 9. RULES ENGINE

**Library**: `RulesEngine` v6.0.0

```
RuleDataService → loads rules from DB → builds RulesEngine per app/workflow
RuleEvaluationActivity → evaluates rules with flattened form data + global defaults
RuleExecutionActivity → dispatches actions based on evaluation results
```

**Custom Expression Types** (available in rule expressions):
- `WorkflowExecutionService` — workflow execution context
- `Utils` — utility functions
- `CacheService` — distributed cache

---

## 10. DASHBOARD UI (_Host.cshtml)

**File**: `D:\Workflows\MozartWorkflows\Pages\_Host.cshtml` (53KB)

### JS auth initialization
```javascript
var token = localStorage.getItem('elsa_jwt');
// Validates expiry, populates window.IS_ADMIN, window.USER_ID, etc.
```

### Key JS Functions
```javascript
getAuthHeaders(extra)         // → { Authorization: 'Bearer <token>', ...extra }
doLogout()                    // clears localStorage, POST /logout, redirect
openChangePassword()          // modal → POST /api/user-mgmt/change-password
openManageUsers() / loadUsers()  // panel → GET /api/user-mgmt/users
createUser()                  // → POST /api/user-mgmt/users
openChangeHistory() / refreshHistory()  // panel → GET /api/workflow-change-audit/paged
```

### Admin-Only UI Elements
- "Manage Users" button — only shown when `window.IS_ADMIN === true`
- "Change History" button — only shown when `window.IS_ADMIN === true`

### Elsa Studio Components
```html
<elsa-studio-root server-url="https://localhost:7001">
  <div slot="header">...</div>
  <elsa-studio-dashboard></elsa-studio-dashboard>
</elsa-studio-root>
```

---

## 11. PROGRAM.CS STARTUP ORDER

```
1. QuestPDF Community license
2. SignalR
3. NLog
4. Elsa 2.12.0 (RegisterElsa.cs) — multi-DB, custom activities
5. JWT + Cookie auth (RegisterJwt.cs)
6. 50+ scoped/singleton service registrations
7. Background hosted services: AuditWorker, WorkflowAuditWorker, EventSyncJob
├── Middleware pipeline:
    CORS → HTTPS redirect → Static files → Routing →
    Request logging → Authentication → RequestUserContext →
    Authorization → MapControllers → MapRazorPages →
    Elsa HTTP → /logout handler → SignalR hub
```

---

## 12. CONFIGURATION (appsettings.json)

```json
{
  "DatabaseProvider": "SqlServer",
  "ConnectionStrings": { "connectionString": "..." },
  "jwt": {
    "Key": "DhftOS5uphK3vmCJQrexST1RsyjZBjXWRgJMFPU4",
    "DashboardExpiry": 8,
    "Session": { "EnableTracking": true, "IdleTimeoutMinutes": 20 }
  },
  "Elsa": { "Server": { "BaseUrl": "https://localhost:7002/" } },
  "EmailSettings": { "Email", "Password", "Host", "Displayname", "Port" },
  "Twilio": { "AccountSid", "AuthToken", "FromNumber" },
  "Gupshup": { "Sms": {...}, "WhatsApp": {...}, "Email": {...} },
  "AzureAd": { "TenantId", "ClientId", "ClientSecret" },
  "AzureDocumentIntelligence": { "Endpoint", "ApiKey", "ModelId" },
  "AuditLogging": { "Mode": "Database" },
  "BatchConfig": { "BatchSize": 100, "MaxWaitMs": 20000 }
}
```

---

## PART 2: OCELOT API GATEWAY

**Path**: `D:\Workflows\Ocelot\`
**Port**: 6001 (HTTPS)
**Routes to**: `https://localhost:7001` (MozartWorkflows)

### Files
```
Ocelot/
├── Gateway/
│   ├── RequestLoggingMiddleware.cs   Logs all requests/responses with timing
│   └── LogRepository.cs             Persists to DB via sp_LogApiRequest
├── Program.cs                       Startup: Ocelot + Polly + JWT + logging
├── ocelot.json                      Route definitions (60+ routes)
├── oce.appsettings.json             Connection string, JWT key
└── OcelotAPI.csproj                 References MozartWorkflows.csproj
```

### Program.cs Key Points
- Listens on `GATEWAY_HTTPS_PORT` env var or default 6001
- Config: `oce.appsettings.json` + `ocelot.json`
- `AddOcelot(...).AddPolly()` for resilience
- Reuses `RegisterJwt` from MozartWorkflows (shared project reference)
- `RequestLoggingMiddleware` buffers request + response bodies for logging

### ocelot.json Route Pattern
```json
{
  "DownstreamPathTemplate": "/endpoint",
  "DownstreamHostAndPorts": [{ "Host": "localhost", "Port": 7001 }],
  "UpstreamPathTemplate": "/api/endpoint",
  "UpstreamHttpMethod": ["POST"],
  "QoSOptions": {
    "ExceptionsAllowedBeforeBreaking": 3,
    "DurationOfBreak": 1000,
    "TimeoutValue": 5000
  },
  "RetryPolicy": { "RetryCount": 3, "RetryBackOff": 1000 },
  "AuthenticationOptions": { "AuthenticationProviderKey": "Bearer" }
}
```

### Sample Routed Endpoints (60+ total)
```
POST /api/login                       → /login              (no auth)
POST /api/addUser                     → /addUser
POST /api/Upload-Documents            → /Upload-Documents
GET  /api/Download-Document/{docId}   → /Download-Document/{docId}
GET  /api/DocumentsListById/{caseId}  → /DocumentsListById/{caseId}
POST /api/FraudCaseRequest            → /FraudCaseRequest
POST /api/PerformCaseActions          → /PerformCaseActions
GET  /api/InvestigationList/{userId}  → /InvestigationList/{userId}
GET  /api/GetCaseData/{caseId}        → /GetCaseData/{caseId}
POST /api/PasswordResetSendOtp        → /PasswordResetSendOtp
POST /api/ValidateOTP                 → /ValidateOTP
POST /api/ResetPassword               → /ResetPassword
POST /api/UpdatePassword              → /UpdatePassword
POST /api/DeactivateUser              → /DeactivateUser
POST /api/ActivateUser                → /ActivateUser
GET  /api/GetUserDetails/{userId}     → /GetUserDetails/{userId}
```

### RequestLoggingMiddleware
- Enables request buffering
- Captures response via `MemoryStream` swap
- Logs: method, path, request body, response body, status, elapsed ms
- Persists via `LogRepository.LogRequestAsync()` → `sp_LogApiRequest` stored procedure

---

## 13. DEPENDENCIES

### MozartWorkflows.csproj Key Packages
```
Elsa.*                         2.12.0   (core + activities + persistence + studio)
Dapper                         2.1.66
Microsoft.Data.SqlClient       6.0.2
Oracle.ManagedDataAccess       23.8.0
AWSSDK.S3                      3.7.416.5
Azure.Storage.Blobs            12.18.0
Azure.AI.DocumentIntelligence  1.0.0
Google.Cloud.Storage.V1        4.13.0
ClosedXML                      0.104.2
QuestPDF                       2025.4.0
RulesEngine                    6.0.0
Microsoft.Graph                5.103.0
Twilio                         7.11.5
NLog.Web.AspNetCore            5.3.2
Microsoft.AspNetCore.Authentication.JwtBearer  8.0.13
Hangfire.SqlServer             1.8.5
```

### OcelotAPI.csproj Key Packages
```
Ocelot                    24.0.0
Ocelot.Provider.Polly     24.0.0
Swashbuckle.AspNetCore    6.6.2
ProjectReference: ..\MozartWorkflows\MozartWorkflows.csproj
```

---

## 14. EXTERNAL INTEGRATIONS

| Service | Purpose | Config Key |
|---------|---------|-----------|
| Microsoft Graph | Teams meetings | `AzureAd` section |
| Azure Document Intelligence | OCR + classification | `AzureDocumentIntelligence` |
| Gupshup | SMS + WhatsApp | `Gupshup` section |
| Twilio | Voice calls | `Twilio` section |
| SMTP (Gmail) | Email delivery | `EmailSettings` |
| AWS S3 | File storage | `StorageProviderConfig` table |
| Azure Blob Storage | File storage | `StorageProviderConfig` table |
| Google Cloud Storage | File storage | `StorageProviderConfig` table |
| OmniDocs | File storage | `StorageProviderConfig` table |
| SignalR | Real-time push | Hub at `/notificationHub` |

---

## 15. QUICK FILE REFERENCE

| What | File |
|------|------|
| Login handler (C#) | `Pages/Login.cshtml.cs` |
| Login UI + JS | `Pages/Login.cshtml` |
| Dashboard (full UI) | `Pages/_Host.cshtml` (53KB) |
| JWT + Cookie auth setup | `Extensions/RegisterJwt.cs` |
| Elsa DI registration | `Extensions/RegisterElsa.cs` |
| DB connection factory | `Extensions/DbConnectionFactory.cs` |
| Dynamic policy provider | `Extensions/DynamicApplicationPolicyProvider.cs` |
| Password hashing | `Services/PasswordHasher.cs` |
| User management | `Services/UserManagementService.cs` |
| SQL query constants | `SqlQueries.SqlServer.cs` (18 queries) |
| Notification routing | `Notifications/Services/NotificationOrchestrator.cs` |
| Storage factory | `Storage/Factory/StorageServiceFactory.cs` |
| Rules engine activities | `Elsa/Activities/RuleEvaluationActivity.cs` |
| Workflow change handler | `Elsa/NotificationHandlers/WorkflowDefinitionChangeHandler.cs` |
| Startup + DI | `Program.cs` |
| Ocelot routes | `D:\Workflows\Ocelot\ocelot.json` |
| Gateway logging | `D:\Workflows\Ocelot\Gateway\RequestLoggingMiddleware.cs` |

---

## 16. COMMON PATTERNS & CONVENTIONS

### Adding a New API Endpoint
1. Add method to controller in `Controllers/`
2. Use `[Authorize(AuthenticationSchemes = "Bearer")]` for JWT-gated endpoints
3. Reference SQL via `SqlQueries.SqlServer.*` constant
4. Add SQL constant to `SqlQueries.SqlServer.cs` and parallel dialect files
5. If admin-only: check `User.FindFirst("IsAdmin")?.Value == "true"`

### Adding a New Elsa Activity
1. Create `Elsa/Activities/MyActivity.cs` extending `Activity`
2. Decorate with `[Action(Category = "MyCategory", Description = "...")]`
3. Use `[ActivityInput]` and `[ActivityOutput]` for parameters/results
4. Override `OnExecuteAsync(ActivityExecutionContext context)`
5. Register via DI (auto-scanned via `AddActivitiesFrom<Program>()`)

### Adding a New Notification Type
1. Define mode string (e.g. `"SMS"`)
2. Extend `NotificationOrchestrator.SendAsync()` to handle new mode
3. Implement `ISmsNotificationService` (follow existing Email pattern)
4. Register in `Program.cs`

### Auth Scheme Decision
```
Calling from JS fetch() with getAuthHeaders()  → [Authorize(AuthenticationSchemes = "Bearer")]
Called from Razor page / form POST / redirect   → [Authorize(AuthenticationSchemes = "Cookies")]
Called from Elsa HTTP activity (external JWT)   → [Authorize(Policy = "...")]
```
