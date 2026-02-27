# Deployment Topology - Infrastructure & Deployment

**Last Updated:** February 5, 2026  
**Status:** Production  
**Owner:** DevOps / IT Manager

## Purpose

This diagram shows the physical and logical deployment topology, including:
- On-premises vs. cloud infrastructure
- Network architecture
- Database topology
- Credential management
- Deployment process and environments

## Current Deployment Topology (Phase 1)

```mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
flowchart TB
  subgraph OnPrem["On-Premises (Company Data Center)"]
    subgraph Compute["Compute Layer"]
      SCHED["Windows Scheduler<br/>(1st of month, 1:00 AM)"]
      SERVICE["MyInvois-Service<br/>(ASP.NET Core 8)<br/>Windows Server 2022"]
    end
    
    subgraph Data["Data Layer"]
      SQLDB["SQL Server 2022<br/>Audit Database<br/>- AuditLog table<br/>- 7-year retention<br/>- TDE encryption"]
      CACHE["Local Cache<br/>(In-Memory)<br/>Configuration"]
    end
    
    subgraph Network["Network"]
      FW["Firewall<br/>Port 443 outbound"]
      DNS["DNS<br/>API resolution"]
    end
  end
  
  subgraph Azure["Microsoft Azure (Cloud)"]
    subgraph Cloud["Cloud Services"]
      KEYVAULT["Azure Key Vault<br/>- OAuth credentials<br/>- e-signature keys<br/>- TLS certificates<br/>- Access: encrypted"]
    end
  end
  
  subgraph External["External Systems"]
    MOVEX["MOVEX (M3)<br/>Manufacturing ERP<br/>REST API<br/>https://m3.company.com"]
    
    MYINVOIS["MyInvois Portal<br/>Malaysian Tax Authority<br/>OAuth 2.0 + REST API<br/>https://myinvois.gov.my"]
  end
  
  SCHED -->|1. Trigger| SERVICE
  SERVICE -->|2. Read config| CACHE
  SERVICE -->|3. Get credentials| KEYVAULT
  KEYVAULT -->|Return secrets<br/>encrypted| SERVICE
  SERVICE -->|4. Fetch invoices| MOVEX
  MOVEX -->|JSON invoices| SERVICE
  SERVICE -->|5. Log to DB| SQLDB
  SQLDB -->|Confirm| SERVICE
  SERVICE -->|6. Submit to MyInvois| MYINVOIS
  MYINVOIS -->|Response| SERVICE
  SERVICE -->|7. Log result| SQLDB
  
  FW -->|Restrict outbound| External
  DNS -->|Resolve| External
  
  style OnPrem fill:#f0f0f0
  style Azure fill:#e6f2ff
  style External fill:#ffe6e6
```

## Network Architecture

```mermaid
---
config:
  theme: light
---
graph LR
  subgraph Internal["Company Internal Network"]
    PC["Finance PC<br/>Windows 10/11"]
    SERVICE["MyInvois-Service<br/>Windows Server"]
    SQLDB["SQL Server<br/>Database"]
    FW["Firewall"]
  end
  
  subgraph Internet["Internet / Cloud"]
    AZURE["Azure Key Vault"]
    MOVEX["MOVEX API<br/>m3.company.com"]
    MYINVOIS["MyInvois API<br/>myinvois.gov.my"]
  end
  
  PC -->|Local network<br/>TCP/IP| SERVICE
  SERVICE -->|TCP 1433| SQLDB
  SERVICE -->|Port 443<br/>HTTPS| FW
  FW -->|Outbound allowed| AZURE
  FW -->|Outbound allowed| MOVEX
  FW -->|Outbound allowed| MYINVOIS
  
  AZURE -->|Encrypted secrets| SERVICE
  MOVEX -->|JSON invoices| SERVICE
  SERVICE -->|Signed UBL docs| MYINVOIS
```

## Deployment Process

