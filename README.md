# MQTT & NTRIP 网关

这是一个高性能的 C# 控制台应用程序，作为双协议网关使用：
1.  **MQTT Broker**：接收来自 IoT 设备的 RTCM 数据。
2.  **NTRIP Source**：将接收到的数据转发给标准的 NTRIP Caster。

## 前置要求
- .NET 6.0 SDK 或更高版本 (本地开发)。
- Docker & Docker Compose (容器化部署)。
- 外部 NTRIP Caster（例如 SNIP、BKG NtripCaster），或者如果进行了扩展，使用内置逻辑。

## 快速开始

### 方式一：Docker Compose (推荐)

1.  **配置**：
    编辑 `backend/appsettings.json` 以设置您的 MQTT 凭据和目标 NTRIP Caster 详细信息。

2.  **运行**：
    在项目根目录下执行：
    ```bash
    docker-compose up --build -d
    ```

3.  **查看日志**：
    ```bash
    docker-compose logs -f
    ```

### 方式二：本地 .NET 环境

1.  **配置**：
    编辑 `backend/appsettings.json` 以设置您的 MQTT 凭据和目标 NTRIP Caster 详细信息。

    ```json
    {
      "Mqtt": {
        "Port": 1883,
        "Auth": { "Username": "admin", "Password": "password" }
      },
      "Ntrip": {
        "TargetCasterHost": "caster.example.com",
        "TargetCasterPort": 2101,
        "Mountpoint": "STR01",
        "Password": "source_password"
      }
    }
    ```

2.  **运行**：
    ```bash
    cd backend
    dotnet run
    ```

3.  **验证**：
    -   控制台输出将显示 "MQTT Broker started"（MQTT Broker 已启动）和 "Connected to NTRIP Caster"（已连接到 NTRIP Caster）。
    -   向 MQTT 主题 `ntrip/data` 发布数据，并验证数据是否出现在 Caster 上。

## 项目结构
-   `backend/`：源代码及 Dockerfile。
-   `docs/`：设计文档。
-   `docker-compose.yml`：Docker 编排文件。

## 许可证
MIT
