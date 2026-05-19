using System;

namespace Backend.Models
{
    /// <summary>
    /// 应用程序顶层配置模型，包含 MQTT 和多 NTRIP 挂载点配置。
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// MQTT 服务器配置。
        /// </summary>
        public MqttConfig Mqtt { get; set; } = new MqttConfig();

        /// <summary>
        /// NTRIP 多挂载点配置数组，每个元素代表一个独立的 NTRIP Caster 目标。
        /// </summary>
        public List<NtripMountpointConfig> Ntrip { get; set; } = new List<NtripMountpointConfig>();
    }

    /// <summary>
    /// MQTT 服务器配置。
    /// </summary>
    public class MqttConfig
    {
        /// <summary>
        /// MQTT 服务器监听端口。
        /// </summary>
        public int Port { get; set; } = 1883;

        /// <summary>
        /// MQTT 认证配置。
        /// </summary>
        public AuthConfig Auth { get; set; } = new AuthConfig();
    }

    /// <summary>
    /// MQTT 认证配置。
    /// </summary>
    public class AuthConfig
    {
        /// <summary>
        /// 认证用户名。为空时允许匿名连接。
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 认证密码。
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// 单个 NTRIP 挂载点的连接配置，包含目标 Caster 信息和对应的 MQTT 路由 Topic。
    /// </summary>
    public class NtripMountpointConfig
    {
        /// <summary>
        /// NTRIP Caster 主机地址。
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// NTRIP Caster 端口号。
        /// </summary>
        public int Port { get; set; } = 2101;

        /// <summary>
        /// NTRIP 挂载点名称（如 BASE1、BASE2）。
        /// </summary>
        public string Mountpoint { get; set; } = string.Empty;

        /// <summary>
        /// NTRIP Source 认证密码。
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 对应的 MQTT Topic，该 Topic 上的消息将转发到此挂载点。
        /// 例如 "ntrip/base1" 表示 MQTT 主题 ntrip/base1 的消息转发到此挂载点。
        /// </summary>
        public string Topic { get; set; } = string.Empty;
    }

    /// <summary>
    /// 单个 NTRIP 挂载点连接的运行时状态信息，用于 HTTP API 返回。
    /// </summary>
    public class NtripConnectionStatus
    {
        /// <summary>
        /// 挂载点名称。
        /// </summary>
        public string Mountpoint { get; set; } = string.Empty;

        /// <summary>
        /// 目标 Caster 地址（格式: host:port）。
        /// </summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>
        /// 对应的 MQTT Topic。
        /// </summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>
        /// 当前是否已连接到 NTRIP Caster。
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 最后一次成功转发数据的 UTC 时间戳。未转发过数据时为 null。
        /// </summary>
        public DateTime? LastDataTime { get; set; }

        /// <summary>
        /// 累计转发的字节数。
        /// </summary>
        public long TotalBytesForwarded { get; set; }

        /// <summary>
        /// 累计重连次数。
        /// </summary>
        public int ReconnectCount { get; set; }
    }
}