```mermaid
---
config:
  theme: light
---
sequenceDiagram
  autonumber
  participant Dev as Developer
  participant GitHub as GitHub<br/>Repository
  participant Build as Build<br/>System<br/>CI/CD
  participant Staging as Staging<br/>Environment
  participant Prod as Production<br/>Environment
  participant Test as QA<br/>Testing
  
  Note over Dev,Prod: DEVELOPMENT PHASE
  
  Dev->>GitHub: Push code changes<br/>(feature branch)
  GitHub->>Build: Trigger CI pipeline<br/>(on PR)
  Build->>Build: Run unit tests<br/>Target: ≥80% coverage
  Build->>Build: Run linters<br/>(StyleCop, SecurityCodeScan)
  Build->>Build: Build Docker image
  alt All checks pass
    Build-->>Dev: ✓ All green
  else Checks fail
    Build-->>Dev: ✗ Failures<br/>Fix required
  end
  
  Dev->>GitHub: Merge to main branch<br/>(after approval)
  
  Note over Dev,Prod: STAGING DEPLOYMENT
  
  GitHub->>Build: Trigger release pipeline
  Build->>Staging: Deploy to staging<br/>ASP.NET Core 8<br/>Windows Server
  
  Note over Test: Smoke Tests
  Test->>Staging: Run integration tests<br/>- MOVEX API mock<br/>- MyInvois API mock<br/>- Database smoke tests
  Test->>Staging: Run security tests<br/>- OAuth flow<br/>- Key Vault access<br/>- TLS validation
  Test->>Staging: Run performance tests<br/>- Batch time: <40 min
  
  alt All tests pass
    Test-->>Build: ✓ Ready for production
  else Tests fail
    Test-->>Build: ✗ Issues found<br/>Debug and retest
  end
  
  Note over Dev,Prod: PRODUCTION DEPLOYMENT
  
  Build->>Prod: Deploy to production<br/>(approved window only)
  Note over Prod: Deployment window:<br/>Friday 5 PM - Sunday 8 AM<br/>Avoid: Month-end (27-31)
  
  Prod->>Prod: Stop previous version
  Prod->>Prod: Backup database (automatic)
  Prod->>Prod: Deploy new ASP.NET Core image
  Prod->>Prod: Run health checks
  Prod->>Prod: Verify Key Vault access
  
  alt Deployment succeeds
    Prod-->>Build: ✓ Live
    Build->>Build: Tag release in GitHub
  else Deployment fails
    Prod-->>Build: ✗ Rollback triggered
    Prod->>Prod: Restore previous version
    Build->>Dev: Notify team of rollback
  end
```

## Database Topology

### SQL Server Schema

```
Database: MyInvoisAudit
├── [dbo].[AuditLog] (Main audit table)
│   ├── AuditId (PK, GUID)
│   ├── Timestamp (DateTime2, UTC)
│   ├── UserId (Service account)
│   ├── Action (e.g., "MyInvois_Submit")
│   ├── ResourceType (Invoice)
│   ├── ResourceId (Invoice ID)
│   ├── Status (Success/Failed)
│   ├── MyInvoisUUID (From response)
│   ├── ErrorMessage (If failed)
│   ├── RequestPayload (Signed UBL)
│   └── ResponsePayload (MyInvois response)
│
├── Indexes
│   ├── IX_AuditLog_Timestamp (Clustered)
│   ├── IX_AuditLog_Status (Non-clustered)
│   ├── IX_AuditLog_MyInvoisUUID (Non-clustered)
│   └── IX_AuditLog_Resource (Non-clustered)
│
└── Views
    ├── vw_MonthlySubmissions (Summary by month)
    ├── vw_FailedSubmissions (Only failures)
    └── vw_AuditTrail (Formatted for compliance)
```

### Encryption & Security
- **Encryption at Rest**: TDE (Transparent Data Encryption) enabled
- **Encryption in Transit**: TLS 1.2+ for all connections
- **Backup Encryption**: Encrypted backups to network storage
- **Access Control**: 
  - Service account (read/write)
  - Database admin (backup/restore)
  - Auditor (read-only)

### Backup & Recovery
- **Frequency**: Daily automated backups (11 PM)
- **Retention**: 30 days (on-premises), 7 years (archive)
- **Recovery SLA**: RTO 4 hours, RPO 24 hours
- **Testing**: Quarterly restoration tests

---

## Azure Key Vault Configuration

