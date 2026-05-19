using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Services
{
    /// <summary>
    /// 管理单个 NTRIP 挂载点的 TCP 连接，包含连接建立、数据发送、
    /// 断线检测和指数退避自动重连逻辑。
    /// </summary>
    public class NtripConnection : IDisposable
    {
        private const int InitialReconnectDelayMs = 2000;
        private const int MaxReconnectDelayMs = 60000;

        private readonly NtripMountpointConfig _config;
        private readonly ILogger _logger;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private bool _isConnected;
        private bool _enabled = true;
        private int _reconnectCount;
        private long _totalBytesForwarded;
        private DateTime? _lastDataTime;
        private int _currentReconnectDelayMs = InitialReconnectDelayMs;

        /// <summary>
        /// 获取该连接对应的 MQTT Topic。
        /// </summary>
        public string Topic => _config.Topic;

        /// <summary>
        /// 获取该连接对应的挂载点名称。
        /// </summary>
        public string Mountpoint => _config.Mountpoint;

        /// <summary>
        /// 初始化 <see cref="NtripConnection"/> 的新实例。
        /// </summary>
        /// <param name="config">该挂载点的连接配置。</param>
        /// <param name="logger">日志记录器。</param>
        public NtripConnection(NtripMountpointConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;

            if (string.IsNullOrWhiteSpace(_config.Host)
                || _config.Host.Contains("example.com"))
            {
                _enabled = false;
                _logger.LogWarning(
                    "NTRIP Connection [{Mountpoint}] DISABLED: Host is not configured (current: '{Host}').",
                    _config.Mountpoint, _config.Host);
            }
        }

        /// <summary>
        /// 异步运行连接管理循环，包含自动重连和断线检测。
        /// 当传入的 <paramref name="stoppingToken"/> 被取消时退出循环。
        /// </summary>
        /// <param name="stoppingToken">用于通知终止的取消令牌。</param>
        public async Task RunAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation(
                    "NTRIP Connection [{Mountpoint}] is disabled due to missing configuration.",
                    _config.Mountpoint);
                return;
            }

            _logger.LogInformation(
                "NTRIP Connection [{Mountpoint}] starting... Target: {Host}:{Port}, Topic: {Topic}",
                _config.Mountpoint, _config.Host, _config.Port, _config.Topic);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_isConnected)
                {
                    try
                    {
                        await ConnectToCasterAsync(stoppingToken);
                        _currentReconnectDelayMs = InitialReconnectDelayMs;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _reconnectCount++;
                        _logger.LogError(ex,
                            "NTRIP [{Mountpoint}] connection failed. Reconnecting in {Delay}ms (attempt #{Attempt})...",
                            _config.Mountpoint, _currentReconnectDelayMs, _reconnectCount);

                        try
                        {
                            await Task.Delay(_currentReconnectDelayMs, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        _currentReconnectDelayMs = Math.Min(
                            _currentReconnectDelayMs * 2,
                            MaxReconnectDelayMs);
                        continue;
                    }
                }
                else
                {
                    try
                    {
                        if (_tcpClient != null && _tcpClient.Client != null)
                        {
                            if (_tcpClient.Client.Poll(0, SelectMode.SelectRead)
                                && _tcpClient.Client.Available == 0)
                            {
                                _logger.LogWarning(
                                    "NTRIP [{Mountpoint}] connection lost. Reconnecting...",
                                    _config.Mountpoint);
                                Disconnect();
                                _reconnectCount++;
                                _currentReconnectDelayMs = Math.Min(
                                    _currentReconnectDelayMs * 2,
                                    MaxReconnectDelayMs);
                                continue;
                            }
                        }
                        else
                        {
                            Disconnect();
                            _reconnectCount++;
                            continue;
                        }
                    }
                    catch
                    {
                        Disconnect();
                        _reconnectCount++;
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
        /// 异步连接到 NTRIP Caster 并完成 SOURCE 握手协议。
        /// </summary>
        /// <param name="stoppingToken">用于通知终止的取消令牌。</param>
        private async Task ConnectToCasterAsync(CancellationToken stoppingToken)
        {
            await _lock.WaitAsync(stoppingToken);
            try
            {
                _logger.LogInformation(
                    "NTRIP [{Mountpoint}] connecting to {Host}:{Port}...",
                    _config.Mountpoint, _config.Host, _config.Port);

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_config.Host, _config.Port, stoppingToken);
                _stream = _tcpClient.GetStream();

                string handshake =
                    $"SOURCE {_config.Password} {_config.Mountpoint}\r\nSource-Agent: NTRIP-CSharp-Gateway\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(handshake);
                await _stream.WriteAsync(data, 0, data.Length, stoppingToken);

                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                if (response.Contains("ICY 200 OK")
                    || response.Contains("HTTP/1.0 200 OK")
                    || response.Contains("HTTP/1.1 200 OK"))
                {
                    _isConnected = true;
                    _logger.LogInformation(
                        "NTRIP [{Mountpoint}] CONNECTED. Target: {Host}:{Port}",
                        _config.Mountpoint, _config.Host, _config.Port);
                }
                else
                {
                    _logger.LogError(
                        "NTRIP [{Mountpoint}] connection FAILED. Response: {Response}",
                        _config.Mountpoint, response.Trim());
                    Disconnect();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 异步发送数据到该挂载点对应的 NTRIP Caster 连接。
        /// 如果未连接则丢弃数据并记录警告。
        /// </summary>
        /// <param name="data">要发送的原始字节数据。</param>
        public async Task SendDataAsync(byte[] data)
        {
            if (!_enabled || !_isConnected || _stream == null)
            {
                if (_enabled)
                {
                    _logger.LogWarning(
                        "NTRIP [{Mountpoint}] not connected. Dropping data packet ({Size} bytes).",
                        _config.Mountpoint, data.Length);
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
                        Interlocked.Add(ref _totalBytesForwarded, data.Length);
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
                _logger.LogError(ex,
                    "NTRIP [{Mountpoint}] error sending data. Disconnecting...",
                    _config.Mountpoint);
                Disconnect();
            }
        }

        /// <summary>
        /// 断开当前 NTRIP Caster 连接并释放网络资源。
        /// </summary>
        private void Disconnect()
        {
            _isConnected = false;
            try { _stream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _stream = null;
            _tcpClient = null;
        }

        /// <summary>
        /// 获取该挂载点连接的当前运行时状态快照。
        /// </summary>
        /// <returns>包含连接状态、统计信息的 <see cref="NtripConnectionStatus"/> 实例。</returns>
        public NtripConnectionStatus GetStatus()
        {
            return new NtripConnectionStatus
            {
                Mountpoint = _config.Mountpoint,
                Target = $"{_config.Host}:{_config.Port}",
                Topic = _config.Topic,
                IsConnected = _isConnected,
                LastDataTime = _lastDataTime,
                TotalBytesForwarded = Interlocked.Read(ref _totalBytesForwarded),
                ReconnectCount = _reconnectCount
            };
        }

        /// <summary>
        /// 释放该实例占用的所有资源，包括 TCP 连接和信号量。
        /// </summary>
        public void Dispose()
        {
            Disconnect();
            _lock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
