using System.Net;
using System.Threading.RateLimiting;
using MedicalPortal.Core.Domain;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.DistributedLock;
using MedicalPortal.Infrastructure.Fhir;
using MedicalPortal.Infrastructure.Hms;
using MedicalPortal.Infrastructure.Logging;
using MedicalPortal.Infrastructure.Notifications;
using MedicalPortal.Infrastructure.Observability;
using MedicalPortal.Infrastructure.Payments;
using MedicalPortal.Infrastructure.Security;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using MedicalPortal.Web.Hubs;
using MedicalPortal.Web.Middleware;
using MedicalPortal.Web.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Enable legacy timestamp behavior for full PostgreSQL compatibility across timezones
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Register Structured-Logging PHI Scrubber
builder.Logging.AddPhiScrubber();

// Forwarded Headers: Honored strictly and exclusively from trusted proxies and networks
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();

    // Default trusted networks: Loopback (IPv4 127.0.0.0/8 and IPv6 ::1/128)
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.IPv6Loopback, 128));

    // Parse configured trusted proxies (e.g. FORWARDED_HEADERS_TRUSTED_PROXIES or ForwardedHeaders:TrustedProxies)
    var trustedProxiesConfig = builder.Configuration["ForwardedHeaders:TrustedProxies"]
        ?? builder.Configuration["FORWARDED_HEADERS_TRUSTED_PROXIES"]
        ?? builder.Configuration["TRUSTED_PROXIES"];

    if (!string.IsNullOrWhiteSpace(trustedProxiesConfig))
    {
        foreach (var proxy in trustedProxiesConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    }

    // Parse configured trusted networks/CIDRs (e.g. FORWARDED_HEADERS_TRUSTED_NETWORKS or ForwardedHeaders:TrustedNetworks)
    var trustedNetworksConfig = builder.Configuration["ForwardedHeaders:TrustedNetworks"]
        ?? builder.Configuration["FORWARDED_HEADERS_TRUSTED_NETWORKS"]
        ?? builder.Configuration["TRUSTED_NETWORKS"];

    if (!string.IsNullOrWhiteSpace(trustedNetworksConfig))
    {
        foreach (var network in trustedNetworksConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = network.Split('/', 2);
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var netIp) && int.TryParse(parts[1], out var prefix))
            {
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(netIp, prefix));
            }
            else if (IPAddress.TryParse(network, out var singleIp))
            {
                options.KnownProxies.Add(singleIp);
            }
        }
    }
});

// HTTP Client & Memory Cache for Public IP & Geocoding
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("PublicIpClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.Add("User-Agent", "MedicalPortal/1.0 (PublicIpResolver)");
});
builder.Services.AddMemoryCache();

// SignalR Real-Time Collaboration (In-memory single instance, or Redis backplane for clustered scale-out)
var signalRBuilder = builder.Services.AddSignalR();
var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration["REDIS_CONNECTION_STRING"]
    ?? builder.Configuration["SignalR:Redis:ConnectionString"];

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("MedicalPortal.SignalR");
    });
}

builder.Services.AddSingleton<IPortalNotificationService, SignalRNotificationService>();

// Validate JWT secret early at startup (fail fast if missing or shorter than 32 bytes)
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? builder.Configuration["JWT_SECRET"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException(
        "Startup failed: JWT secret is not configured. Set 'Jwt:Secret' or 'JWT_SECRET' in configuration or environment variables.");
}

if (System.Text.Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException(
        "Startup failed: JWT secret must be at least 32 bytes (256 bits) for HMAC-SHA256 security.");
}

