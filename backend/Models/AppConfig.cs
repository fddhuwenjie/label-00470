namespace Backend.Models
{
    /// <summary>
    /// 应用程序配置根节点
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// MQTT 服务配置
        /// </summary>
        public MqttConfig Mqtt { get; set; } = new MqttConfig();

        /// <summary>
        /// NTRIP 目标配置数组，支持多个 Mountpoint
        /// </summary>
        public NtripTargetConfig[] NtripTargets { get; set; } = Array.Empty<NtripTargetConfig>();
    }

    /// <summary>
    /// MQTT 服务配置
    /// </summary>
    public class MqttConfig
    {
        /// <summary>
        /// MQTT 服务监听端口
        /// </summary>
        public int Port { get; set; } = 1883;

        /// <summary>
        /// MQTT 认证配置
        /// </summary>
        public AuthConfig Auth { get; set; } = new AuthConfig();

        /// <summary>
        /// HTTP API 服务监听端口
        /// </summary>
        public int ApiPort { get; set; } = 5000;
    }

    /// <summary>
    /// 认证配置
    /// </summary>
    public class AuthConfig
    {
        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// NTRIP 目标配置，每个配置对应一个独立的 Caster 连接和 Mountpoint
    /// </summary>
    public class NtripTargetConfig
    {
        /// <summary>
        /// 目标 Caster 主机地址
        /// </summary>
        public string TargetCasterHost { get; set; } = string.Empty;

        /// <summary>
        /// 目标 Caster 端口
        /// </summary>
        public int TargetCasterPort { get; set; } = 2101;

        /// <summary>
        /// 挂载点名称
        /// </summary>
        public string Mountpoint { get; set; } = string.Empty;

        /// <summary>
        /// Caster 认证密码
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 订阅的 MQTT Topic，该 Topic 的消息将转发到此 Mountpoint
        /// </summary>
        public string SourceTopic { get; set; } = string.Empty;
    }
}
