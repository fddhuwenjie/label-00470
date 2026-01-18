import time
import sys
import random

try:
    import paho.mqtt.client as mqtt
except ImportError:
    print("需要安装 paho-mqtt 库")
    print("请运行: pip install paho-mqtt")
    sys.exit(1)

# 配置
BROKER = "localhost"
PORT = 9999
TOPIC = "ntrip/data"
USERNAME = "admin"
PASSWORD = "password"

def on_connect(client, userdata, flags, rc):
    if rc == 0:
        print(f"已连接到 MQTT Broker: {BROKER}:{PORT}")
    else:
        print(f"连接失败，代码: {rc}")

def on_publish(client, userdata, mid):
    print(f"数据发送成功 (Message ID: {mid})")

def main():
    client = mqtt.Client()
    client.username_pw_set(USERNAME, PASSWORD)
    client.on_connect = on_connect
    client.on_publish = on_publish

    print(f"正在连接到 {BROKER}...")
    try:
        client.connect(BROKER, PORT, 60)
        client.loop_start()
    except Exception as e:
        print(f"无法连接到 MQTT Broker: {e}")
        print("请确保 Docker 容器已启动 (docker-compose up -d)")
        return

    try:
        while True:
            # 模拟 RTCM 数据 (随机字节)
            dummy_data = bytearray(random.getrandbits(8) for _ in range(20))
            print(f"正在发送模拟 RTCM 数据: {dummy_data.hex()} 到主题 {TOPIC}")
            
            client.publish(TOPIC, dummy_data)
            
            time.sleep(2)
    except KeyboardInterrupt:
        print("\n停止发送")
        client.loop_stop()
        client.disconnect()

if __name__ == "__main__":
    main()
