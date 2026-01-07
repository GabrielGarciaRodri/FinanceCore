# FinanceCore

Enterprise-grade financial ETL system for transaction processing, reconciliation, and analysis built with .NET 8 and Clean Architecture.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Technology Stack](#technology-stack)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Domain Model](#domain-model)
- [Key Features](#key-features)
- [API Endpoints](#api-endpoints)
- [Background Jobs](#background-jobs)
- [Configuration](#configuration)
- [Development](#development)
- [License](#license)

## Overview

FinanceCore is a comprehensive financial data processing platform designed to handle high-volume transaction ingestion, automated reconciliation, and real-time balance tracking. The system implements enterprise patterns and practices commonly found in banking and fintech applications.

### Core Capabilities

- **Transaction Ingestion**: Process transactions from multiple sources (APIs, CSV, Excel, SFTP) with idempotency guarantees
- **Automated Reconciliation**: Match internal records against external sources with configurable tolerance thresholds
- **Balance Management**: Real-time balance tracking with daily close procedures and audit trails
- **Multi-Currency Support**: Handle transactions in multiple currencies with automatic exchange rate conversion
- **Compliance Ready**: Full audit logging with immutable transaction history

### Business Rules

| Principle | Implementation |
|-----------|----------------|
| Double-Entry Accounting | Every transaction generates balanced debit/credit entries |
| Immutability | Posted transactions cannot be modified, only adjusted |
| Precision | All monetary values use decimal(18,4) with banker's rounding |
| Traceability | Complete audit trail for every operation |
| Idempotency | Duplicate detection via external_id and hash |

## Architecture

The system follows Clean Architecture principles with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────────────┐
│                        PRESENTATION                             │
│         REST API  |  Hangfire Dashboard  |  Health Checks       │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                        APPLICATION                              │
│           Commands  |  Queries  |  MediatR Pipeline             │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                          DOMAIN                                 │
│      Entities  |  Value Objects  |  Domain Events  |  Rules     │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                       INFRASTRUCTURE                            │
│     EF Core  |  Dapper  |  Hangfire  |  External Services       │
└─────────────────────────────────────────────────────────────────┘
```

### Design Patterns

- **CQRS**: Command Query Responsibility Segregation with MediatR
- **Repository Pattern**: Abstraction over data access with Unit of Work
- **Domain Events**: Loose coupling between aggregates
- **Result Pattern**: Explicit error handling without exceptions
- **Value Objects**: Encapsulation of domain concepts (Money, Currency)

## Technology Stack

| Category | Technology | Purpose |
|----------|------------|---------|
| Framework | .NET 8 | Runtime and SDK |
| Web API | ASP.NET Core | REST API endpoints |
| ORM | Entity Framework Core 8 | Write operations and migrations |
| Micro-ORM | Dapper | High-performance read queries |
| Database | PostgreSQL 16 | Primary data store |
| Cache | Redis | Distributed caching |
| Background Jobs | Hangfire | Scheduled and queued job processing |
| Validation | FluentValidation | Request validation |
| Mediator | MediatR | CQRS and pipeline behaviors |
| Logging | Serilog | Structured logging |
| Documentation | Swagger/OpenAPI | API documentation |
| Containerization | Docker | Development and deployment |

## Project Structure

```
FinanceCore/
├── src/
│   ├── FinanceCore.Domain/
│   │   ├── Entities/
│   │   │   ├── Transaction.cs
│   │   │   ├── Account.cs
│   │   │   └── ...
│   │   ├── ValueObjects/
│   │   │   ├── Money.cs
│   │   │   └── Currency.cs
│   │   ├── Enums/
│   │   ├── Events/
│   │   ├── Exceptions/
│   │   └── Repositories/
│   │
│   ├── FinanceCore.Application/
│   │   ├── Common/
│   │   │   ├── Behaviors/
│   │   │   └── Models/
│   │   └── Transactions/
│   │       └── Commands/
│   │
│   ├── FinanceCore.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Context/
│   │   │   └── Repositories/
│   │   └── BackgroundJobs/
│   │       ├── Jobs/
│   │       └── Configuration/
│   │
│   └── FinanceCore.API/
│       ├── Controllers/
│       ├── Program.cs
│       └── appsettings.json
│
├── database/
│   └── migrations/
│       └── V001__Initial_Schema.sql
│
├── docker-compose.yml
└── README.md
```

## Getting Started

### Prerequisites

- .NET 8 SDK
- Docker and Docker Compose
- PostgreSQL 16 (or use Docker)
- Redis (or use Docker)

### Installation

1. Clone the repository:

```bash
git clone https://github.com/GabrielGarciaRodri/FinanceCore.git
cd FinanceCore
```

2. Start infrastructure services:

```bash
docker-compose up -d
```

3. Restore dependencies:

```bash
dotnet restore
```

4. Build the solution:

```bash
dotnet build
```

5. Run the API:

```bash
cd src/FinanceCore.API
dotnet run
```

### Access Points

| Service | URL |
|---------|-----|
| API | http://localhost:5000 |
| Swagger UI | http://localhost:5000/swagger |
| Hangfire Dashboard | http://localhost:5000/hangfire |
| Health Check | http://localhost:5000/health |
| pgAdmin | http://localhost:5050 |

## Domain Model

### Core Entities

**Transaction**: Represents a financial movement with full audit trail.

```csharp
public class Transaction : BaseEntity, IAggregateRoot
{
    public string ExternalId { get; }           // Source system identifier
    public Money Amount { get; }                // Value with currency
    public TransactionType Type { get; }        // Debit, Credit, Transfer, etc.
    public TransactionStatus Status { get; }    // Pending, Posted, Reconciled, etc.
    public DateOnly ValueDate { get; }          // Effective date
    public string Hash { get; }                 // Duplicate detection
}
```

**Account**: Financial account with balance tracking and optimistic concurrency.

```csharp
public class Account : BaseEntity, IAggregateRoot
{
    public string AccountNumber { get; }
    public Money CurrentBalance { get; }
    public Money AvailableBalance { get; }
    public int Version { get; }                 // Concurrency token
}
```

### Value Objects

**Money**: Immutable representation of monetary values with banker's rounding.

```csharp
var price = Money.FromDecimal(100.50m, Currency.USD);
var tax = price.Percentage(8.25m);
var total = price.Add(tax);  // Ensures same currency
```

**Currency**: ISO 4217 currency codes with validation.

```csharp
var usd = Currency.FromCode("USD");
var cop = Currency.COP;  // Predefined constants
```

## Key Features

### Idempotent Transaction Processing

Transactions are uniquely identified by the combination of `external_id` and `source`, preventing duplicates during reprocessing:

```csharp
var command = new IngestTransactionsCommand
{
    Source = "BANK_API",
    Transactions = transactions,
    FailOnFirstError = false
};

var result = await mediator.Send(command);
// Result: { Succeeded: 95, Failed: 3, Duplicates: 2 }
```

### Pipeline Behaviors

Cross-cutting concerns are handled through MediatR pipeline behaviors:

| Behavior | Purpose |
|----------|---------|
| LoggingBehavior | Request/response logging with timing |
| ValidationBehavior | FluentValidation execution |
| TransactionBehavior | Database transaction management |
| CachingBehavior | Response caching for queries |

### Dual Data Access Strategy

- **Entity Framework Core**: Write operations with change tracking and migrations
- **Dapper**: High-performance read queries for reporting and search

## API Endpoints

### Transactions

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | /api/transactions/ingest | Ingest batch of transactions |
| GET | /api/transactions/{id} | Get transaction by ID |
| GET | /api/transactions/search | Search with filters and pagination |
| GET | /api/transactions/accounts/{accountId}/summary | Account transaction summary |

### Request Example

```http
POST /api/transactions/ingest
Content-Type: application/json

{
  "source": "BANCOLOMBIA_API",
  "sourceType": "BankApi",
  "failOnFirstError": false,
  "transactions": [
    {
      "externalId": "TXN-2024-001",
      "accountId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "amount": -150000.00,
      "currencyCode": "COP",
      "type": "Debit",
      "description": "Wire transfer",
      "valueDate": "2024-01-15"
    }
  ]
}
```

## Background Jobs

Scheduled jobs are managed through Hangfire with PostgreSQL persistence:

| Job | Schedule | Description |
|-----|----------|-------------|
| TransactionIngestionJob | Every 15 minutes | Process pending file uploads |
| DailyCloseJob | 23:59 daily | Calculate daily balances and detect discrepancies |
| ExchangeRateUpdateJob | Hourly (8am-6pm, Mon-Fri) | Update currency exchange rates |
| DataCleanupJob | Sundays at 3am | Archive old audit logs |

### Job Configuration

Jobs support automatic retry with exponential backoff:

```csharp
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
[DisableConcurrentExecution(timeoutInSeconds = 600)]
public async Task ExecuteDailyCloseAsync(DateOnly closeDate, CancellationToken ct)
```

## Configuration

### Application Settings

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=financecore;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "FinanceCore": {
    "FileIngestion": {
      "InputDirectory": "./data/input",
      "ProcessedDirectory": "./data/processed",
      "SupportedExtensions": [".csv", ".xlsx"]
    },
    "Reconciliation": {
      "AutoReconcileEnabled": true,
      "MaxDiscrepancyForAutoApproval": 1.00
    },
    "Processing": {
      "BatchSize": 1000,
      "MaxParallelJobs": 4
    }
  }
}
```

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| ASPNETCORE_ENVIRONMENT | Runtime environment | Development |
| ConnectionStrings__DefaultConnection | PostgreSQL connection | - |
| ConnectionStrings__Redis | Redis connection | localhost:6379 |

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Database Migrations

The initial schema is located at `database/migrations/V001__Initial_Schema.sql`. To apply manually:

```bash
psql -h localhost -U postgres -d financecore -f database/migrations/V001__Initial_Schema.sql
```

### Code Style

The project follows standard .NET conventions:

- PascalCase for public members
- camelCase for private fields with underscore prefix
- Async suffix for asynchronous methods
- XML documentation for public APIs

## License

This project is licensed under the MIT License. See the LICENSE file for details.

---

Developed by Gabriel Garcia Rodriguez

Full Stack Developer | .NET | Financial Systems
