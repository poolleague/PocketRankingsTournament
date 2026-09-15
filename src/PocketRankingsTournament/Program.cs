using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using PocketRankingsTournament.Security;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllersWithViews();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = builder.Environment.IsDevelopment()
                    ? "PocketRankingsTournament.Development"
                    : "__Host-PocketRankingsTournament";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.LoginPath = builder.Environment.IsDevelopment() ? "/development/access" : "/account-required";
                options.AccessDeniedPath = "/access-denied";
            });
        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Name = builder.Environment.IsDevelopment()
                ? "PocketRankingsTournament.Development.CSRF"
                : "__Host-PocketRankingsTournament-CSRF";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });
        builder.Services.AddAuthorization(options => TournamentAuthorization.Configure(options));
        builder.Services.AddSingleton<BracketBuilder>();
        builder.Services.AddSingleton<LiveLinkRevealStore>();
        var privacyKey = builder.Configuration["Privacy:SuppressionHashKey"];
        if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(privacyKey))
            throw new InvalidOperationException("Privacy:SuppressionHashKey is required outside Development.");
        builder.Services.AddSingleton(new TournamentPrivacy(privacyKey ?? "development-only-tournament-privacy-key"));
        builder.Services.AddHealthChecks();

        var connectionString = builder.Configuration.GetConnectionString("TournamentDatabase");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            // One data source owns the connection pool; repositories must not create per-request pools.
            builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
            builder.Services.AddHostedService<PostgresSchemaInitializer>();
            builder.Services.AddScoped<ITournamentStore, PostgresTournamentStore>();
            builder.Services.AddHealthChecks().AddCheck<TournamentReadinessCheck>("tournament_database", tags: new[] { "ready" });
        }
        else if (builder.Environment.IsDevelopment())
        {
            // The fictional store keeps local design and automated work usable without pretending to be Production persistence.
            builder.Services.AddSingleton<ITournamentStore, DevelopmentTournamentStore>();
        }
        else
        {
            // A deployed installation must never appear healthy while silently serving fictional in-memory data.
            throw new InvalidOperationException("ConnectionStrings:TournamentDatabase is required outside Development.");
        }

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
            // Local Compose is intentionally HTTP-only; a future approved Caddy phase terminates public TLS.
            app.UseHttpsRedirection();
        }
        app.UseStaticFiles();

        app.Use(async (context, next) =>
        {
            // These headers keep organizer data out of embedding/referrer channels without weakening the public bracket surface.
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
            await next();
        });

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthAsync
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthAsync
        });
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthAsync
        });

        app.Run();
    }

    // Returns a bounded product-local health document without exception or connection-string details.
    private static Task WriteHealthAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy",
            product = "tournament"
        });
    }
}
