using Npgsql;
using PocketRankingsTournament.Services;

namespace PocketRankingsTournament;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton<BracketBuilder>();
        builder.Services.AddSingleton<ITournamentCatalog, TournamentCatalog>();

        var connectionString = builder.Configuration.GetConnectionString("TournamentDatabase");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            // One data source owns the connection pool; repositories must not create per-request pools.
            builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
            builder.Services.AddHostedService<PostgresSchemaInitializer>();
        }

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", product = "tournament" }));

        app.Run();
    }
}
