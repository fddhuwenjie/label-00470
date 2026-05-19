using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.Services
{
    /// <summary>
    /// NTRIP 连接管理器，负责根据配置创建和管理多个 <see cref="NtripConnection"/> 实例，
    /// 提供 MQTT Topic 到 NTRIP 挂载点的路由转发以及全局连接状态查询。
    /// 作为 <see cref="BackgroundService"/> 运行，在后台并行维护所有连接。
    /// </summary>
    public class NtripConnectionManager : BackgroundService
    {
        private readonly AppConfig _appConfig;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<NtripConnectionManager> _logger;
        private readonly Dictionary<string, NtripConnection> _connectionsByTopic = new();
        private readonly List<NtripConnection> _allConnections = new();

        /// <summary>
        /// 初始化 <see cref="NtripConnectionManager"/> 的新实例。
        /// </summary>
        /// <param name="appConfig">应用程序配置。</param>
        /// <param name="loggerFactory">日志工厂，用于为每个连接创建独立的日志记录器。</param>
        /// <param name="logger">管理器自身的日志记录器。</param>
        public NtripConnectionManager(
            AppConfig appConfig,
            ILoggerFactory loggerFactory,
            ILogger<NtripConnectionManager> logger)
        {
            _appConfig = appConfig;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        /// <summary>
        /// 后台服务执行入口。为每个配置的挂载点创建 <see cref="NtripConnection"/> 实例，
        /// 并并行启动所有连接的运行循环。
        /// </summary>
        /// <param name="stoppingToken">用于通知终止的取消令牌。</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_appConfig.Ntrip == null || _appConfig.Ntrip.Count == 0)
            {
                _logger.LogWarning("No NTRIP mountpoint configurations found. Connection manager will idle.");
                await Task.Delay(Timeout.Infinite, stoppingToken);
                return;
            }

            foreach (var mountConfig in _appConfig.Ntrip)
            {
                var connectionLogger = _loggerFactory.CreateLogger($"NtripConnection.{mountConfig.Mountpoint}");
                var connection = new NtripConnection(mountConfig, connectionLogger);
                _allConnections.Add(connection);
                _connectionsByTopic[mountConfig.Topic] = connection;

                _logger.LogInformation(
                    "Registered NTRIP connection: Mountpoint={Mountpoint}, Topic={Topic}, Target={Host}:{Port}",
                    mountConfig.Mountpoint, mountConfig.Topic, mountConfig.Host, mountConfig.Port);
            }

            var tasks = _allConnections.Select(c => c.RunAsync(stoppingToken)).ToArray();

            _logger.LogInformation("NTRIP Connection Manager started with {Count} mountpoint(s).", _allConnections.Count);

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("NTRIP Connection Manager is shutting down...");
            }
        }

        /// <summary>
        /// 根据 MQTT Topic 将数据路由到对应的 NTRIP 挂载点连接。
        /// 如果没有匹配的 Topic，则记录警告并丢弃数据。
        /// </summary>
        /// <param name="topic">MQTT 消息的 Topic。</param>
        /// <param name="data">要转发的原始字节数据。</param>
        public async Task RouteAsync(string topic, byte[] data)
        {
            if (_connectionsByTopic.TryGetValue(topic, out var connection))
            {
                await connection.SendDataAsync(data);
            }
            else
            {
                _logger.LogWarning(
                    "No NTRIP connection mapped for MQTT topic '{Topic}'. Dropping {Size} bytes.",
                    topic, data.Length);
            }
        }

        /// <summary>
        /// 获取所有 NTRIP 挂载点连接的当前运行时状态快照列表。
        /// </summary>
        /// <returns>每个挂载点连接的状态信息列表。</returns>
        public List<NtripConnectionStatus> GetAllStatus()
        {
            return _allConnections.Select(c => c.GetStatus()).ToList();
        }

        /// <summary>
        /// 释放所有 NTRIP 连接实例占用的资源。
        /// </summary>
        public override void Dispose()
        {
            foreach (var connection in _allConnections)
            {
                connection.Dispose();
            }
            _allConnections.Clear();
            _connectionsByTopic.Clear();
            base.Dispose();
        }
    }
}
