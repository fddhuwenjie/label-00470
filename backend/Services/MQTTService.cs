using System;
using System.Collections.Generic;
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
        private readonly Dictionary<string, NtripService> _ntripServicesByTopic;
        private MqttServer? _mqttServer;

        public MQTTService(AppConfig config, ILogger<MQTTService> logger, IEnumerable<NtripService> ntripServices)
        {
            _config = config;
            _logger = logger;
            _ntripServicesByTopic = new Dictionary<string, NtripService>();

            foreach (var ntripService in ntripServices)
            {
                var topic = ntripService.Config.SourceTopic;
                if (!string.IsNullOrEmpty(topic))
                {
                    _ntripServicesByTopic[topic] = ntripService;
                    _logger.LogInformation("MQTT Service: Registered topic '{Topic}' -> NtripService for mountpoint '{Mountpoint}'", topic, ntripService.Config.Mountpoint);
                }
            }
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
            _logger.LogInformation("MQTT Service STARTED. Listening on port: {Port}", _config.Mqtt.Port);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
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
            string topic = arg.ApplicationMessage.Topic;

            if (_ntripServicesByTopic.TryGetValue(topic, out var targetService))
            {
                byte[] payload = arg.ApplicationMessage.PayloadSegment.ToArray();
                if (payload != null && payload.Length > 0)
                {
                    _logger.LogDebug("Received {Bytes} bytes on topic {Topic}, forwarding to NTRIP mountpoint {Mountpoint}...", payload.Length, topic, targetService.Config.Mountpoint);
                    await targetService.SendDataAsync(payload);
                }
            }
        }
    }
}
