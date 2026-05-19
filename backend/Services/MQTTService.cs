using System;
using System.Linq;
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
    /// MQTT 服务器后台服务，负责启动内置 MQTT Broker、验证客户端连接、
    /// 并将收到的消息按 Topic 路由到对应的 NTRIP 挂载点连接。
    /// </summary>
    public class MQTTService : BackgroundService
    {
        private readonly AppConfig _config;
        private readonly ILogger<MQTTService> _logger;
        private readonly NtripConnectionManager _ntripManager;
        private MqttServer? _mqttServer;

        /// <summary>
        /// 初始化 <see cref="MQTTService"/> 的新实例。
        /// </summary>
        /// <param name="config">应用程序配置。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="ntripManager">NTRIP 连接管理器，用于消息路由转发。</param>
        public MQTTService(
            AppConfig config,
            ILogger<MQTTService> logger,
            NtripConnectionManager ntripManager)
        {
            _config = config;
            _logger = logger;
            _ntripManager = ntripManager;
        }

        /// <summary>
        /// 后台服务执行入口。启动 MQTT Broker 并注册消息拦截和连接验证回调。
        /// </summary>
        /// <param name="stoppingToken">用于通知终止的取消令牌。</param>
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

        /// <summary>
        /// 验证 MQTT 客户端连接请求的凭据。若未配置认证则允许所有连接。
        /// </summary>
        /// <param name="arg">连接验证事件参数。</param>
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
        /// 拦截 MQTT 发布消息，根据消息 Topic 路由到对应的 NTRIP 挂载点连接进行转发。
        /// </summary>
        /// <param name="arg">消息拦截事件参数。</param>
        private async Task InterceptMessage(InterceptingPublishEventArgs arg)
        {
            string topic = arg.ApplicationMessage.Topic;
            byte[] payload = arg.ApplicationMessage.PayloadSegment.ToArray();

            if (payload != null && payload.Length > 0)
            {
                _logger.LogDebug(
                    "Received {Bytes} bytes on topic '{Topic}', routing to NTRIP...",
                    payload.Length, topic);
                await _ntripManager.RouteAsync(topic, payload);
            }
        }
    }
}