if (builder.Environment.IsProduction())
{
    if (jwtSecret.Contains("ThisIsAClusterSecretKey") ||
        jwtSecret.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
        jwtSecret.Contains("ChangeMe", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Production startup failed: Default or placeholder JWT secret is strictly prohibited in production.");
    }
}

// Database configuration (PostgreSQL or SQLite fallback)
var rawConnectionString = builder.Configuration["DATABASE_URL"]
                          ?? builder.Configuration["POSTGRES_CONNECTION_STRING"]
                          ?? builder.Configuration.GetConnectionString("DefaultConnection");

var isPostgres = PostgresConnectionHelper.IsPostgres(rawConnectionString);

if (builder.Environment.IsProduction() && !isPostgres)
{
    throw new InvalidOperationException(
        "Production environment requires PostgreSQL. Please configure 'DATABASE_URL' or a PostgreSQL connection string instead of falling back to SQLite.");
}

if (isPostgres)
{
    var npgsqlConnection = PostgresConnectionHelper.ToNpgsqlConnectionString(rawConnectionString!);
    builder.Services.AddDbContext<MedicalPortalDbContext>(options =>
        options.UseNpgsql(npgsqlConnection, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null);
        }));
}
else
{
    var dbPath = builder.Configuration["DB_PATH"] ?? "medical_portal.db";
    var sqliteConnection = !string.IsNullOrWhiteSpace(rawConnectionString) && rawConnectionString.Contains("Data Source=")
        ? rawConnectionString
        : $"Data Source={dbPath}";

    builder.Services.AddDbContext<MedicalPortalDbContext>(options =>
        options.UseSqlite(sqliteConnection));
}

// Enterprise Dependency Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready", "db" })
    .AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready", "cache" })
    .AddCheck<PaymentGatewayHealthCheck>("payment_gateway", tags: new[] { "ready", "payment" })
    .AddCheck<HmsHealthCheck>("hms", tags: new[] { "ready", "ehr" })
    .AddCheck<NotificationSenderHealthCheck>("notifications", tags: new[] { "ready", "messaging" });

// OpenTelemetry Observability (Traces, Metrics, Runtime Instrumentation)
var otelServiceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? MedicalPortalDiagnostics.ServiceName;
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: otelServiceName, serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["host.name"] = Environment.MachineName
        }))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(MedicalPortalDiagnostics.ActivitySourceName)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = httpContext => !httpContext.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
        }
        else if (builder.Configuration.GetValue<bool>("OTEL_CONSOLE_EXPORTER"))
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(MedicalPortalDiagnostics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
        }
        else if (builder.Configuration.GetValue<bool>("OTEL_CONSOLE_EXPORTER"))
        {
            metrics.AddConsoleExporter();
        }
    });

// Enterprise Rate Limiting (Brute-force protection & API quotas)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict rate limiter for Authentication & Sensitive operations (15 requests per minute per IP per instance in Production)
    var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:AuthPolicyPermitLimit") 
        ?? (builder.Environment.IsDevelopment() ? 300 : 15);

    options.AddPolicy("AuthPolicy", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Strict rate limiter for Share Code Redemption (brute-force defense: 10 requests per minute per IP per instance in Production)
    var sharePermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:ShareRedemptionPolicyPermitLimit") 
        ?? (builder.Environment.IsDevelopment() ? 300 : 10);

    options.AddPolicy("ShareRedemptionPolicy", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = sharePermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // General API rate limiter (150 requests per minute per IP per instance)
    var generalPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:GeneralApiPermitLimit") ?? 150;

    options.AddPolicy("GeneralApiPolicy", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = generalPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 10
        });
    });
});

// Core & Infrastructure Services
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddSingleton<IPhiScrubber, PhiScrubber>();
builder.Services.AddSingleton<IKeyProvider, ConfigurationKeyProvider>();
builder.Services.AddSingleton<IFieldEncryptionService, AesGcmFieldEncryptionService>();
builder.Services.AddSingleton<IPublicIpService, PublicIpService>();
builder.Services.AddScoped<IAccessControlService, AccessControlService>();
builder.Services.AddSingleton<IAuditBatchQueue, AuditBatchQueue>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ISnapshotService, SnapshotService>();
builder.Services.AddScoped<IPlacesService, PlacesService>();
builder.Services.AddSingleton<IGeocodingService, GeocodingService>();
builder.Services.AddScoped<IFhirService, FhirService>();

