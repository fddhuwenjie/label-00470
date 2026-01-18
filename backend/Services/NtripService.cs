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

        public NtripService(AppConfig config, ILogger<NtripService> logger)
        {
            _config = config.Ntrip;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_isConnected)
                {
                    try
                    {
                        await ConnectToCasterAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to NTRIP Caster. Retrying in 5 seconds...");
                        await Task.Delay(5000, stoppingToken);
                    }
                }
                
                // Keep connection check or heartbeat if needed
                // For now just wait a bit
                await Task.Delay(1000, stoppingToken);
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
                // Protocol: SOURCE <password> <mountpoint>\r\nSource-Agent: NTRIP-C#\r\n\r\n
                string handshake = $"SOURCE {_config.Password} {_config.Mountpoint}\r\nSource-Agent: NTRIP-CSharp-Gateway\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(handshake);
                await _stream.WriteAsync(data, 0, data.Length, stoppingToken);

                // Read response
                // Expect "ICY 200 OK" or similar
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
                    _tcpClient.Close();
                    _isConnected = false;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SendDataAsync(byte[] data)
        {
            if (!_isConnected || _stream == null)
            {
                // Optionally buffer or just drop
                _logger.LogWarning("NTRIP Caster not connected. Dropping data packet ({Size} bytes).", data.Length);
                return;
            }

            try
            {
                await _lock.WaitAsync();
                try
                {
                    if (_stream.CanWrite)
                    {
                        await _stream.WriteAsync(data, 0, data.Length);
                        await _stream.FlushAsync();
                        // _logger.LogDebug("Sent {Size} bytes to NTRIP Caster", data.Length);
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
                _isConnected = false;
                try { _tcpClient?.Close(); } catch { }
            }
        }

        public override void Dispose()
        {
            _tcpClient?.Dispose();
            _lock.Dispose();
            base.Dispose();
        }
    }
}
