# SnsUpdater API Documentation

## Overview

The SnsUpdater API provides endpoints for managing person records with automatic SNS notification on creation. The system implements a resilient, asynchronous messaging pattern using MediatR, in-memory channels, and background processing.

## Architecture

### Flow Diagram
```
HTTP Request → API Controller → MediatR Command → Command Handler
                                                    ↓
                                              Save Person
                                                    ↓
                                              Publish Event
                                                    ↓
                                           Event Handler → Queue Message
                                                              ↓
                                                    Background Service
                                                              ↓
                                                         AWS SNS
```

### Key Components
- **MediatR**: Command/Event pattern for decoupled architecture
- **In-Memory Channels**: High-performance message queuing
- **Background Service**: Resilient message processing with retry logic
- **OpenTelemetry**: Comprehensive observability

## API Endpoints

### Create Person
Creates a new person record and queues SNS notification.

**Endpoint:** `POST /api/people`

**Request Body:**
```json
{
  "firstName": "string",
  "lastName": "string",
  "phoneNumber": "string" // optional
}
```

**Response (201 Created):**
```json
{
  "id": 1234,
  "firstName": "string",
  "lastName": "string",
  "phoneNumber": "string",
  "success": true,
  "message": "Person created successfully."
}
```

**Response (400 Bad Request):**
```json
{
  "message": "First name and last name are required."
}
```

**Headers:**
- `X-Correlation-Id`: Optional. If provided, will be propagated through to SNS message

### Health Check
Returns current system health status.

**Endpoint:** `GET /api/health`

**Response (200 OK):**
```json
{
  "status": "Healthy",
  "timestamp": "2024-01-08T12:00:00Z",
  "services": {
    "messageQueue": {
      "status": "Healthy",
      "queuedMessages": 5
    },
    "snsClient": {
      "status": "Healthy",
      "circuitBreakerOpen": false
    }
  },
  "telemetry": {
    "activeTraces": true,
    "activitySources": [
      "SnsUpdater.API.Api",
      "SnsUpdater.API.Messaging",
      "SnsUpdater.API.BackgroundService"
    ]
  }
}
```

### Queue Status
Returns current message queue status.

**Endpoint:** `GET /api/health/queue`

**Response (200 OK):**
```json
{
  "queuedMessages": 10,
  "timestamp": "2024-01-08T12:00:00Z"
}
```

### Reset Circuit Breaker
Manually resets the SNS circuit breaker.

**Endpoint:** `POST /api/health/circuit-breaker/reset`

**Response (200 OK):**
```json
{
  "message": "Circuit breaker reset successfully",
  "timestamp": "2024-01-08T12:00:00Z"
}
```

## SNS Message Format

Messages published to SNS have the following format:

**Subject:** `Person Created: {FirstName} {LastName}`

**Message Body:**
```json
{
  "EventType": "PersonCreated",
  "PersonId": 1234,
  "FirstName": "John",
  "LastName": "Doe",
  "PhoneNumber": "555-123-4567",
  "CreatedAt": "2024-01-08T12:00:00Z",
  "Timestamp": "2024-01-08T12:00:00Z"
}
```

**Message Attributes:**
- `eventType`: String - "PersonCreated"
- `personId`: Number - The person's ID

## Error Handling

### Validation Errors
- Returns 400 Bad Request with descriptive message
- No SNS message is queued

### Internal Errors
- Returns 500 Internal Server Error
- Generic error message to client
- Detailed error logged internally

### SNS Failures
- Automatic retry with exponential backoff (1s, 2s, 4s)
- Maximum 3 retry attempts
- Failed messages logged to dead letter file
- Circuit breaker prevents cascading failures

## Resilience Features

### Retry Logic
- Exponential backoff starting at 1 second
- Maximum 3 attempts per message
- Retry count tracked per message

### Circuit Breaker
- Opens after 5 consecutive failures
- Stays open for 1 minute
- Automatically resets after timeout
- Manual reset available via API

### Dead Letter Queue
- Failed messages saved to JSON files
- Located in `App_Data/DeadLetters/`
- Includes full message details and error information

## Performance Characteristics

### Queue Capacity
- Default: 1000 messages
- Configurable via `BackgroundService:ChannelCapacity`
- Blocks when full (BoundedChannelFullMode.Wait)

### Processing
- Single background thread for message processing
- FIFO message ordering guaranteed
- Non-blocking API responses

### Scalability Considerations
- In-memory queue limits horizontal scaling
- Messages lost on application restart
- Consider database-backed queue for production

## Security

### AWS Authentication
- Uses STS AssumeRole for cross-account access
- Credentials cached for 55 minutes
- Automatic refresh before expiry

### API Security
- Standard ASP.NET Web API authentication
- Correlation ID for request tracking
- No PII logged in dead letters

## Monitoring

### OpenTelemetry Metrics
- `persons_created`: Person creation count
- `messages_queued`: Messages queued count
- `messages_processed`: Messages processed count
- `messages_retried`: Retry attempts count
- `messages_deadlettered`: Dead letter count
- `message_processing_duration`: Processing time histogram
- `queue_size`: Current queue depth
- `circuit_breaker_status`: Circuit state (0=closed, 1=open)

### Distributed Tracing
- End-to-end request tracing
- Correlation ID propagation
- Exception capture in traces
- Performance timing

### Health Monitoring
- Health check endpoint for monitoring tools
- Queue depth visibility
- Circuit breaker status
- Real-time metrics via OpenTelemetry

## Development

### Local Testing
1. Configure AWS credentials in Web.config
2. Set up SNS topic and IAM role
3. Run application in Visual Studio
4. Use Postman or curl to test endpoints

### Troubleshooting
- Check dead letter files for failed messages
- Monitor circuit breaker status
- Review OpenTelemetry console output
- Check application traces for errors

### Dependencies
- .NET Framework 4.8.1
- MediatR 12.2.0
- AWS SDK for .NET
- OpenTelemetry 1.6.0
- Unity Container 5.11.10