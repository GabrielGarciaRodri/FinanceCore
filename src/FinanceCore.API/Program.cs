using System.Reflection;
using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using FinanceCore.API.Authentication;
using FinanceCore.API.Logging;
using FinanceCore.Application.Common.Behaviors;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Domain.Repositories;
using FinanceCore.Infrastructure.Alerting;
using FinanceCore.Infrastructure.BackgroundJobs.Configuration;
using FinanceCore.Infrastructure.Exports;
using FinanceCore.Infrastructure.FileIngestion;
using FinanceCore.Infrastructure.Identity;
using FinanceCore.Infrastructure.Observability;
using FinanceCore.Infrastructure.Persistence;
using FinanceCore.Infrastructure.Persistence.Context;
using FinanceCore.Infrastructure.Persistence.Repositories;
using FinanceCore.Infrastructure.Reconciliations;
using FinanceCore.Infrastructure.Services;
using FinanceCore.Infrastructure.BackgroundJobs.Jobs;
using FinanceCore.Infrastructure.ExchangeRates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text;

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
    .Enrich.With<SensitiveDataEnricher>()
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
    var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(
        configuration.GetConnectionString("DefaultConnection"));
    dataSourceBuilder.MapEnum<FinanceCore.Domain.Enums.TransactionType>("transaction_type", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
    dataSourceBuilder.MapEnum<FinanceCore.Domain.Enums.TransactionStatus>("transaction_status", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
    dataSourceBuilder.MapEnum<FinanceCore.Domain.Enums.AccountType>("account_type", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
    dataSourceBuilder.MapEnum<FinanceCore.Domain.Enums.ReconciliationStatus>("reconciliation_status", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
    dataSourceBuilder.MapEnum<FinanceCore.Domain.Enums.SourceType>("source_type", new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator());
    var dataSource = dataSourceBuilder.Build();

    services.AddDbContext<FinanceCoreDbContext>(options =>
    {
        options.UseNpgsql(
            dataSource,
            npgsqlOptions =>
            {
                npgsqlOptions.CommandTimeout(30);
            });

        // En desarrollo, habilitar logging sensible solo si se solicita explícitamente
        var enableSensitiveLogging = configuration.GetValue<bool>("Logging:EnableSensitiveDataLogging");
        if (builder.Environment.IsDevelopment() && enableSensitiveLogging)
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
    services.AddScoped<IAccountRepository, AccountRepository>();
    services.AddScoped<IDailyBalanceRepository, DailyBalanceRepository>();
    services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
    services.AddScoped<IReconciliationRepository, ReconciliationRepository>();
    services.AddScoped<IReconciliationSourceProfileRepository, ReconciliationSourceProfileRepository>();
    services.AddScoped<IUnitOfWork, UnitOfWork>();
    services.Configure<FileIngestionOptions>(configuration.GetSection("FinanceCore:FileIngestion"));
    services.Configure<ExchangeRateOptions>(configuration.GetSection(ExchangeRateOptions.SectionName));
    services.Configure<FinanceCore.Infrastructure.Reconciliations.ReconciliationOptions>(
        configuration.GetSection(FinanceCore.Infrastructure.Reconciliations.ReconciliationOptions.SectionName));
    services.AddScoped<IFileIngestionService, FileIngestionService>();
    services.AddScoped<IUploadTransactionParser, UploadTransactionParser>();
    services.AddHttpClient<IExchangeRateProvider, ExchangeRateApiProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(
                configuration.GetValue<int>("FinanceCore:ExchangeRates:TimeoutSeconds", 10));
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts =
                configuration.GetValue<int>("FinanceCore:ExchangeRates:RetryAttempts", 3);
            options.Retry.Delay = TimeSpan.FromSeconds(2);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(60);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        });
    services.AddScoped<IReconciliationEngine, ReconciliationEngine>();
    services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

    // ─────────────────────────────────────────────────────────────────────────────
    // Exports
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddScoped<ITransactionExportService, TransactionExportService>();
    services.AddScoped<IReconciliationExportService, ReconciliationExportService>();

    // ─────────────────────────────────────────────────────────────────────────────
    // Alerting (logging sink siempre activo; webhook opcional via config)
    // ─────────────────────────────────────────────────────────────────────────────
    services.Configure<AlertingOptions>(configuration.GetSection(AlertingOptions.SectionName));
    services.AddScoped<IAlertSink, LoggingAlertSink>();
    services.AddHttpClient<WebhookAlertSink>();
    services.AddScoped<IAlertSink>(sp => sp.GetRequiredService<WebhookAlertSink>());
    services.AddScoped<IAlertDispatcher, AlertDispatcher>();

    // ─────────────────────────────────────────────────────────────────────────────
    // MediatR + Pipeline Behaviors
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
        cfg.RegisterServicesFromAssembly(
            Assembly.Load("FinanceCore.Application"));
        cfg.RegisterServicesFromAssembly(
            Assembly.Load("FinanceCore.Infrastructure"));

        // Pipeline behaviors (orden importa!)
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
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
    // Caché (IMemoryCache siempre disponible; IDistributedCache via Redis o memoria)
    // ─────────────────────────────────────────────────────────────────────────────
    
    services.AddMemoryCache();

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
        services.AddDistributedMemoryCache();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // OpenTelemetry - métricas + tracing
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddSingleton<IngestionMetrics>();
    services.AddSingleton<ReconciliationMetrics>();
    services.AddSingleton<ExchangeRateMetrics>();

    var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    var resourceBuilder = ResourceBuilder.CreateDefault()
        .AddService(serviceName: "FinanceCore.API", serviceVersion: serviceVersion)
        .AddAttributes(new KeyValuePair<string, object>[]
        {
            new("deployment.environment", builder.Environment.EnvironmentName)
        });

    var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

    services.AddOpenTelemetry()
        .ConfigureResource(rb => rb.AddService(serviceName: "FinanceCore.API", serviceVersion: serviceVersion))
        .WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(FinanceCoreTelemetry.MeterName)
                .AddPrometheusExporter();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                metrics.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
            }
        })
        .WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation(opts =>
                {
                    opts.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health") &&
                        !ctx.Request.Path.StartsWithSegments("/metrics");
                })
                .AddHttpClientInstrumentation()
                .AddSource(FinanceCoreTelemetry.ActivitySourceName);

            if (builder.Environment.IsDevelopment())
                tracing.AddConsoleExporter();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                tracing.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
        });

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

    services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problemDetails = new ValidationProblemDetails(context.ModelState)
            {
                Title = "Validación fallida",
                Status = StatusCodes.Status400BadRequest
            };

            return new BadRequestObjectResult(problemDetails);
        };
    });

    // ─────────────────────────────────────────────────────────────────────────────
    // Rate limiting — global por IP + política estricta para /api/auth/*
    // ─────────────────────────────────────────────────────────────────────────────
    var rateLimiting = configuration
        .GetSection(FinanceCore.API.RateLimiting.RateLimitingOptions.SectionName)
        .Get<FinanceCore.API.RateLimiting.RateLimitingOptions>() ?? new();

    // Render/Vercel: la API corre detrás de un proxy. Sin forwarded headers,
    // RemoteIpAddress es siempre la IP del proxy y el limiter por IP colapsa
    // en un bucket único para todos los visitantes.
    services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        // PaaS: la IP del proxy no se conoce de antemano.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    if (rateLimiting.Enabled)
    {
        static string ClientIp(HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(
                        System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                }

                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Demasiadas solicitudes",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = "Se superó el límite de solicitudes. Intentá de nuevo en unos segundos."
                }, ct);
            };

            limiter.GlobalLimiter =
                System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                {
                    // /health y /metrics exentos: probes de Render + Prometheus.
                    if (ctx.Request.Path.StartsWithSegments("/health") ||
                        ctx.Request.Path.StartsWithSegments("/metrics"))
                    {
                        return System.Threading.RateLimiting.RateLimitPartition
                            .GetNoLimiter("exempt");
                    }

                    return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                        ClientIp(ctx),
                        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                        {
                            PermitLimit = rateLimiting.GlobalPermitLimit,
                            Window = TimeSpan.FromSeconds(rateLimiting.GlobalWindowSeconds),
                            QueueLimit = 0
                        });
                });

            limiter.AddPolicy("auth", ctx =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    ClientIp(ctx),
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimiting.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimiting.AuthWindowSeconds),
                        QueueLimit = 0
                    }));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Swagger/OpenAPI
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen(options =>
    {
        // Respeta los nullable annotations del C# (#nullable enable) al
        // generar los schemas. Sin esto, todas las propiedades salen como
        // opcionales y nullable en el JSON, contaminando los types TypeScript
        // generados por openapi-typescript.
        options.SupportNonNullableReferenceTypes();
        options.UseAllOfToExtendReferenceSchemas();

        // Marca como `required` toda propiedad no-nullable. Sin esto las
        // propiedades quedan opcionales en el OpenAPI aunque el tipo C# las
        // declare non-nullable, generando types TS con `?` en todo.
        options.SchemaFilter<FinanceCore.API.Swagger.RequiredNonNullableSchemaFilter>();

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

        // API Key authentication in Swagger
        options.AddSecurityDefinition(ApiKeyDefaults.AuthenticationScheme, new OpenApiSecurityScheme
        {
            Description = "API Key authentication (header only). Provide your API key in the X-Api-Key header.",
            Name = ApiKeyDefaults.HeaderName,
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = ApiKeyDefaults.AuthenticationScheme
        });

        // JWT Bearer
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Bearer token. Obtainable via POST /api/auth/login. Format: 'Bearer {token}'",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            },
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = ApiKeyDefaults.AuthenticationScheme
                    }
                },
                Array.Empty<string>()
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
    // Authentication & Authorization
    // ─────────────────────────────────────────────────────────────────────────────
    // Identity (ASP.NET Core Identity sobre el mismo DbContext).
    services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = false;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;

            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<FinanceCoreDbContext>()
        .AddDefaultTokenProviders();

    services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
    services.Configure<IdentitySeedOptions>(configuration.GetSection(IdentitySeedOptions.SectionName));
    services.AddScoped<IJwtTokenService, JwtTokenService>();
    services.AddScoped<IIdentitySeeder, IdentitySeeder>();

    var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

    // Auth multi-scheme: el ForwardDefaultSelector elige por la presencia del header
    // Authorization: Bearer ... vs X-Api-Key. [Authorize] sin AuthenticationSchemes
    // funciona con ambos automáticamente.
    // OJO: AddIdentity (más arriba) sobrescribe DefaultAuthenticateScheme y
    // DefaultChallengeScheme apuntándolos a Identity.Application (cookies).
    // Tenemos que reescribir TODOS los defaults de auth/challenge/forbid acá
    // para que el flujo [Authorize] use MultiScheme (JWT o ApiKey) y no la
    // cookie scheme de Identity, que para esta app es ruido — Identity la
    // usamos sólo para hashing/UserManager/RoleManager, no para login cookie.
    services.AddAuthentication(options =>
        {
            options.DefaultScheme = "MultiScheme";
            options.DefaultAuthenticateScheme = "MultiScheme";
            options.DefaultChallengeScheme = "MultiScheme";
            options.DefaultForbidScheme = "MultiScheme";
        })
        .AddPolicyScheme("MultiScheme", "JWT or ApiKey", options =>
        {
            options.ForwardDefaultSelector = ctx =>
            {
                var authHeader = ctx.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authHeader) &&
                    authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                return ApiKeyDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
            {
                Log.Warning("JWT SigningKey ausente o demasiado corta. /api/auth/* fallará hasta configurarla.");
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(
                        string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
                            ? new string('0', 64)        // placeholder: ningún token va a validar
                            : jwtOptions.SigningKey)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        })
        .AddApiKey(options =>
        {
            var apiKeysSection = configuration.GetSection("Authentication:ApiKeys");
            if (apiKeysSection.Exists())
            {
                foreach (var keySection in apiKeysSection.GetChildren())
                {
                    var key = keySection.Key;
                    var clientName = keySection["ClientName"] ?? "Unknown";
                    var roles = keySection.GetSection("Roles").Get<string[]>() ?? Array.Empty<string>();
                    options.ApiKeys[key] = new ApiKeyConfig
                    {
                        ClientName = clientName,
                        Roles = roles
                    };
                }
            }
        });

    services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin", "FinanceAdmin"));
        options.AddPolicy("ReaderOrAbove", policy => policy.RequireRole("Admin", "FinanceAdmin", "Reader"));
    });

    // ─────────────────────────────────────────────────────────────────────────────
    // CORS
    // ─────────────────────────────────────────────────────────────────────────────
    services.AddCors(options =>
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
            ?? Array.Empty<string>();

        options.AddPolicy("Development", policy =>
        {
            // Frontend de desarrollo + cualquier dev custom configurado.
            var devOrigins = new[] { "http://localhost:3000", "http://localhost:5173" }
                .Concat(allowedOrigins)
                .Distinct()
                .ToArray();

            policy.WithOrigins(devOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });

        options.AddPolicy("Production", policy =>
        {
            if (allowedOrigins.Length == 0)
            {
                // Sin orígenes configurados, negar CORS explícitamente.
                policy.SetIsOriginAllowed(_ => false);
                return;
            }

            policy.WithOrigins(allowedOrigins)
                  .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                  .WithHeaders("Content-Type", "Accept", "Authorization", ApiKeyDefaults.HeaderName)
                  .AllowCredentials();
        });
    });

    // ═══════════════════════════════════════════════════════════════════════════
    // BUILD APPLICATION
    // ═══════════════════════════════════════════════════════════════════════════
    var app = builder.Build();

    // Primero en el pipeline: restaura la IP real del cliente desde
    // X-Forwarded-For antes de que cualquier middleware (rate limiter,
    // logging) la use.
    app.UseForwardedHeaders();

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
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

            if (exception is ValidationException validationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";

                var errors = validationException.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                var validationProblem = new ValidationProblemDetails(errors)
                {
                    Title = "Validación fallida",
                    Status = StatusCodes.Status400BadRequest
                };

                await context.Response.WriteAsJsonAsync(validationProblem);
                return;
            }

            if (exception is TransactionValidationException transactionValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";

                var errors = new Dictionary<string, string[]>
                {
                    { "Transaction", transactionValidationException.ValidationErrors.ToArray() }
                };

                var validationProblem = new ValidationProblemDetails(errors)
                {
                    Title = "Validación fallida",
                    Status = StatusCodes.Status400BadRequest
                };

                await context.Response.WriteAsJsonAsync(validationProblem);
                return;
            }

            Log.Error(exception, "Unhandled exception");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Error interno del servidor",
                Status = StatusCodes.Status500InternalServerError,
                Detail = app.Environment.IsDevelopment() ? exception?.Message : null
            });
        });
    });

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
    if (rateLimiting.Enabled)
    {
        app.UseRateLimiter();
    }
    app.UseAuthentication();
    app.UseAuthorization();

    // ─────────────────────────────────────────────────────────────────────────────
    // Endpoints
    // ─────────────────────────────────────────────────────────────────────────────
    app.MapControllers();

    // Prometheus scraping endpoint (anonymous; idealmente exponer sólo a la red interna)
    app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

    // Health checks (anonymous access)
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
    }).AllowAnonymous();

    // Hangfire Dashboard
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireDashboardAuthorizationFilter() },
        DashboardTitle = "FinanceCore - Jobs",
        DisplayStorageConnectionString = false
    });

    // Configurar jobs recurrentes
    HangfireJobsConfiguration.ConfigureRecurringJobs();

    // Identity: seed inicial (idempotente).
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IIdentitySeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Identity seed falló. La aplicación continúa pero podría no haber usuarios.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INICIALIZACIÓN Y EJECUCIÓN
    // ═══════════════════════════════════════════════════════════════════════════

    Log.Information("Aplicación iniciada. Escuchando en: {Urls}",
        string.Join(", ", app.Urls));
    Log.Information("Swagger disponible en: /swagger");
    Log.Information("Hangfire Dashboard disponible en: /hangfire");
    Log.Information("Health checks disponibles en: /health");
    Log.Information("Prometheus metrics disponibles en: /metrics");

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
