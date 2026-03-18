using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Backend.Services
{
    public class NtripService : BackgroundService
    {
        private readonly NtripConfig _config;
        private readonly ILogger<NtripService> _logger;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private bool _isConnected = false;
        private bool _enabled = true;

        public NtripService(AppConfig config, ILogger<NtripService> logger)
        {
            _config = config.Ntrip;
            _logger = logger;

            // 检查是否为示例/未配置的地址，如果是则禁用自动连接
            if (string.IsNullOrWhiteSpace(_config.TargetCasterHost) 
                || _config.TargetCasterHost.Contains("example.com")
                || _config.TargetCasterHost == "localhost" && _config.TargetCasterPort == 0)
            {
                _enabled = false;
                _logger.LogWarning("⚠️ NTRIP Service DISABLED: TargetCasterHost is not configured (current: '{Host}'). Please update appsettings.json with a valid caster address.", _config.TargetCasterHost);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("NTRIP Service is disabled due to missing configuration. Waiting for config update...");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_isConnected)
                {
                    try
                    {
                        await ConnectToCasterAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to NTRIP Caster. Retrying in 5 seconds...");
                        try
                        {
                            await Task.Delay(5000, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    // 主动检测连接是否还活着
                    try
                    {
                        if (_tcpClient != null && _tcpClient.Client != null)
                        {
                            // Poll: SelectRead 返回 true 且 Available == 0 表示对端已关闭
                            if (_tcpClient.Client.Poll(0, System.Net.Sockets.SelectMode.SelectRead) && _tcpClient.Client.Available == 0)
                            {
                                _logger.LogWarning("NTRIP Caster connection lost. Reconnecting...");
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
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ConnectToCasterAsync(CancellationToken stoppingToken)
        {
            await _lock.WaitAsync(stoppingToken);
            try
            {
                _logger.LogInformation("Connecting to NTRIP Caster at {Host}:{Port}...", _config.TargetCasterHost, _config.TargetCasterPort);
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_config.TargetCasterHost, _config.TargetCasterPort, stoppingToken);
                _stream = _tcpClient.GetStream();

                // Send SOURCE handshake
                string handshake = $"SOURCE {_config.Password} {_config.Mountpoint}\r\nSource-Agent: NTRIP-CSharp-Gateway\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(handshake);
                await _stream.WriteAsync(data, 0, data.Length, stoppingToken);

                // Read response
                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                if (response.Contains("ICY 200 OK") || response.Contains("HTTP/1.0 200 OK") || response.Contains("HTTP/1.1 200 OK"))
                {
                    _logger.LogInformation("✅ NTRIP Service CONNECTED. Target: {Host}:{Port} Mountpoint: {Mount}", _config.TargetCasterHost, _config.TargetCasterPort, _config.Mountpoint);
                    _isConnected = true;
                }
                else
                {
                    _logger.LogError("❌ NTRIP Connection FAILED. Response: {Response}", response.Trim());
                    Disconnect();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SendDataAsync(byte[] data)
        {
            if (!_enabled || !_isConnected || _stream == null)
            {
                if (_enabled)
                {
                    _logger.LogWarning("NTRIP Caster not connected. Dropping data packet ({Size} bytes).", data.Length);
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
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending data to NTRIP Caster. Disconnecting...");
                Disconnect();
            }
        }

        private void Disconnect()
        {
            _isConnected = false;
            try { _stream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _stream = null;
            _tcpClient = null;
        }

        public override void Dispose()
        {
            Disconnect();
            _lock.Dispose();
            base.Dispose();
        }
    }
}
