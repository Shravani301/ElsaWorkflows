# Database Backup

The MozartPlatform DEV database backup (`.bacpac`) is **not stored in this Git repository**.

## Why

- The backup file is ~444 MB, which exceeds GitHub's 100 MB per-file hard limit.
- A `.bacpac` contains both schema and table data, so publishing it would expose DEV data, configuration rows, and any secrets stored in the database.

## Where to obtain the backup

Request the latest `.bacpac` from the team. Typical sources:

- Internal file share / OneDrive
- Azure Storage container used for DEV backups
- Generated on demand from SQL Server Management Studio:
  `Tasks → Export Data-tier Application... → .bacpac`

## Restoring locally

Using **SqlPackage**:

```powershell
SqlPackage.exe /Action:Import `
  /SourceFile:"<path-to>\MozartPlatform_DEV.bacpac" `
  /TargetServerName:"localhost" `
  /TargetDatabaseName:"MozartPlatform_DEV"
```

Or in **SSMS**: right-click the server → *Import Data-tier Application...* → select the `.bacpac`.

## Filename convention

`MozartPlatform_DEV <n>_<dd-MM-yyyy>.bacpac` — for example, `MozartPlatform_DEV 1_22-05-2026.bacpac`.

After restoring, update `appsettings.Secrets.json` with the local connection string. See the root `README` for details.
