using System.Reflection;
using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using FinanceCore.Application.Common.Behaviors;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.BackgroundJobs.Configuration;
using FinanceCore.Infrastructure.Persistence.Context;
using FinanceCore.Infrastructure.Persistence.Repositories;

// ═══════════════════════════════════════════════════════════════════════════════
// CONFIGURACIÓN DE SERILOG (antes de construir el host)
// ═══════════════════════════════════════════════════════════════════════════════
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Hangfire", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "FinanceCore")
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/financecore-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("═══════════════════════════════════════════════════════════════");
    Log.Information("  FINANCECORE - Sistema de Conciliación y Análisis Financiero  ");
    Log.Information("═══════════════════════════════════════════════════════════════");
    Log.Information("Iniciando aplicación...");

    var builder = WebApplication.CreateBuilder(args);

    // Usar Serilog
    builder.Host.UseSerilog();

    // ═══════════════════════════════════════════════════════════════════════════
    // CONFIGURACIÓN DE SERVICIOS
    // ═══════════════════════════════════════════════════════════════════════════

    var services = builder.Services;
    var configuration = builder.Configuration;

    // ─────────────────────────────────────────────────────────────────────────────
    // Base de datos - PostgreSQL con EF Core
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddDbContext<FinanceCoreDbContext>(options =>
    {
        options.UseNpgsql(
            configuration.GetConnectionString("DefaultConnection"),
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(FinanceCoreDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
                npgsqlOptions.CommandTimeout(30);
            });

        // En desarrollo, habilitar logging de queries
        if (builder.Environment.IsDevelopment())
        {
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        }
    });

    // ─────────────────────────────────────────────────────────────────────────────
    // Dapper - para queries de alto rendimiento
    // ─────────────────────────────────────────────────────────────────────────────
    Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

    // ─────────────────────────────────────────────────────────────────────────────
    // Repositorios y Unit of Work
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddScoped<ITransactionRepository, TransactionRepository>();
    // services.AddScoped<IAccountRepository, AccountRepository>();
    // services.AddScoped<IDailyBalanceRepository, DailyBalanceRepository>();
    // services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
    // services.AddScoped<IUnitOfWork, UnitOfWork>();

    // ─────────────────────────────────────────────────────────────────────────────
    // MediatR + Pipeline Behaviors
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
        cfg.RegisterServicesFromAssembly(
            Assembly.Load("FinanceCore.Application"));
        
        // Pipeline behaviors (orden importa!)
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        // cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
    });

    // ─────────────────────────────────────────────────────────────────────────────
    // FluentValidation
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddValidatorsFromAssembly(Assembly.Load("FinanceCore.Application"));

    // ─────────────────────────────────────────────────────────────────────────────
    // Hangfire
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddHangfireServices(configuration);

    // ─────────────────────────────────────────────────────────────────────────────
    // Caché (Redis o Memory)
    // ─────────────────────────────────────────────────────────────────────────────
    var redisConnection = configuration.GetConnectionString("Redis");
    if (!string.IsNullOrEmpty(redisConnection))
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "FinanceCore:";
        });
    }
    else
    {
        services.AddMemoryCache();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Health Checks
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddHealthChecks()
        .AddNpgSql(
            configuration.GetConnectionString("DefaultConnection")!,
            name: "postgresql",
            tags: new[] { "db", "sql", "postgresql" })
        .AddHangfire(
            options => options.MinimumAvailableServers = 1,
            name: "hangfire",
            tags: new[] { "hangfire", "jobs" });

    // ─────────────────────────────────────────────────────────────────────────────
    // API Controllers
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = 
                System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.WriteIndented = true;
            options.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    // ─────────────────────────────────────────────────────────────────────────────
    // Swagger/OpenAPI
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "FinanceCore API",
            Version = "v1",
            Description = "Sistema de Conciliación y Análisis Financiero - API REST",
            Contact = new OpenApiContact
            {
                Name = "Gabriel - Full Stack Developer",
                Email = "gabriel@example.com"
            }
        });

        // Incluir comentarios XML
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    });

    // ─────────────────────────────────────────────────────────────────────────────
    // CORS
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddCors(options =>
    {
        options.AddPolicy("Development", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });

        options.AddPolicy("Production", policy =>
        {
            policy.WithOrigins(
                    configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
                    ?? Array.Empty<string>())
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    // ═══════════════════════════════════════════════════════════════════════════
    // BUILD APPLICATION
    // ═══════════════════════════════════════════════════════════════════════════
    var app = builder.Build();

    // ─────────────────────────────────────────────────────────────────────────────
    // Middleware Pipeline
    // ─────────────────────────────────────────────────────────────────────────────
    
    // Request logging con Serilog
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = 
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        };
    });

    // Manejo global de errores
    app.UseExceptionHandler("/error");

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "FinanceCore API v1");
            options.RoutePrefix = "swagger";
        });
        app.UseCors("Development");
    }
    else
    {
        app.UseHsts();
        app.UseCors("Production");
    }

    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthorization();

    // ─────────────────────────────────────────────────────────────────────────────
    // Endpoints
    // ─────────────────────────────────────────────────────────────────────────────
    app.MapControllers();

    // Health checks
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var result = new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds
                }),
                totalDuration = report.TotalDuration.TotalMilliseconds
            };
            await context.Response.WriteAsJsonAsync(result);
        }
    });

    // Hangfire Dashboard
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireDashboardAuthorizationFilter() },
        DashboardTitle = "FinanceCore - Jobs",
        DisplayStorageConnectionString = false
    });

    // Configurar jobs recurrentes
    HangfireJobsConfiguration.ConfigureRecurringJobs();

    // Endpoint de error
    app.Map("/error", (HttpContext context) =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        Log.Error(exception, "Unhandled exception");
        
        return Results.Problem(
            title: "Error interno del servidor",
            statusCode: 500,
            detail: app.Environment.IsDevelopment() ? exception?.Message : null);
    });

    // ═══════════════════════════════════════════════════════════════════════════
    // INICIALIZACIÓN Y EJECUCIÓN
    // ═══════════════════════════════════════════════════════════════════════════

    // Aplicar migraciones automáticamente en desarrollo
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinanceCoreDbContext>();
        
        Log.Information("Aplicando migraciones de base de datos...");
        await dbContext.Database.MigrateAsync();
        Log.Information("Migraciones aplicadas correctamente.");
    }

    Log.Information("Aplicación iniciada. Escuchando en: {Urls}", 
        string.Join(", ", app.Urls));
    Log.Information("Swagger disponible en: /swagger");
    Log.Information("Hangfire Dashboard disponible en: /hangfire");
    Log.Information("Health checks disponibles en: /health");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación falló al iniciar");
    throw;
}
finally
{
    Log.Information("Aplicación finalizada.");
    await Log.CloseAndFlushAsync();
}
