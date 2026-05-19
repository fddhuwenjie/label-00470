using System;
using System.IO;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Backend
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("       MQTT & NTRIP Gateway - Starting Up");
                Console.WriteLine("==================================================");

                Log.Information("Initializing System...");

                var builder = WebApplication.CreateBuilder(args);

                builder.Configuration
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                builder.Host.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console());

                var appConfig = new AppConfig();
                builder.Configuration.Bind(appConfig);
                builder.Services.AddSingleton(appConfig);

                Log.Information("Configuration loaded successfully.");
                Log.Information("  > MQTT Port: {Port}", appConfig.Mqtt.Port);
                Log.Information("  > API Port: {Port}", appConfig.Mqtt.ApiPort);
                Log.Information("  > NTRIP Targets: {Count}", appConfig.NtripTargets?.Length ?? 0);

                if (appConfig.NtripTargets != null)
                {
                    foreach (var target in appConfig.NtripTargets)
                    {
                        Log.Information("    - {Topic} -> {Host}:{Port}/{Mount}",
                            target.SourceTopic, target.TargetCasterHost, target.TargetCasterPort, target.Mountpoint);
                    }
                }

                builder.Services.AddSingleton<NtripService>();
                builder.Services.AddHostedService(sp => sp.GetRequiredService<NtripService>());
                builder.Services.AddHostedService<MQTTService>();

                builder.WebHost.UseUrls($"http://*:{appConfig.Mqtt.ApiPort}");

                var app = builder.Build();

                app.MapGet("/api/status", (NtripService ntripService) =>
                {
                    return Results.Ok(ntripService.GetAllStatus());
                })
                .WithName("GetNtripStatus")
                .WithDescription("获取所有 NTRIP 连接的当前状态信息");

                Log.Information("Services initialized. Starting Host (API on port {Port})...", appConfig.Mqtt.ApiPort);
                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