// Clinical Safety Knowledge Bases (Replaceable with licensed clinical pharmacology providers)
var useDemoClinicalKb = builder.Configuration.GetValue<bool>("Clinical:UseDemoKnowledgeBase", false);
if (builder.Environment.IsProduction())
{
    if (useDemoClinicalKb)
    {
        throw new InvalidOperationException(
            "Production startup failed: Demo clinical knowledge bases are strictly prohibited in production.");
    }

    var rxNormKb = new LicensedRxNormKnowledgeBase("2026.10", "National Clinical Drug Authority / RxNorm");
    builder.Services.AddSingleton<IInteractionKnowledgeBase>(rxNormKb);
    builder.Services.AddSingleton<IAllergyKnowledgeBase>(rxNormKb);
}
else
{
    builder.Services.AddSingleton<IInteractionKnowledgeBase, DemoInteractionKnowledgeBase>();
    builder.Services.AddSingleton<IAllergyKnowledgeBase, DemoAllergyKnowledgeBase>();
}

// Multi-Instance Distributed Locking (PostgreSQL session advisory locks across cluster, in-memory fallback for dev)
builder.Services.AddSingleton<IDistributedLockService, PostgreSqlAdvisoryLockService>();

// Payment Gateway (Stripe with hosted fields / PCI SAQ A, or Fake gateway for tests and dev)
var stripeSecretKey = builder.Configuration["Stripe:SecretKey"] ?? builder.Configuration["STRIPE_SECRET_KEY"];
var useFakeGateway = builder.Configuration.GetValue<bool>("Stripe:UseFake") ||
                      builder.Configuration.GetValue<bool>("STRIPE_USE_FAKE");

