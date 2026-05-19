using System;
using System.IO;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Backend
{
    /// <summary>
    /// MQTT-NTRIP 网关应用程序入口类。
    /// 使用 ASP.NET Core WebApplication 构建，集成 MQTT Broker、
    /// 多 NTRIP 挂载点连接管理和 HTTP 状态 API。
    /// </summary>
    class Program
    {
        /// <summary>
        /// 应用程序主入口点。
        /// </summary>
        /// <param name="args">命令行参数。</param>
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

                builder.Host.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console());

                builder.Services.AddSingleton<AppConfig>(sp =>
                {
                    var appConfig = new AppConfig();
                    builder.Configuration.Bind(appConfig);
                    return appConfig;
                });

                builder.Services.AddSingleton<NtripConnectionManager>();
                builder.Services.AddHostedService(sp => sp.GetRequiredService<NtripConnectionManager>());
                builder.Services.AddHostedService<MQTTService>();

                var app = builder.Build();

                var appConfig = app.Services.GetRequiredService<AppConfig>();
                Log.Information("Configuration loaded successfully.");
                Log.Information("  > MQTT Port: {Port}", appConfig.Mqtt.Port);
                foreach (var ntrip in appConfig.Ntrip)
                {
                    Log.Information("  > NTRIP Mountpoint: {Mount} @ {Host}:{Port} (Topic: {Topic})",
                        ntrip.Mountpoint, ntrip.Host, ntrip.Port, ntrip.Topic);
                }

                app.MapGet("/api/status", (NtripConnectionManager manager) =>
                {
                    return Results.Ok(manager.GetAllStatus());
                });

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
