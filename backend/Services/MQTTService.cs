using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Backend.Services
{
    public class MQTTService : BackgroundService
    {
        private readonly AppConfig _config;
        private readonly ILogger<MQTTService> _logger;
        private readonly NtripService _ntripService;
        private MqttServer? _mqttServer;

        public MQTTService(AppConfig config, ILogger<MQTTService> logger, NtripService ntripService)
        {
            _config = config;
            _logger = logger;
            _ntripService = ntripService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var optionsBuilder = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(_config.Mqtt.Port);

            _mqttServer = new MqttFactory().CreateMqttServer(optionsBuilder.Build());

            _mqttServer.ValidatingConnectionAsync += ValidateConnection;
            _mqttServer.InterceptingPublishAsync += InterceptMessage;

            await _mqttServer.StartAsync();
            _logger.LogInformation("✅ MQTT Service STARTED. Listening on port: {Port}", _config.Mqtt.Port);

            // Wait until cancellation
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Graceful shutdown
            }
            finally
            {
                if (_mqttServer != null)
                {
                    await _mqttServer.StopAsync();
                }
            }
        }

        private Task ValidateConnection(ValidatingConnectionEventArgs arg)
        {
            if (string.IsNullOrEmpty(_config.Mqtt.Auth.Username))
            {
                arg.ReasonCode = MqttConnectReasonCode.Success;
                return Task.CompletedTask;
            }

            if (arg.UserName != _config.Mqtt.Auth.Username || arg.Password != _config.Mqtt.Auth.Password)
            {
                arg.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                _logger.LogWarning("MQTT connection rejected for user: {User}", arg.UserName);
                return Task.CompletedTask;
            }

            arg.ReasonCode = MqttConnectReasonCode.Success;
            _logger.LogInformation("MQTT connection accepted for user: {User}", arg.UserName);
            return Task.CompletedTask;
        }

        private async Task InterceptMessage(InterceptingPublishEventArgs arg)
        {
            // Check if topic matches
            if (arg.ApplicationMessage.Topic == _config.Ntrip.SourceTopic)
            {
                // Forward to NTRIP Service
                byte[] payload = arg.ApplicationMessage.PayloadSegment.ToArray();
                if (payload != null && payload.Length > 0)
                {
                    _logger.LogDebug("Received {Bytes} bytes on topic {Topic}, forwarding to NTRIP...", payload.Length, arg.ApplicationMessage.Topic);
                    await _ntripService.SendDataAsync(payload);
                }
            }
            
            // Allow message to proceed (e.g. to other subscribers)
            // If we only want to act as a bridge and not store/forward to others, we can set arg.ProcessPublish = false?
            // Usually we leave it to be distributed to subscribers if any.
        }
    }
}
