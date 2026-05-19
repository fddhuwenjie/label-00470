using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Backend.Services
{
    /// <summary>
    /// NTRIP 连接管理器，管理多个 Mountpoint 的独立连接实例
    /// </summary>
    public class NtripService : BackgroundService
    {
        private readonly AppConfig _config;
        private readonly ILogger<NtripService> _logger;
        private readonly ConcurrentDictionary<string, NtripClient> _clients = new();
        private readonly ConcurrentDictionary<string, NtripConnectionStatus> _statusCache = new();

        /// <summary>
        /// 初始化 NTRIP 服务管理器
        /// </summary>
        /// <param name="config">应用程序配置</param>
        /// <param name="logger">日志记录器</param>
        public NtripService(AppConfig config, ILogger<NtripService> logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有 NTRIP 连接的当前状态
        /// </summary>
        /// <returns>连接状态列表</returns>
        public IEnumerable<NtripConnectionStatus> GetAllStatus()
        {
            foreach (var client in _clients.Values)
            {
                yield return client.GetStatus();
            }
        }

        /// <summary>
        /// 根据 Topic 获取对应的 NTRIP 客户端并发送数据
        /// </summary>
        /// <param name="topic">MQTT Topic</param>
        /// <param name="data">要发送的数据</param>
        /// <returns>任务</returns>
        public async Task SendDataByTopicAsync(string topic, byte[] data)
        {
            if (_clients.TryGetValue(topic, out var client))
            {
                await client.SendDataAsync(data);
            }
            else
            {
                _logger.LogDebug("No NTRIP client configured for topic: {Topic}", topic);
            }
        }

        /// <summary>
        /// 后台服务执行入口
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        /// <returns>任务</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_config.NtripTargets == null || _config.NtripTargets.Length == 0)
            {
                _logger.LogWarning("⚠️ No NTRIP targets configured. NTRIP Service will not forward any data.");
                return;
            }

            _logger.LogInformation("Initializing {Count} NTRIP client(s)...", _config.NtripTargets.Length);

            foreach (var targetConfig in _config.NtripTargets)
            {
                if (string.IsNullOrWhiteSpace(targetConfig.SourceTopic))
                {
                    _logger.LogWarning("Skipping NTRIP target '{Mountpoint}' - SourceTopic is not configured.", targetConfig.Mountpoint);
                    continue;
                }

                var client = new NtripClient(targetConfig, _logger);
                if (_clients.TryAdd(targetConfig.SourceTopic, client))
                {
                    _logger.LogInformation("Created NTRIP client for topic: {Topic} -> {Host}:{Port}/{Mount}",
                        targetConfig.SourceTopic, targetConfig.TargetCasterHost, targetConfig.TargetCasterPort, targetConfig.Mountpoint);

                    _ = client.StartAsync(stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Duplicate SourceTopic '{Topic}' detected. Only first configuration will be used.", targetConfig.SourceTopic);
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("NTRIP Service shutting down. Stopping all clients...");
            foreach (var client in _clients.Values)
            {
                client.Stop();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _clients.Clear();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 单个 NTRIP 客户端实例，管理与一个 Caster Mountpoint 的连接
    /// </summary>
    internal class NtripClient : IDisposable
    {
        private const int InitialReconnectDelayMs = 2000;
        private const int MaxReconnectDelayMs = 60000;

        private readonly NtripTargetConfig _config;
        private readonly ILogger _logger;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private int _currentReconnectDelayMs = InitialReconnectDelayMs;
        private int _reconnectCount = 0;
        private long _totalBytesForwarded = 0;
        private DateTime? _lastDataTimeUtc;
        private string _connectionState = "Disconnected";
        private bool _enabled = true;
        private bool _disposed = false;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// 初始化 NTRIP 客户端
        /// </summary>
        /// <param name="config">目标配置</param>
        /// <param name="logger">日志记录器</param>
        public NtripClient(NtripTargetConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;

            if (string.IsNullOrWhiteSpace(_config.TargetCasterHost)
                || _config.TargetCasterHost.Contains("example.com")
                || (_config.TargetCasterHost == "localhost" && _config.TargetCasterPort == 0))
            {
                _enabled = false;
                _logger.LogWarning("⚠️ NTRIP client '{Mountpoint}' DISABLED: TargetCasterHost is not configured (current: '{Host}').",
                    _config.Mountpoint, _config.TargetCasterHost);
            }
        }

        /// <summary>
        /// 获取当前连接状态
        /// </summary>
        /// <returns>连接状态信息</returns>
        public NtripConnectionStatus GetStatus()
        {
            return new NtripConnectionStatus
            {
                Mountpoint = _config.Mountpoint,
                TargetCasterHost = _config.TargetCasterHost,
                TargetCasterPort = _config.TargetCasterPort,
                SourceTopic = _config.SourceTopic,
                ConnectionState = _enabled ? _connectionState : "Disabled",
                LastDataTimeUtc = _lastDataTimeUtc,
                TotalBytesForwarded = _totalBytesForwarded,
                ReconnectCount = _reconnectCount,
                CurrentReconnectDelayMs = _currentReconnectDelayMs
            };
        }

        /// <summary>
        /// 启动客户端连接循环
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        /// <returns>任务</returns>
        public async Task StartAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("NTRIP client '{Mountpoint}' is disabled.", _config.Mountpoint);
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            while (!_cts.Token.IsCancellationRequested)
            {
                if (_connectionState != "Connected")
                {
                    try
                    {
                        _connectionState = "Connecting";
                        await ConnectToCasterAsync(_cts.Token);
                        _connectionState = "Connected";
                        _currentReconnectDelayMs = InitialReconnectDelayMs;
                        _reconnectCount++;
                        _logger.LogInformation("✅ NTRIP client '{Mountpoint}' CONNECTED to {Host}:{Port}",
                            _config.Mountpoint, _config.TargetCasterHost, _config.TargetCasterPort);
                    }
                    catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _connectionState = "Disconnected";
                        _logger.LogError(ex, "NTRIP client '{Mountpoint}' failed to connect. Retrying in {Delay}ms (attempt {Count})",
                            _config.Mountpoint, _currentReconnectDelayMs, _reconnectCount + 1);

                        try
                        {
                            await Task.Delay(_currentReconnectDelayMs, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        _currentReconnectDelayMs = Math.Min(_currentReconnectDelayMs * 2, MaxReconnectDelayMs);
                    }
                }
                else
                {
                    try
                    {
                        if (_tcpClient != null && _tcpClient.Client != null)
                        {
                            if (_tcpClient.Client.Poll(0, System.Net.Sockets.SelectMode.SelectRead) && _tcpClient.Client.Available == 0)
                            {
                                _logger.LogWarning("NTRIP client '{Mountpoint}' connection lost. Reconnecting...", _config.Mountpoint);
                                Disconnect();
                                continue;
                            }
                        }
                        else
                        {
                            Disconnect();
                            continue;
                        }
                    }
                    catch
                    {
                        Disconnect();
                        continue;
                    }
                }

                try
                {
                    await Task.Delay(1000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _connectionState = "Disconnected";
            Disconnect();
        }

        /// <summary>
        /// 停止客户端
        /// </summary>
        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }
        }

        /// <summary>
        /// 连接到 NTRIP Caster
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        /// <returns>任务</returns>
        private async Task ConnectToCasterAsync(CancellationToken stoppingToken)
        {
            await _lock.WaitAsync(stoppingToken);
            try
            {
                _logger.LogInformation("NTRIP client '{Mountpoint}' connecting to {Host}:{Port}...",
                    _config.Mountpoint, _config.TargetCasterHost, _config.TargetCasterPort);

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_config.TargetCasterHost, _config.TargetCasterPort, stoppingToken);
                _stream = _tcpClient.GetStream();

                string handshake = $"SOURCE {_config.Password} {_config.Mountpoint}\r\nSource-Agent: NTRIP-CSharp-Gateway\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(handshake);
                await _stream.WriteAsync(data, 0, data.Length, stoppingToken);

                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                if (response.Contains("ICY 200 OK") || response.Contains("HTTP/1.0 200 OK") || response.Contains("HTTP/1.1 200 OK"))
                {
                    _logger.LogInformation("✅ NTRIP client '{Mountpoint}' handshake successful.", _config.Mountpoint);
                }
                else
                {
                    throw new InvalidOperationException($"NTRIP handshake failed. Response: {response.Trim()}");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 发送数据到 NTRIP Caster
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <returns>任务</returns>
        public async Task SendDataAsync(byte[] data)
        {
            if (!_enabled || _connectionState != "Connected" || _stream == null)
            {
                if (_enabled)
                {
                    _logger.LogDebug("NTRIP client '{Mountpoint}' not connected. Dropping {Size} bytes.", _config.Mountpoint, data.Length);
                }
                return;
            }

            try
            {
                await _lock.WaitAsync();
                try
                {
                    if (_stream != null && _stream.CanWrite)
                    {
                        await _stream.WriteAsync(data, 0, data.Length);
                        await _stream.FlushAsync();
                        _totalBytesForwarded += data.Length;
                        _lastDataTimeUtc = DateTime.UtcNow;
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NTRIP client '{Mountpoint}' error sending data. Disconnecting...", _config.Mountpoint);
                Disconnect();
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        private void Disconnect()
        {
            _connectionState = "Disconnected";
            try { _stream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _stream = null;
            _tcpClient = null;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                Stop();
                Disconnect();
                _lock.Dispose();
                _cts?.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~NtripClient()
        {
            Dispose(false);
        }
    }
}
