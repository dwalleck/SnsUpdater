# SnsUpdater Troubleshooting Runbook

## Overview

This runbook provides step-by-step troubleshooting procedures for common issues with the SnsUpdater API. Follow these procedures in order when investigating issues.

## Quick Diagnostics

### 1. Check System Health
```bash
GET /api/health
```

Expected healthy response:
```json
{
  "status": "Healthy",
  "services": {
    "messageQueue": { "status": "Healthy" },
    "snsClient": { "status": "Healthy", "circuitBreakerOpen": false }
  }
}
```

### 2. Check Queue Status
```bash
GET /api/health/queue
```

- **Normal**: 0-100 messages
- **Warning**: 100-500 messages
- **Critical**: >500 messages or near capacity

## Common Issues and Solutions

### Issue: API Returns 500 Error on Person Creation

**Symptoms:**
- POST /api/people returns 500
- No person created
- No SNS message sent

**Investigation Steps:**
1. Check application logs for exception details
2. Verify MediatR handler registration
3. Check if background service is running
4. Verify queue is not full

**Resolution:**
```bash
# 1. Check IIS application pool status
appcmd list apppool /name:SnsUpdaterPool

# 2. Restart application pool if needed
appcmd recycle apppool /apppool.name:SnsUpdaterPool

# 3. Check event viewer for .NET errors
eventvwr.msc → Applications and Services Logs → Microsoft → Windows → IIS-W3SVC-WP
```

### Issue: SNS Messages Not Being Sent

**Symptoms:**
- API returns success
- Messages queued (visible in /api/health/queue)
- No messages in SNS topic

**Investigation Steps:**
1. Check circuit breaker status
2. Review dead letter files
3. Verify AWS credentials
4. Check background service logs

**Resolution:**
```bash
# 1. Check circuit breaker
GET /api/health

# 2. If circuit is open, reset it
POST /api/health/circuit-breaker/reset

# 3. Check dead letter files
dir C:\inetpub\wwwroot\SnsUpdater\App_Data\DeadLetters\

# 4. Review most recent dead letter
type C:\inetpub\wwwroot\SnsUpdater\App_Data\DeadLetters\deadletter_*.json
```

### Issue: Circuit Breaker Keeps Opening

**Symptoms:**
- Circuit breaker opens repeatedly
- Messages accumulating in queue
- Dead letter files being created

**Investigation Steps:**
1. Check AWS service health
2. Verify IAM role permissions
3. Test SNS topic accessibility
4. Review error patterns in dead letters

**AWS CLI Diagnostics:**
```bash
# Test role assumption
aws sts assume-role --role-arn "arn:aws:iam::123456789012:role/SnsUpdaterRole" --role-session-name "test"

# Test SNS publish (using assumed role credentials)
aws sns publish --topic-arn "arn:aws:sns:us-east-1:123456789012:person-updates" --message "test"

# Check SNS topic attributes
aws sns get-topic-attributes --topic-arn "arn:aws:sns:us-east-1:123456789012:person-updates"
```

### Issue: High Memory Usage

**Symptoms:**
- w3wp.exe using excessive memory
- Application performance degradation
- OutOfMemoryException in logs

**Investigation Steps:**
1. Check queue depth
2. Review channel capacity configuration
3. Monitor message processing rate
4. Check for memory leaks

**Resolution:**
```bash
# 1. Check current queue depth
GET /api/health/queue

# 2. If queue is full, check why messages aren't processing
# Review dead letters for processing failures

# 3. Consider reducing channel capacity in Web.config
<add key="BackgroundService:ChannelCapacity" value="1000" />

# 4. Restart application to clear queue (WARNING: loses messages)
iisreset
```

### Issue: Messages Lost After Application Restart

**Symptoms:**
- Queue count drops to 0 after restart
- Expected SNS messages never sent
- No dead letter files created

**Root Cause:**
- In-memory channel loses messages on restart
- This is expected behavior with current architecture

**Mitigation:**
1. Implement graceful shutdown procedures
2. Monitor queue depth before deployments
3. Consider database-backed queue for production

**Preventive Measures:**
```bash
# Before planned restart:
# 1. Stop accepting new requests
appcmd set site "Default Web Site" /serverAutoStart:false

# 2. Wait for queue to drain
# Monitor: GET /api/health/queue

# 3. Once queue is empty, restart
iisreset
```

## OpenTelemetry Diagnostics

### Viewing Traces and Metrics

