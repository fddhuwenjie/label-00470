namespace Backend.Models
{
    /// <summary>
    /// NTRIP 连接状态信息，用于 HTTP API 输出
    /// </summary>
    public class NtripConnectionStatus
    {
        /// <summary>
        /// 挂载点名称
        /// </summary>
        public string Mountpoint { get; set; } = string.Empty;

        /// <summary>
        /// 目标 Caster 主机地址
        /// </summary>
        public string TargetCasterHost { get; set; } = string.Empty;

        /// <summary>
        /// 目标 Caster 端口
        /// </summary>
        public int TargetCasterPort { get; set; }

        /// <summary>
        /// 订阅的 MQTT Topic
        /// </summary>
        public string SourceTopic { get; set; } = string.Empty;

        /// <summary>
        /// 连接状态：Connected, Disconnected, Connecting
        /// </summary>
        public string ConnectionState { get; set; } = string.Empty;

        /// <summary>
        /// 最后一次成功发送数据的时间（UTC）
        /// </summary>
        public DateTime? LastDataTimeUtc { get; set; }

        /// <summary>
        /// 累计转发字节数
        /// </summary>
        public long TotalBytesForwarded { get; set; }

        /// <summary>
        /// 重连次数
        /// </summary>
        public int ReconnectCount { get; set; }

        /// <summary>
        /// 当前重连延迟（毫秒），用于指数退避状态展示
        /// </summary>
        public int CurrentReconnectDelayMs { get; set; }
    }
}
