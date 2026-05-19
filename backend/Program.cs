using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

                var appConfig = new AppConfig();
                builder.Configuration.Bind(appConfig);
                builder.Services.AddSingleton(appConfig);

                Log.Information("Configuration loaded successfully.");
                Log.Information("  > MQTT Port: {Port}", appConfig.Mqtt.Port);
                Log.Information("  > HTTP Status Endpoint: http://localhost:5000/api/status");

                var ntripServices = new List<NtripService>();
                foreach (var casterConfig in appConfig.NtripCasters)
                {
                    var ntripLogger = LoggerFactory.Create(factory => factory.AddConsole()).CreateLogger<NtripService>();
                    var service = new NtripService(casterConfig, ntripLogger);
                    ntripServices.Add(service);
                    Log.Information("  > NTRIP Caster: {Host}:{Port} Mountpoint: {Mountpoint} Topic: {Topic}", casterConfig.Host, casterConfig.Port, casterConfig.Mountpoint, casterConfig.SourceTopic);
                }

                foreach (var service in ntripServices)
                {
                    service.Start();
                }

                builder.Services.AddSingleton(ntripServices.AsEnumerable());
                builder.Services.AddHostedService<MQTTService>();

                builder.WebHost.UseUrls("http://0.0.0.0:5000");

                builder.Host.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console());

                var app = builder.Build();

                app.MapGet("/api/status", (HttpContext context) =>
                {
                    var services = context.RequestServices.GetRequiredService<IEnumerable<NtripService>>();
                    var statusList = new List<NtripConnectionStatus>();
                    foreach (var svc in services)
                    {
                        statusList.Add(svc.GetStatus());
                    }
                    return Results.Json(statusList);
                });

                Log.Information("Services initialized. Starting Host...");
                await app.RunAsync();

                foreach (var service in ntripServices)
                {
                    service.Stop();
                    service.Dispose();
                }
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
