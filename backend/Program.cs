using System;
using System.IO;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services;
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
            // Setup Serilog first for early logging
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

                IHost host = Host.CreateDefaultBuilder(args)
                    .ConfigureAppConfiguration((context, config) =>
                    {
                        var path = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                        Log.Information("Loading configuration from: {Path}", path);
                        if (!File.Exists(path))
                        {
                            Log.Warning("Configuration file not found at expected path!");
                        }
                        
                        config.SetBasePath(Directory.GetCurrentDirectory());
                        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    })
                    .UseSerilog((context, services, configuration) => configuration
                        .ReadFrom.Configuration(context.Configuration)
                        .Enrich.FromLogContext()
                        .WriteTo.Console())
                    .ConfigureServices((context, services) =>
                    {
                        // Bind Configuration
                        var appConfig = new AppConfig();
                        context.Configuration.Bind(appConfig);
                        services.AddSingleton(appConfig);

                        Log.Information("Configuration loaded successfully.");
                        Log.Information("  > MQTT Port: {Port}", appConfig.Mqtt.Port);
                        Log.Information("  > Target Caster: {Host}:{Port}", appConfig.Ntrip.TargetCasterHost, appConfig.Ntrip.TargetCasterPort);
                        Log.Information("  > Mountpoint: {Mount}", appConfig.Ntrip.Mountpoint);

                        // Register Services
                        services.AddSingleton<NtripService>();
                        services.AddHostedService(sp => sp.GetRequiredService<NtripService>());
                        
                        services.AddHostedService<MQTTService>();
                    })
                    .Build();

                Log.Information("Services initialized. Starting Host...");
                await host.RunAsync();
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