if (builder.Environment.IsProduction())
{
    if (useFakeGateway)
    {
        throw new InvalidOperationException(
            "Production startup failed: Fake payment gateway is strictly prohibited in production.");
    }

    if (string.IsNullOrWhiteSpace(stripeSecretKey) ||
        stripeSecretKey.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
        stripeSecretKey.Contains("dummy", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Production startup failed: Valid production Stripe secret key is required.");
    }

    builder.Services.AddHttpClient<IPaymentGateway, StripePaymentGateway>(client =>
    {
        client.BaseAddress = new Uri("https://api.stripe.com/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}
else
{
    if (useFakeGateway || string.IsNullOrWhiteSpace(stripeSecretKey))
    {
        builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
    }
    else
    {
        builder.Services.AddHttpClient<IPaymentGateway, StripePaymentGateway>(client =>
        {
            client.BaseAddress = new Uri("https://api.stripe.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }
}

// Notification Delivery (BAA-Capable Email: SendGrid/AWS SES/SMTP; BAA-Capable SMS: Twilio/AWS SNS; Fake for tests)
builder.Services.AddSingleton<FakeNotificationSender>();
builder.Services.AddHttpClient<SendGridEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.sendgrid.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<TwilioSmsSender>(client =>
{
    client.BaseAddress = new Uri("https://api.twilio.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<AwsSesEmailSender>();
builder.Services.AddHttpClient<AwsSnsSmsSender>();
builder.Services.AddTransient<SmtpEmailSender>();

// Register INotificationSender and INotificationDeliveryService
builder.Services.AddTransient<INotificationSender, BaaNotificationSender>();
builder.Services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>();

// HMS Integration (Client, Conflict Resolution, Dead-Letter Queue, Webhooks, Sync Worker)
var useFakeHms = builder.Configuration.GetValue<bool>("Hms:UseFake", !builder.Environment.IsProduction()) ||
                 builder.Configuration.GetValue<bool>("HMS_USE_FAKE", false);
var hmsBaseUrl = builder.Configuration["Hms:BaseUrl"] ?? builder.Configuration["HMS_BASE_URL"];
var hmsSecret = builder.Configuration["Hms:WebhookSecret"] ?? builder.Configuration["HMS_WEBHOOK_SECRET"] ??
                builder.Configuration["Hms:WebhookSecrets"] ?? builder.Configuration["HMS_WEBHOOK_SECRETS"];

if (builder.Environment.IsProduction())
{
    if (useFakeHms)
    {
        throw new InvalidOperationException(
            "Production startup failed: Fake HMS client is strictly prohibited in production.");
    }

    if (string.IsNullOrWhiteSpace(hmsSecret) ||
        hmsSecret.Contains("demo-hms-webhook-secret", StringComparison.OrdinalIgnoreCase) ||
        hmsSecret.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Production startup failed: Valid production HMS webhook secret is required.");
    }

    if (string.IsNullOrWhiteSpace(hmsBaseUrl))
    {
        throw new InvalidOperationException(
            "Production startup failed: HMS base URL is required.");
    }

    if (Uri.TryCreate(hmsBaseUrl, UriKind.Absolute, out var uri))
    {
        var isInternal = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !isInternal)
        {
            throw new InvalidOperationException(
                $"Production startup failed: External HMS endpoint '{hmsBaseUrl}' must use HTTPS.");
        }
    }

    builder.Services.AddHttpClient<IHmsClient, HmsClient>(client =>
    {
        client.BaseAddress = new Uri(hmsBaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}
else
{
    builder.Services.AddSingleton<FakeHmsClient>();
    if (useFakeHms || string.IsNullOrWhiteSpace(hmsBaseUrl))
    {
        builder.Services.AddSingleton<IHmsClient>(sp => sp.GetRequiredService<FakeHmsClient>());
    }
    else
    {
        builder.Services.AddHttpClient<IHmsClient, HmsClient>(client =>
        {
            client.BaseAddress = new Uri(hmsBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }
}

builder.Services.AddScoped<IHmsConflictService, HmsConflictService>();
builder.Services.AddScoped<IHmsDeadLetterService, HmsDeadLetterService>();
builder.Services.AddScoped<IHmsWebhookHandler, HmsWebhookHandler>();
builder.Services.AddScoped<IHmsSyncService, HmsSyncService>();

// Clinical Core FHIR R4 Client (OpenMRS 2.8+ / O3 on PostgreSQL)
builder.Services.Configure<ClinicalCoreFhirOptions>(builder.Configuration.GetSection(ClinicalCoreFhirOptions.SectionName));
var coreFhirOptions = builder.Configuration.GetSection(ClinicalCoreFhirOptions.SectionName).Get<ClinicalCoreFhirOptions>() ?? new ClinicalCoreFhirOptions();

if (builder.Environment.IsProduction())
{
    if (coreFhirOptions.UseFake)
    {
        throw new InvalidOperationException("Production startup failed: ClinicalCore:UseFake cannot be true in Production.");
    }
    builder.Services.AddHttpClient<IClinicalCoreFhirClient, ClinicalCoreFhirClient>();
}
else
{
    builder.Services.AddSingleton<FakeClinicalCoreFhirClient>();
    if (coreFhirOptions.UseFake || string.IsNullOrWhiteSpace(coreFhirOptions.BaseUrl))
    {
        builder.Services.AddSingleton<IClinicalCoreFhirClient>(sp => sp.GetRequiredService<FakeClinicalCoreFhirClient>());
    }
    else
    {
        builder.Services.AddHttpClient<IClinicalCoreFhirClient, ClinicalCoreFhirClient>();
    }
}

// Automated Background Workers
builder.Services.AddHostedService<AppointmentReminderWorker>();
builder.Services.AddHostedService<BillingInstallmentWorker>();
builder.Services.AddHostedService<AuditLogBatchWorker>();
builder.Services.AddHostedService<HmsSyncWorker>();

builder.Services.AddScoped<PhiAuditFilter>();
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<PhiAuditFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Medical Portal API", Version = "v1" });
    c.SchemaFilter<PhiSwaggerHygieneFilter>();
    c.OperationFilter<PhiSwaggerHygieneFilter>();
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? builder.Configuration["CORS_ALLOWED_ORIGINS"]?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    ?? Array.Empty<string>();

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            // Default same-origin only: no cross-origin requests allowed
            policy.WithOrigins(Array.Empty<string>());
        }
    });
});

var app = builder.Build();

// Support dedicated migration command for Production/CI: dotnet run -- --migrate [--seed]
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
    Console.WriteLine("Applying database migrations...");
    await db.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied successfully.");

    if (args.Contains("--seed"))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        Console.WriteLine("Seeding database...");
        await DbSeeder.SeedAsync(db, hasher);
        Console.WriteLine("Database seeded successfully.");
    }
    return;
}

// In Development, apply migrations for PostgreSQL (or Migrate/EnsureCreated for SQLite) and seed automatically
if (app.Environment.IsDevelopment())
{
    await DbInitLock.WaitAsync();
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        if (db.Database.IsNpgsql())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            try
            {
                await db.Database.MigrateAsync();
            }
            catch
            {
                await db.Database.EnsureCreatedAsync();
            }
        }

        await DbSeeder.SeedAsync(db, hasher);
    }
    finally
    {
        DbInitLock.Release();
    }
}
else
{
    // Production: Validate required production services without running auto-migrations
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ITokenService>();

    var ikb = scope.ServiceProvider.GetService<IInteractionKnowledgeBase>();
    var akb = scope.ServiceProvider.GetService<IAllergyKnowledgeBase>();
    if (ikb is DemoInteractionKnowledgeBase || akb is DemoAllergyKnowledgeBase)
    {
        throw new InvalidOperationException(
            "Production startup failed: Demo clinical knowledge bases are registered in production.");
    }

    var pg = scope.ServiceProvider.GetService<IPaymentGateway>();
    if (pg is FakePaymentGateway)
    {
        throw new InvalidOperationException(
            "Production startup failed: Fake payment gateway is registered in production.");
    }

    var hms = scope.ServiceProvider.GetService<IHmsClient>();
    if (hms is FakeHmsClient)
    {
        throw new InvalidOperationException(
            "Production startup failed: Fake HMS client is registered in production.");
    }

    var clinicalCore = scope.ServiceProvider.GetService<IClinicalCoreFhirClient>();
    if (clinicalCore is FakeClinicalCoreFhirClient)
    {
        throw new InvalidOperationException(
            "Production startup failed: Fake clinical core FHIR client is registered in production.");
    }
}

// Global PHI-Safe Exception Handler (intercepts unhandled exceptions, sanitizes PHI, and returns safe responses)
app.UseMiddleware<PhiSafeExceptionHandlerMiddleware>();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Defensive Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Permissions-Policy", "geolocation=(self), camera=(), microphone=()");
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none';");

    if (context.Request.IsHttps ||
        string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase) ||
        !app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
    }

    await next();
});

var swaggerInProd = builder.Configuration.GetValue<bool>("Swagger:EnabledInProduction");
if (app.Environment.IsDevelopment() || swaggerInProd)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Medical Portal API v1");
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();
app.UseRateLimiter();

// Health Check Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 2),
                    description = e.Value.Description,
                    data = e.Value.Data,
                    error = e.Value.Exception?.Message
                })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await context.Response.WriteAsync(result);
    }
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Liveness check: returns 200 if app process is alive
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Tags.Contains("database") || check.Name == "database", // Readiness check: checks dependencies
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

// SignalR Hub
app.MapHub<PortalHub>("/hubs/portal");

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program
{
    private static readonly SemaphoreSlim DbInitLock = new(1, 1);
}
