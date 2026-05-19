using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Services
{
    /// <summary>
    /// 管理单个 NTRIP Caster 连接的服务实例。每个 Mountpoint 对应一个独立的 NtripService 实例，
    /// 负责建立连接、发送数据流、监控连接状态以及指数退避自动重连。
    /// </summary>
    public class NtripService : IDisposable
    {
        private readonly NtripCasterConfig _config;
        private readonly ILogger<NtripService> _logger;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private volatile bool _isConnected = false;
        private volatile bool _enabled = true;
        private volatile long _totalBytesSent = 0;
        private volatile int _reconnectCount = 0;
        private volatile DateTime? _lastDataTime = null;

        private Task? _connectionMonitorTask;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// 获取当前是否已连接到 NTRIP Caster。
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 获取该实例是否已启用配置。若 Host 未配置或为示例地址则返回 false。
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// 获取该实例关联的 Caster 配置。
        /// </summary>
        public NtripCasterConfig Config => _config;

        /// <summary>
        /// 获取该实例累计成功发送到 Caster 的字节数。
        /// </summary>
        public long TotalBytesSent => _totalBytesSent;

        /// <summary>
        /// 获取该实例自上次连接成功以来的断线重连次数。
        /// </summary>
        public int ReconnectCount => _reconnectCount;

        /// <summary>
        /// 获取该实例最后一次成功发送数据的 UTC 时间。
        /// </summary>
        public DateTime? LastDataTime => _lastDataTime;

        /// <summary>
        /// 用指定的 Caster 配置和日志工厂初始化 NtripService 实例。
        /// </summary>
        /// <param name="config">NTRIP Caster 配置，包含 host/port/mountpoint/password/sourceTopic。</param>
        /// <param name="logger">日志工厂实例。</param>
        public NtripService(NtripCasterConfig config, ILogger<NtripService> logger)
        {
            _config = config;
            _logger = logger;

            if (string.IsNullOrWhiteSpace(_config.Host)
                || _config.Host.Contains("example.com")
                || (_config.Host == "localhost" && _config.Port == 0))
            {
                _enabled = false;
                _logger.LogWarning("NTRIP Service DISABLED for mountpoint {Mountpoint}: Host '{Host}' is not configured.", _config.Mountpoint, _config.Host);
            }
        }

        /// <summary>
        /// 启动该实例的连接监控循环。若实例因配置问题已禁用则不执行任何操作。
        /// </summary>
        public void Start()
        {
            if (!_enabled)
            {
                _logger.LogInformation("NTRIP Service for mountpoint {Mountpoint} is disabled due to missing configuration.", _config.Mountpoint);
                return;
            }

            _cts = new CancellationTokenSource();
            _connectionMonitorTask = Task.Run(() => ConnectionMonitorLoop(_cts.Token), _cts.Token);
        }

        /// <summary>
        /// 停止该实例的连接监控循环并断开当前连接。
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            lock (_lock)
            {
                DisconnectInternal();
            }
        }

        /// <summary>
        /// 连接监控循环：当未连接时尝试连接，连接失败则按指数退避策略等待；
        /// 当已连接时周期性检测连接是否仍然存活。
        /// </summary>
        /// <param name="stoppingToken">用于取消该循环的令牌。</param>
        private async Task ConnectionMonitorLoop(CancellationToken stoppingToken)
        {
            int currentDelayMs = 2000;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_isConnected)
                {
                    try
                    {
                        _reconnectCount++;
                        await ConnectToCasterAsync(stoppingToken);
                        if (_isConnected)
                        {
                            currentDelayMs = 2000;
                            _reconnectCount = 0;
                        }
                        else
                        {
                            await Task.Delay(currentDelayMs, stoppingToken);
                            currentDelayMs = Math.Min(currentDelayMs * 2, 60000);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Mountpoint {Mountpoint}: Failed to connect to NTRIP Caster. Retrying in {Delay}ms (exponential backoff)...", _config.Mountpoint, currentDelayMs);
                        try
                        {
                            await Task.Delay(currentDelayMs, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        currentDelayMs = Math.Min(currentDelayMs * 2, 60000);
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
                                _logger.LogWarning("Mountpoint {Mountpoint}: NTRIP Caster connection lost. Reconnecting...", _config.Mountpoint);
                                lock (_lock)
                                {
                                    DisconnectInternal();
                                }
                                currentDelayMs = 2000;
                                continue;
                            }
                        }
                        else
                        {
                            lock (_lock)
                            {
                                DisconnectInternal();
                            }
                            currentDelayMs = 2000;
                            continue;
                        }
                    }
                    catch
                    {
                        lock (_lock)
                        {
                            DisconnectInternal();
                        }
                        currentDelayMs = 2000;
                        continue;
                    }
                }

                try
                {
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 异步连接到 NTRIP Caster，发送 SOURCE 握手并验证响应。
        /// </summary>
        /// <param name="stoppingToken">用于取消连接过程的令牌。</param>
        private async Task ConnectToCasterAsync(CancellationToken stoppingToken)
        {
            await _lock.WaitAsync(stoppingToken);
            try
            {
                _logger.LogInformation("Mountpoint {Mountpoint}: Connecting to NTRIP Caster at {Host}:{Port}...", _config.Mountpoint, _config.Host, _config.Port);
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_config.Host, _config.Port, stoppingToken);
                _stream = _tcpClient.GetStream();

                string handshake = $"SOURCE {_config.Password} {_config.Mountpoint}\r\nSource-Agent: NTRIP-CSharp-Gateway\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(handshake);
                await _stream.WriteAsync(data, 0, data.Length, stoppingToken);

                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                if (response.Contains("ICY 200 OK") || response.Contains("HTTP/1.0 200 OK") || response.Contains("HTTP/1.1 200 OK"))
                {
                    _logger.LogInformation("Mountpoint {Mountpoint}: CONNECTED. Target: {Host}:{Port}", _config.Mountpoint, _config.Host, _config.Port);
                    _isConnected = true;
                }
                else
                {
                    _logger.LogError("Mountpoint {Mountpoint}: Connection FAILED. Response: {Response}", _config.Mountpoint, response.Trim());
                    DisconnectInternal();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 异步发送数据到当前已连接的 NTRIP Caster。若未连接或已禁用则丢弃数据包。
        /// </summary>
        /// <param name="data">要发送的二进制数据（通常为 GNSS RTK 流）。</param>
        public async Task SendDataAsync(byte[] data)
        {
            if (!_enabled || !_isConnected || _stream == null)
            {
                if (_enabled)
                {
                    _logger.LogWarning("Mountpoint {Mountpoint}: NTRIP Caster not connected. Dropping data packet ({Size} bytes).", _config.Mountpoint, data.Length);
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
                        Interlocked.Add(ref _totalBytesSent, data.Length);
                        _lastDataTime = DateTime.UtcNow;
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mountpoint {Mountpoint}: Error sending data to NTRIP Caster. Disconnecting...", _config.Mountpoint);
                lock (_lock)
                {
                    DisconnectInternal();
                }
            }
        }

        /// <summary>
        /// 释放当前 TCP 连接和网络流，将连接状态置为未连接。
        /// </summary>
        private void DisconnectInternal()
        {
            _isConnected = false;
            try { _stream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _stream = null;
            _tcpClient = null;
        }

        /// <summary>
        /// 采集并返回该 NtripService 实例的当前连接状态，供 HTTP API 读取。
        /// </summary>
        /// <returns>包含 Mountpoint、连接状态、最后数据时间、累计字节数、重连次数等信息的 <see cref="NtripConnectionStatus"/> 对象。</returns>
        public NtripConnectionStatus GetStatus()
        {
            return new NtripConnectionStatus
            {
                Mountpoint = _config.Mountpoint,
                Host = _config.Host,
                Port = _config.Port,
                IsConnected = _isConnected,
                LastDataTime = _lastDataTime,
                TotalBytesSent = _totalBytesSent,
                ReconnectCount = _reconnectCount,
                SourceTopic = _config.SourceTopic
            };
        }

        /// <summary>
        /// 释放该实例占用的所有资源，包括连接监控循环、TCP 连接和信号量。
        /// </summary>
        public void Dispose()
        {
            _cts?.Cancel();
            lock (_lock)
            {
                DisconnectInternal();
            }
            _lock.Dispose();
            _cts?.Dispose();
        }
    }
}
