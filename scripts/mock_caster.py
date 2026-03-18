"""Simple mock NTRIP Caster for testing. Accepts SOURCE handshake, prints received data."""
import socket
import sys
import threading

def handle_client(conn, addr):
    print(f"[Caster] Connection from {addr}", flush=True)
    data = conn.recv(1024)
    print(f"[Caster] Handshake: {data.decode('ascii', errors='replace').strip()}", flush=True)
    conn.sendall(b"ICY 200 OK\r\n\r\n")
    print("[Caster] Sent ICY 200 OK", flush=True)
    try:
        while True:
            chunk = conn.recv(4096)
            if not chunk:
                break
            print(f"[Caster] Received {len(chunk)} bytes: {chunk.hex()}", flush=True)
    except Exception as e:
        print(f"[Caster] Error: {e}", flush=True)
    finally:
        conn.close()
        print("[Caster] Connection closed", flush=True)

srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(("0.0.0.0", 2101))
srv.listen(1)
print("[Caster] Listening on port 2101...", flush=True)
conn, addr = srv.accept()
handle_client(conn, addr)
