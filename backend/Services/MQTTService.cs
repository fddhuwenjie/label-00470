using System;
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
    /// <summary>
    /// MQTT 服务，监听 MQTT 消息并根据 Topic 路由到对应的 NTRIP 客户端
    /// </summary>
    public class MQTTService : BackgroundService
    {
        private readonly AppConfig _config;
        private readonly ILogger<MQTTService> _logger;
        private readonly NtripService _ntripService;
        private MqttServer? _mqttServer;

        /// <summary>
        /// 初始化 MQTT 服务
        /// </summary>
        /// <param name="config">应用程序配置</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="ntripService">NTRIP 服务管理器</param>
        public MQTTService(AppConfig config, ILogger<MQTTService> logger, NtripService ntripService)
        {
            _config = config;
            _logger = logger;
            _ntripService = ntripService;
        }

        /// <summary>
        /// 后台服务执行入口
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        /// <returns>任务</returns>
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

        /// <summary>
        /// 验证 MQTT 连接
        /// </summary>
        /// <param name="arg">连接验证参数</param>
        /// <returns>任务</returns>
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

        /// <summary>
        /// 拦截并处理发布的消息，根据 Topic 路由到对应的 NTRIP 客户端
        /// </summary>
        /// <param name="arg">发布事件参数</param>
        /// <returns>任务</returns>
        private async Task InterceptMessage(InterceptingPublishEventArgs arg)
        {
            string topic = arg.ApplicationMessage.Topic;
            byte[] payload = arg.ApplicationMessage.PayloadSegment.ToArray();

            if (payload != null && payload.Length > 0)
            {
                _logger.LogDebug("Received {Bytes} bytes on topic {Topic}", payload.Length, topic);
                await _ntripService.SendDataByTopicAsync(topic, payload);
            }
        }
    }
}