**Console Output Location:**
- IIS: `C:\inetpub\logs\stdout\`
- Visual Studio: Output window

**Common Trace Issues:**
1. **No traces visible**: Check if OpenTelemetry is initialized
2. **Missing spans**: Verify activity sources are registered
3. **No metrics**: Check meter registration

### Metric Interpretation

| Metric | Normal Range | Investigation Threshold |
|--------|--------------|------------------------|
| persons_created | 0-100/min | >1000/min or 0/min |
| messages_queued | Should match persons_created | Divergence >10% |
| messages_processed | Should match queued within 1min | Lag >5min |
| messages_retried | <5% of processed | >10% indicates issues |
| messages_deadlettered | 0-1/hour | >10/hour critical |
| queue_size | 0-100 | >500 investigate |
| circuit_breaker_status | 0 (closed) | 1 (open) requires action |

## Dead Letter Analysis

### Location
`C:\inetpub\wwwroot\SnsUpdater\App_Data\DeadLetters\`

### File Format
`deadletter_YYYYMMDD_{MessageId}.json`

### Analyzing Dead Letters
```powershell
# Count dead letters by date
Get-ChildItem -Path ".\App_Data\DeadLetters" | Group-Object { $_.Name.Substring(11,8) } | Select Name, Count

# Find most common error
Get-ChildItem -Path ".\App_Data\DeadLetters" -Filter "*.json" | 
    ForEach-Object { Get-Content $_ | ConvertFrom-Json } | 
    Group-Object { $_.Error.Message } | 
    Sort-Object Count -Descending | 
    Select -First 10 Count, Name
```

### Common Dead Letter Causes

1. **InvalidParameterException**
   - Invalid SNS topic ARN
   - Malformed message attributes

2. **AccessDeniedException**
   - IAM role lacks sns:Publish
   - Topic policy denies access

3. **ExpiredTokenException**
   - Credential refresh failed
   - STS endpoint unreachable

## Performance Troubleshooting

### Slow API Response Times

**Check:**
1. Queue depth (blocking on full queue)
2. OpenTelemetry trace durations
3. Database connection (when implemented)

**Commands:**
```bash
# Check IIS request queue
appcmd list request /elapsed:5000

# Check worker process
appcmd list wp
```

### High CPU Usage

**Common Causes:**
1. Infinite retry loop
2. Excessive logging
3. Serialization issues

**Investigation:**
1. Attach debugger to w3wp.exe
2. Check for hot paths in CPU profiler
3. Review retry configuration

## Emergency Procedures

### Complete System Failure

1. **Immediate Actions:**
   ```bash
   # Reset IIS
   iisreset /stop
   iisreset /start
   
   # Clear dead letters to prevent disk full
   del C:\inetpub\wwwroot\SnsUpdater\App_Data\DeadLetters\*.json
   ```

2. **Verify Core Services:**
   - AWS STS endpoint accessible
   - SNS service healthy
   - Network connectivity

3. **Fallback Mode:**
   - Temporarily increase retry delays
   - Reduce channel capacity
   - Enable detailed logging

### Data Recovery

**Recover from Dead Letters:**
```powershell
# PowerShell script to replay dead letters
$deadLetters = Get-ChildItem -Path ".\App_Data\DeadLetters" -Filter "*.json"
foreach ($file in $deadLetters) {
    $content = Get-Content $file | ConvertFrom-Json
    # POST to API to reprocess
    Invoke-RestMethod -Uri "http://localhost/api/people" -Method Post -Body @{
        firstName = $content.Body.FirstName
        lastName = $content.Body.LastName
        phoneNumber = $content.Body.PhoneNumber
    }
}
```

## Monitoring Checklist

### Daily Checks
- [ ] Queue depth <100
- [ ] Circuit breaker closed
- [ ] No new dead letters
- [ ] API response time <200ms

### Weekly Checks
- [ ] Review dead letter patterns
- [ ] Check memory usage trends
- [ ] Verify AWS role permissions
- [ ] Review OpenTelemetry metrics

### Monthly Checks
- [ ] Analyze performance trends
- [ ] Update runbook with new issues
- [ ] Test disaster recovery procedures
- [ ] Review AWS costs

## Contact Information

### Escalation Path
1. Application Support Team
2. DevOps Team (for infrastructure)
3. AWS Support (for service issues)

### Key Resources
- Application Logs: `C:\inetpub\logs\LogFiles\`
- IIS Logs: `C:\inetpub\logs\LogFiles\W3SVC1\`
- Event Viewer: Windows Logs → Application
- AWS Console: CloudWatch Logs for Lambda/ECS
- OpenTelemetry: Console exporter output