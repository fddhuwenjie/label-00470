namespace Backend.Models
{
    public class AppConfig
    {
        public MqttConfig Mqtt { get; set; } = new MqttConfig();
        public NtripConfig Ntrip { get; set; } = new NtripConfig();
    }

    public class MqttConfig
    {
        public int Port { get; set; } = 1883;
        public AuthConfig Auth { get; set; } = new AuthConfig();
    }

    public class AuthConfig
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class NtripConfig
    {
        public string TargetCasterHost { get; set; } = string.Empty;
        public int TargetCasterPort { get; set; } = 2101;
        public string Mountpoint { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SourceTopic { get; set; } = "ntrip/data";
    }
}
