# FinanceCore - Sistema de Conciliación y Análisis Financiero

![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)
![License](https://img.shields.io/badge/license-MIT-green)

Sistema ETL financiero de nivel empresarial para procesamiento, conciliación y análisis de transacciones bancarias.

## 📋 Tabla de Contenidos

- [Descripción](#descripción)
- [Arquitectura](#arquitectura)
- [Stack Tecnológico](#stack-tecnológico)
- [Instalación](#instalación)
- [Estructura del Proyecto](#estructura-del-proyecto)
- [Modelo de Datos](#modelo-de-datos)
- [Casos de Uso](#casos-de-uso)
- [Jobs de Background](#jobs-de-background)
- [API Endpoints](#api-endpoints)
- [Buenas Prácticas](#buenas-prácticas)
- [Mejoras Futuras](#mejoras-futuras)

## 📖 Descripción

FinanceCore es un sistema diseñado para:

- **Ingerir datos financieros** desde múltiples fuentes (APIs, archivos CSV/Excel, SFTP)
- **Procesar y transformar** transacciones con validaciones de integridad
- **Conciliar** movimientos contra fuentes externas
- **Generar reportes** y métricas financieras
- **Detectar anomalías** y posibles fraudes

### Reglas de Negocio Clave

```
┌─────────────────────────────────────────────────────────────────┐
│                    PRINCIPIOS FINANCIEROS                       │
├─────────────────────────────────────────────────────────────────┤
│ ✓ Partida Doble: Débito = Crédito siempre                      │
│ ✓ Inmutabilidad: Transacciones confirmadas nunca se modifican  │
│ ✓ Precisión: decimal(18,4), redondeo bancario                  │
│ ✓ Trazabilidad: Auditoría completa de cada operación           │
│ ✓ Idempotencia: Reprocesar no duplica transacciones            │
└─────────────────────────────────────────────────────────────────┘
```

## 🏗️ Arquitectura

El sistema implementa **Clean Architecture** con separación clara de responsabilidades:

```
┌─────────────────────────────────────────────────────────────────┐
│                      PRESENTATION LAYER                         │
│   REST API │ Hangfire Dashboard │ Health Checks │ Swagger       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      APPLICATION LAYER                          │
│     Use Cases │ Commands │ Queries │ MediatR Pipeline          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        DOMAIN LAYER                             │
│   Entities │ Value Objects │ Domain Services │ Events          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    INFRASTRUCTURE LAYER                         │
│ EF Core │ Dapper │ Hangfire │ Redis │ External APIs │ Files    │
└─────────────────────────────────────────────────────────────────┘
```

## 🛠️ Stack Tecnológico

| Categoría | Tecnología | Uso |
|-----------|-----------|-----|
| **Framework** | .NET 8 / ASP.NET Core | API y aplicación |
| **ORM** | Entity Framework Core 8 | Escrituras y migraciones |
| **Micro-ORM** | Dapper | Queries de alto rendimiento |
| **Base de Datos** | PostgreSQL 16 | Persistencia principal |
| **Caché** | Redis | Caché distribuido |
| **Jobs** | Hangfire | Procesamiento en background |
| **Logging** | Serilog | Logging estructurado |
| **Validación** | FluentValidation | Validación de requests |
| **Mediator** | MediatR | CQRS y pipeline |
| **Documentación** | Swagger/OpenAPI | API docs |

## 🚀 Instalación

### Prerrequisitos

- .NET 8 SDK
- Docker y Docker Compose
- PostgreSQL 16 (o usar Docker)
- Redis (o usar Docker)

### Pasos

1. **Clonar el repositorio**
```bash
git clone https://github.com/GabrielGarciaRodri/financecore.git
cd financecore
```

2. **Iniciar servicios con Docker**
```bash
docker-compose up -d
```

3. **Restaurar paquetes**
```bash
dotnet restore
```

4. **Aplicar migraciones**
```bash
cd src/FinanceCore.API
dotnet ef database update
```

5. **Ejecutar la aplicación**
```bash
dotnet run
```

### URLs de desarrollo

| Servicio | URL |
|----------|-----|
| API | http://localhost:5000 |
| Swagger | http://localhost:5000/swagger |
| Hangfire | http://localhost:5000/hangfire |
| Health | http://localhost:5000/health |
| pgAdmin | http://localhost:5050 |

## 📁 Estructura del Proyecto

```
FinanceCore/
├── src/
│   ├── FinanceCore.Domain/           # Núcleo del negocio
│   │   ├── Entities/                 # Entidades de dominio
│   │   ├── ValueObjects/             # Money, Currency, etc.
│   │   ├── Enums/                    # Enumeraciones
│   │   ├── Exceptions/               # Excepciones de dominio
│   │   ├── Events/                   # Eventos de dominio
│   │   └── Repositories/             # Interfaces de repositorios
│   │
│   ├── FinanceCore.Application/      # Casos de uso
│   │   ├── Common/                   # Behaviors, Models
│   │   ├── Transactions/             # Commands y Queries
│   │   ├── Reconciliation/           # Conciliación
│   │   └── Reports/                  # Reportes
│   │
│   ├── FinanceCore.Infrastructure/   # Implementaciones
│   │   ├── Persistence/              # EF Core, Dapper
│   │   ├── BackgroundJobs/           # Hangfire jobs
│   │   ├── ExternalServices/         # APIs externas
│   │   └── FileProcessing/           # Parsers CSV/Excel
│   │
│   └── FinanceCore.API/              # Presentación
│       ├── Controllers/              # REST controllers
│       ├── Middleware/               # Middlewares
│       └── Filters/                  # Action filters
│
├── tests/                            # Tests unitarios e integración
├── database/migrations/              # Scripts SQL
└── docker-compose.yml
```

## 💾 Modelo de Datos

### Entidades Principales

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│     Account      │────▶│   Transaction    │────▶│  Reconciliation  │
├──────────────────┤     ├──────────────────┤     ├──────────────────┤
│ account_number   │     │ external_id (UK) │     │ matched_count    │
│ currency_code    │     │ amount           │     │ unmatched_count  │
│ current_balance  │     │ status           │     │ discrepancy      │
│ available_balance│     │ value_date       │     │ status           │
│ version (CC)     │     │ hash (duplicates)│     └──────────────────┘
└──────────────────┘     └──────────────────┘
```

### Value Objects

- **Money**: Manejo preciso de valores monetarios con `decimal(18,4)`
- **Currency**: Monedas ISO 4217 con validación

## 📊 Casos de Uso

### 1. Ingesta de Transacciones

```csharp
// Comando para ingerir batch de transacciones
var command = new IngestTransactionsCommand
{
    Source = "BANCOLOMBIA_API",
    Transactions = transactions,
    FailOnFirstError = false
};

var result = await mediator.Send(command);
// Result: { Succeeded: 95, Failed: 3, Duplicates: 2 }
```

### 2. Conciliación Automática

```csharp
// Job programado diariamente
RecurringJob.AddOrUpdate<DailyReconciliationJob>(
    "daily-reconciliation",
    job => job.ReconcileAllAccountsAsync(DateTime.Today.AddDays(-1)),
    "0 6 * * *"); // 6 AM todos los días
```

### 3. Cierre Diario

```csharp
// Calcula balances, detecta descuadres
var closeJob = new DailyCloseJob();
await closeJob.ExecuteDailyCloseAsync(DateOnly.FromDateTime(DateTime.Today));
```

## ⚙️ Jobs de Background

| Job | Schedule | Descripción |
|-----|----------|-------------|
| TransactionIngestion | */15 * * * * | Procesa archivos pendientes cada 15 min |
| DailyClose | 59 23 * * * | Cierre diario a las 23:59 |
| ExchangeRateUpdate | 0 8-18 * * 1-5 | Actualiza tipos de cambio cada hora |
| DataCleanup | 0 3 * * 0 | Limpieza semanal de datos antiguos |

### Configuración de Reintentos

```csharp
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[Queue("critical")]
public async Task ExecuteDailyCloseAsync(DateOnly closeDate) { ... }
```

## 🔌 API Endpoints

### Transacciones

```
POST   /api/transactions/ingest           # Ingerir batch
GET    /api/transactions/{id}             # Obtener por ID
GET    /api/transactions/search           # Búsqueda avanzada
GET    /api/transactions/accounts/{id}/summary  # Resumen por cuenta
```

### Cuentas

```
GET    /api/accounts                      # Listar cuentas
GET    /api/accounts/{id}                 # Obtener cuenta
GET    /api/accounts/{id}/balances        # Historial de saldos
```

### Conciliación

```
POST   /api/reconciliation/run            # Ejecutar conciliación
GET    /api/reconciliation/{id}           # Estado de conciliación
GET    /api/reconciliation/discrepancies  # Listar descuadres
```

## ✅ Buenas Prácticas Implementadas

### 1. Manejo de Dinero

```csharp
// ✅ CORRECTO: Usar decimal con redondeo bancario
public Money Add(Money other)
{
    EnsureSameCurrency(other);
    return new Money(
        Math.Round(Amount + other.Amount, 4, MidpointRounding.ToEven),
        Currency);
}

// ❌ INCORRECTO: Nunca usar float/double para dinero
// float balance = 1000.50f; // NO!
```

### 2. Idempotencia

```csharp
// Verificar duplicados antes de insertar
var existing = await _repo.GetByExternalIdAsync(externalId, source);
if (existing != null)
    return Result.Success(existing.Id); // Retornar existente, no error
```

### 3. Concurrencia Optimista

```csharp
// Account tiene versión para control de concurrencia
builder.Property(a => a.Version)
    .IsConcurrencyToken()
    .HasDefaultValue(1);

// Al actualizar, EF Core valida la versión
account.Version++; // Incrementar en cada cambio
```

### 4. Auditoría Completa

```csharp
// Trigger automático para audit logs
public override async Task<int> SaveChangesAsync(CancellationToken ct)
{
    foreach (var entry in ChangeTracker.Entries<BaseEntity>())
    {
        if (entry.State == EntityState.Modified)
        {
            // Registrar cambios en audit_logs
            await LogAuditAsync(entry);
        }
    }
    return await base.SaveChangesAsync(ct);
}
```

### 5. CQRS con Dapper y EF Core

```csharp
// Escrituras: EF Core (tracking, validaciones)
_context.Transactions.Add(transaction);
await _context.SaveChangesAsync();

// Lecturas complejas: Dapper (rendimiento)
const string sql = "SELECT ... FROM transactions WHERE ...";
return await connection.QueryAsync<TransactionDto>(sql, parameters);
```

## 🚀 Mejoras Futuras

### Corto Plazo
- [ ] Implementar Event Sourcing para transacciones críticas
- [ ] Agregar tests de integración con Testcontainers
- [ ] Implementar rate limiting en API

### Mediano Plazo
- [ ] Migrar a arquitectura de microservicios
- [ ] Implementar SAGA pattern para transacciones distribuidas
- [ ] Agregar procesamiento con Apache Kafka

### Largo Plazo
- [ ] Machine Learning para detección de fraudes
- [ ] Real-time analytics con Apache Spark
- [ ] Multi-tenancy para SaaS

## ⚠️ Errores Comunes y Cómo Evitarlos

| Error | Consecuencia | Solución |
|-------|--------------|----------|
| Usar `float` para dinero | Errores de precisión | Siempre `decimal(18,4)` |
| No validar idempotencia | Transacciones duplicadas | Verificar `external_id` |
| Ignorar zonas horarias | Fechas incorrectas | Usar `DateTimeOffset` |
| Modificar transacciones | Pérdida de auditoría | Crear ajustes nuevos |
| No manejar concurrencia | Race conditions | Optimistic locking |

## 📝 Licencia

MIT License - ver [LICENSE](LICENSE) para detalles.

---

**Desarrollado por Gabriel** - Full Stack Developer  
*Proyecto de portafolio para roles .NET en el sector financiero*