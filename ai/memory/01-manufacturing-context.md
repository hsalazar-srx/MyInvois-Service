# Manufacturing Context - MyInvois-Service

**Last Updated:** February 5, 2026  
**Status:** Active  
**Owner:** Finance Operations Manager

---

## Industry & Plant Environment

### Industry Type
- ✅ **Manufacturing** (Multi-site operations)
- **Region**: Malaysia
- **Scale**: Multiple plants with centralized finance

### Plant Environment Assumptions
- **Operating Model**: 24/7 manufacturing operations
- **Finance Operations**: Business hours (8 AM - 5 PM local time)
- **Month-End Close**: Critical period (28th-31st of each month)
- **Downtime Windows**: Available during off-business hours and weekends
- **Finance Team Size**: 4-6 finance staff managing invoicing

### Operational Constraints

#### Availability & Timing
- **Available Downtime Windows**: 
  - Weekends (Friday EOD - Monday 8 AM)
  - Business off-hours (5 PM - 8 AM)
  - Month-end processing window (27th-3rd of following month)
- **Maximum Acceptable Latency**: 2 hours for batch processing
- **Critical Periods to Avoid**: 
  - Month-end close (27th-31st) - high manual activity
  - Quarter-end (high volume, approvals)
  - Annual close (December/January)

#### System Dependencies
- **M3 MOVEX System** (Invoice Source)
  - Real-time data available 24/7
  - Scheduled exports every 6 hours
  - Timeout tolerance: 30 seconds max
- **MyInvois Government System** (Tax Compliance)
  - Available 6 AM - 10 PM every day
  - Maintenance windows: 10 PM - 6 AM (no submissions)
  - Rate limit: 100 requests/minute per account
  - Network latency: Expect 2-5 second delays
- **SQL Server Audit Database** (Compliance)
  - Always available (7 years retention requirement)
  - Backup window: 11 PM - 12 AM daily

### User Constraints & Roles

#### Primary Users
1. **Finance Officer** (Invoice Review)
   - Reviews batch before submission
   - Approves submission window
   - Monitors submission status
   - **Constraint**: Minimal UI changes (trained on current interface)

2. **Accounts Manager** (Oversight)
   - Reviews audit trail monthly
   - Resolves failed submissions
   - Manages MyInvois account settings
   - **Constraint**: Needs clear error messages

3. **System Administrator** (Operations)
   - Schedules batch jobs
   - Monitors system health
   - Handles emergency scenarios
   - **Constraint**: Limited PowerShell/C# knowledge

#### User Constraints Summary
- Low tolerance for workflow disruptions
- Training costs matter (no major UI redesigns)
- Require clear audit trails for compliance
- Prefer automated notifications over manual checks
- Unable to handle complex troubleshooting

### Integration Points

#### Data Sources
- **M3 MOVEX**
  - Endpoint: [MOVEX API Server]
  - Method: REST API
  - Frequency: On-demand + scheduled
  - Invoice data: 600-1,100/month
  - Contract: [See memory/03-integration-contracts.md]

#### Data Consumers
- **MyInvois Portal** (Tax Authority)
  - Endpoint: https://[MyInvois API]
  - Method: REST + XML (UBL 2.1)
  - Rate limit: 100 req/minute
  - Authentication: OAuth 2.0
  - Compliance: XAdES v1.1 e-signature required

- **Audit & Compliance System**
  - SQL Server database
  - 7-year retention policy
  - Access: Finance team + auditors
  - Read-only for business users

### Change Management

#### Required Approvals
| Change Type | Approver | Lead Time | Notes |
|-------------|----------|-----------|-------|
| Process changes | Finance Manager | 2 weeks | Affects batch timing |
| Batch window changes | Operations | 1 week | Affects schedules |
| MyInvois integration changes | IT Manager + Finance Lead | 3 weeks | Compliance-critical |
| Database schema changes | DBA + Audit Officer | 4 weeks | Audit trail impact |
| Emergency fixes | IT Manager | Immediate | Post-change review required |

#### Testing Requirements
- **Unit Tests**: ≥80% code coverage required
- **Integration Tests**: Must test M3 + MyInvois flows
- **Smoke Tests**: Run before each production deployment
- **Validation Tests**: 20+ mandatory fields for each invoice
- **Dry-Run Period**: 2-week shadow mode for major changes

#### Training Requirements
- **Finance Team**: 1-hour orientation for UI changes
- **Admins**: 2-hour training for operational procedures
- **Documentation**: Updated in docs/ folder
- **Runbooks**: Maintained for common issues

### Manufacturing-Specific Considerations

#### Compliance Criticality
- **ISO 27001 Requirement**: All transactions must be auditable
- **Tax Authority (LHDNM)**: Monthly filing mandatory
- **Data Retention**: 7 years minimum (Malaysian tax law)
- **No deletion rule**: Once submitted, never delete audit logs

#### Operational Continuity
- **MyInvois outages**: Must be able to retry with same data
- **Network failures**: Implement exponential backoff + queue
- **Finance system unavailable**: Batch should be reschedulable
- **M3 data delays**: Graceful degradation (process what's available)

#### Financial Impact
- **Late submission penalty**: Potential tax penalties
- **Data loss**: Could lose compliance evidence
- **Security breach**: Could expose customer tax data
- **Service disruption**: Finance team cannot work manually efficiently

---

## Related Documentation
- [00-product-vision.md](00-product-vision.md) - What we're building
- [02-system-architecture.md](02-system-architecture.md) - Technical design
- [03-integration-contracts.md](03-integration-contracts.md) - Integration specs
- [ai/rules.md](../rules.md) - AI safety rules for this context
