# SnsUpdater Configuration Guide

## Overview

This document describes all configuration settings for the SnsUpdater API application. All settings are configured in the `Web.config` file under the `<appSettings>` section.

## AWS Configuration

### AWS:Region
- **Description**: AWS region for SNS and STS services
- **Type**: String
- **Default**: `us-east-1`
- **Example**: `us-west-2`, `eu-west-1`
- **Required**: Yes

### AWS:RoleArn
- **Description**: IAM role ARN to assume for SNS access
- **Type**: String (ARN format)
- **Default**: None
- **Example**: `arn:aws:iam::123456789012:role/SnsUpdaterRole`
- **Required**: Yes
- **Notes**: This role must have permissions to publish to the SNS topic

### AWS:TopicArn
- **Description**: SNS topic ARN where messages will be published
- **Type**: String (ARN format)
- **Default**: None
- **Example**: `arn:aws:sns:us-east-1:123456789012:person-updates`
- **Required**: Yes

### AWS:RoleSessionName
- **Description**: Session name for assumed role (for AWS CloudTrail)
- **Type**: String
- **Default**: `SnsUpdaterAPI`
- **Example**: `SnsUpdaterAPI-Production`
- **Required**: Yes

## Background Service Configuration

### BackgroundService:ChannelCapacity
- **Description**: Maximum number of messages that can be queued in memory
- **Type**: Integer
- **Default**: `1000`
- **Range**: 1 - 10000
- **Example**: `5000`
- **Notes**: Higher values use more memory. When full, API calls will block.

### BackgroundService:MaxRetryAttempts
- **Description**: Maximum number of retry attempts for failed SNS publishes
- **Type**: Integer
- **Default**: `3`
- **Range**: 1 - 10
- **Example**: `5`
- **Notes**: Failed messages are sent to dead letter after max attempts

### BackgroundService:InitialRetryDelayMs
- **Description**: Initial delay in milliseconds before first retry
- **Type**: Integer
- **Default**: `1000` (1 second)
- **Range**: 100 - 60000
- **Example**: `2000`
- **Notes**: Uses exponential backoff (delay doubles each retry)

## Example Configuration

```xml
<configuration>
  <appSettings>
    <!-- AWS Configuration -->
    <add key="AWS:Region" value="us-east-1" />
    <add key="AWS:RoleArn" value="arn:aws:iam::123456789012:role/SnsUpdaterRole" />
    <add key="AWS:TopicArn" value="arn:aws:sns:us-east-1:123456789012:person-updates" />
    <add key="AWS:RoleSessionName" value="SnsUpdaterAPI-Production" />
    
    <!-- Background Service Configuration -->
    <add key="BackgroundService:ChannelCapacity" value="5000" />
    <add key="BackgroundService:MaxRetryAttempts" value="3" />
    <add key="BackgroundService:InitialRetryDelayMs" value="1000" />
  </appSettings>
</configuration>
```

## Environment-Specific Configuration

### Development
```xml
<add key="AWS:Region" value="us-east-1" />
<add key="AWS:RoleArn" value="arn:aws:iam::123456789012:role/SnsUpdaterRole-Dev" />
<add key="AWS:TopicArn" value="arn:aws:sns:us-east-1:123456789012:person-updates-dev" />
<add key="BackgroundService:ChannelCapacity" value="100" />
```

### Production
```xml
<add key="AWS:Region" value="us-east-1" />
<add key="AWS:RoleArn" value="arn:aws:iam::123456789012:role/SnsUpdaterRole-Prod" />
<add key="AWS:TopicArn" value="arn:aws:sns:us-east-1:123456789012:person-updates-prod" />
<add key="BackgroundService:ChannelCapacity" value="10000" />
```

## OpenTelemetry Configuration (Future)

For production deployments, you can configure OpenTelemetry exporters:

```xml
<!-- OTLP Exporter Configuration -->
<add key="OpenTelemetry:Endpoint" value="http://localhost:4317" />
<add key="OpenTelemetry:Headers" value="api-key=your-api-key" />
<add key="OpenTelemetry:Protocol" value="grpc" />
```

## Circuit Breaker Configuration (Hard-coded)

These values are currently hard-coded but could be made configurable:

- **Failure Threshold**: 5 consecutive failures
- **Timeout Duration**: 1 minute
- **Reset**: Automatic after timeout or manual via API

## Performance Tuning

### High Volume Scenarios
```xml
<add key="BackgroundService:ChannelCapacity" value="10000" />
<add key="BackgroundService:MaxRetryAttempts" value="5" />
<add key="BackgroundService:InitialRetryDelayMs" value="500" />
```

### Low Latency Requirements
```xml
<add key="BackgroundService:ChannelCapacity" value="1000" />
<add key="BackgroundService:MaxRetryAttempts" value="2" />
<add key="BackgroundService:InitialRetryDelayMs" value="100" />
```

## Troubleshooting Configuration Issues

### Common Issues

1. **"Role assumption failed"**
   - Verify `AWS:RoleArn` is correct
   - Check IAM trust relationship allows assuming from EC2/Lambda
   - Ensure role has `sns:Publish` permission

2. **"Queue full" errors**
   - Increase `BackgroundService:ChannelCapacity`
   - Check if background service is running
   - Monitor dead letter queue for processing failures

3. **"Circuit breaker open"**
   - Check SNS topic exists and is active
   - Verify IAM permissions
   - Check AWS service health
   - Reset circuit breaker via API if needed

### Configuration Validation

The application validates configuration on startup:
- Missing required settings cause startup failure
- Invalid numeric values use defaults
- Invalid ARN formats are logged but don't prevent startup

## Security Considerations

1. **Never commit real AWS ARNs to source control**
   - Use configuration transforms
   - Use environment variables in production
   - Use AWS Parameter Store or Secrets Manager

2. **Rotate IAM roles regularly**
   - Update `AWS:RoleArn` when rotating
   - No application restart required

3. **Monitor role usage**
   - Check CloudTrail for `AssumeRole` calls
   - Monitor for unauthorized access attempts