```
Resource Group: myinvois-rg
Key Vault: myinvois-keyvault-prod

Secrets:
├── MyInvoisApiSettings--ClientId
├── MyInvoisApiSettings--ClientSecret
├── MyInvoisSigningKey (PEM format)
├── MyInvoisSigningCert (X.509)
├── MovexApiToken (if applicable)
└── SqlServer--ConnectionString

Access Policies:
├── Service account: Get, List
├── IT Manager: Get, List, Set, Delete
└── Automation: Get (read-only)
```

---

## Environments

### Development Environment
- **Location**: Developer machine
- **Configuration**: appsettings.Development.json
- **Database**: LocalDB or staging SQL Server
- **External APIs**: Mock servers
- **Logging**: Console + file
- **Authentication**: User Secrets (local key management)

### Staging Environment
- **Location**: On-premises staging server
- **Configuration**: appsettings.Staging.json
- **Database**: Staging SQL Server (copy of prod)
- **External APIs**: Mock servers
- **Logging**: File + Event Log
- **Authentication**: Azure Key Vault (same as prod)
- **Purpose**: Final testing before production deployment

### Production Environment
- **Location**: On-premises production server
- **Configuration**: appsettings.json
- **Database**: Production SQL Server
- **External APIs**: Real MOVEX + MyInvois endpoints
- **Logging**: SQL Server audit log
- **Authentication**: Azure Key Vault
- **Purpose**: Live monthly batch processing

---

## Future Deployment Topology (Phase 3 - Cloud)

```
Target: Azure App Service + Azure SQL Database

Current: On-Premises
├── Windows Server + .NET Service
├── SQL Server (on-premises)
└── Manual credential management

Future: Azure Cloud
├── App Service (PaaS)
├── Azure SQL Database (Managed)
├── Azure Key Vault (Managed credentials)
├── Application Insights (Monitoring)
├── Azure Storage (Backups/Archive)
└── Auto-scaling (if needed)

Benefits:
✓ Reduced infrastructure overhead
✓ Automatic backups & geo-replication
✓ Global availability
✓ Pay-per-use cost model
✓ Easier disaster recovery
```

---

## Monitoring & Health Checks

### Service Health Checks
- **Startup**: Verify Key Vault access
- **Startup**: Verify SQL Server connection
- **Periodic**: Check external API availability (MOVEX, MyInvois)
- **Continuous**: Monitor error logs for critical issues

### Alerting
- **Alert 1**: Service fails to start → Email IT Manager
- **Alert 2**: Batch fails to complete → Email Finance Manager
- **Alert 3**: Database full → Email DBA
- **Alert 4**: MyInvois API unreachable → Email IT Manager

---

## Disaster Recovery Plan

### Recovery Objectives
- **RTO** (Recovery Time Objective): 4 hours
- **RPO** (Recovery Point Objective): 24 hours (daily backups)

### Failure Scenarios

| Failure | Recovery Action | Time | Data Loss |
|---------|-----------------|------|-----------|
| Service crashes | Restart service | 5 min | 0 (in-memory data lost, re-process) |
| Database corrupted | Restore from backup | 1 hour | 24 hours |
| Key Vault inaccessible | Manual credential entry (temp) | 30 min | 0 |
| Network outage | Batch reschedule | N/A | 0 |
| Complete data center loss | Fail over to backup DC | 2-4 hours | 24 hours |

### Backup & Recovery Testing
- **Quarterly**: Test database restore from backup
- **Semi-annually**: Test Key Vault recovery procedure
- **Annually**: Full disaster recovery drill

---

## Maintenance & Updates

### Scheduled Maintenance Window
- **Frequency**: First Saturday of month, 8 PM - 10 PM
- **Type**: Patches, updates, housekeeping
- **Notification**: Sent to Finance Manager 1 week prior

### Patch Management
- **Security patches**: Within 48 hours of release
- **Minor updates**: Monthly (first Saturday)
- **Major upgrades**: Quarterly + testing
- **Verification**: Smoke tests after all updates

---

## Related Diagrams
- [architecture.md](architecture.md) - System components
- [auth-flow.md](auth-flow.md) - Credential management
- [data-flow.md](data-flow.md) - Data movement
