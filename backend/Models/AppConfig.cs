namespace Backend.Models
{
    /// <summary>
    /// 应用程序全局配置根对象，绑定自 appsettings.json。
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// MQTT 服务器配置。
        /// </summary>
        public MqttConfig Mqtt { get; set; } = new MqttConfig();

        /// <summary>
        /// 向后兼容保留的单一 NTRIP 配置（已弃用，建议使用 <see cref="NtripCasters"/> 数组）。
        /// </summary>
        public NtripConfig Ntrip { get; set; } = new NtripConfig();

        /// <summary>
        /// 多 Mountpoint NTRIP Caster 配置数组，每项可独立指定 host/port/mountpoint/password/sourceTopic。
        /// </summary>
        public List<NtripCasterConfig> NtripCasters { get; set; } = new List<NtripCasterConfig>();
    }

    /// <summary>
    /// MQTT 服务器相关配置。
    /// </summary>
    public class MqttConfig
    {
        /// <summary>
        /// MQTT 监听端口。
        /// </summary>
        public int Port { get; set; } = 1883;

        /// <summary>
        /// MQTT 连接认证配置。
        /// </summary>
        public AuthConfig Auth { get; set; } = new AuthConfig();
    }

    /// <summary>
    /// MQTT 连接认证配置。
    /// </summary>
    public class AuthConfig
    {
        /// <summary>
        /// 认证用户名。为空则禁用认证。
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 认证密码。为空则禁用认证。
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// 单一 NTRIP Caster 配置（向后兼容保留）。
    /// </summary>
    public class NtripConfig
    {
        /// <summary>
        /// 目标 NTRIP Caster 主机地址。
        /// </summary>
        public string TargetCasterHost { get; set; } = string.Empty;

        /// <summary>
        /// 目标 NTRIP Caster 端口。
        /// </summary>
        public int TargetCasterPort { get; set; } = 2101;

        /// <summary>
        /// 目标 Mountpoint 名称。
        /// </summary>
        public string Mountpoint { get; set; } = string.Empty;

        /// <summary>
        /// NTRIP SOURCE 握手密码。
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// MQTT 监听的源 Topic，收到该 Topic 的消息将转发到对应的 Caster。
        /// </summary>
        public string SourceTopic { get; set; } = "ntrip/data";
    }

    /// <summary>
    /// 多 Mountpoint 配置：每个实例对应一个独立的 NTRIP Caster 目标。
    /// </summary>
    public class NtripCasterConfig
    {
        /// <summary>
        /// 目标 NTRIP Caster 主机地址。
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// 目标 NTRIP Caster 端口。
        /// </summary>
        public int Port { get; set; } = 2101;

        /// <summary>
        /// 目标 Mountpoint 名称。
        /// </summary>
        public string Mountpoint { get; set; } = string.Empty;

        /// <summary>
        /// NTRIP SOURCE 握手密码。
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// MQTT 监听的源 Topic，收到该 Topic 的消息将转发到此 Caster。
        /// </summary>
        public string SourceTopic { get; set; } = string.Empty;
    }

    /// <summary>
    /// NTRIP 连接状态 DTO，用于 GET /api/status HTTP 端点的返回。
    /// </summary>
    public class NtripConnectionStatus
    {
        /// <summary>
        /// Mountpoint 名称。
        /// </summary>
        public string Mountpoint { get; set; } = string.Empty;

        /// <summary>
        /// Caster 主机地址。
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Caster 端口。
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 当前是否已连接到 Caster。
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 最后一次成功发送数据到 Caster 的 UTC 时间。
        /// </summary>
        public DateTime? LastDataTime { get; set; }

        /// <summary>
        /// 累计成功发送到 Caster 的字节数。
        /// </summary>
        public long TotalBytesSent { get; set; }

        /// <summary>
        /// 自上次连接成功以来的断线重连次数。
        /// </summary>
        public int ReconnectCount { get; set; }

        /// <summary>
        /// 关联的 MQTT 源 Topic。
        /// </summary>
        public string SourceTopic { get; set; } = string.Empty;
    }
}
