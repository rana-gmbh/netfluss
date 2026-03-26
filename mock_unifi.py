#!/usr/bin/env python3
"""
Mock UniFi Controller API for testing Netfluss UniFi bandwidth monitoring.

Simulates a UDM-style UniFi OS controller on https://localhost:8443
with self-signed TLS (matching what real UniFi devices use).

Usage:
    python3 mock_unifi.py

Credentials: admin / admin
Then in Netfluss preferences, set UniFi host to "localhost:8443" and
configure credentials as admin/admin.
"""

import http.server
import json
import math
import random
import ssl
import subprocess
import sys
import tempfile
import time
import os
import uuid

# --- Config ---
HOST = "127.0.0.1"
PORT = 8443
USERNAME = "admin"
PASSWORD = "admin"

# Simulated bandwidth (oscillates to look realistic)
BASE_RX_RATE = 5_000_000   # ~5 MB/s base download
BASE_TX_RATE = 500_000     # ~500 KB/s base upload
MAX_SPEED_MBPS = 1000      # 1 Gbps link

sessions = {}  # token -> expiry


def generate_bandwidth():
    """Generate realistic-looking bandwidth with some variation."""
    t = time.time()
    # Sine wave + noise for organic-looking traffic
    rx_factor = 1.0 + 0.4 * math.sin(t / 3.0) + random.uniform(-0.1, 0.1)
    tx_factor = 1.0 + 0.3 * math.sin(t / 5.0) + random.uniform(-0.1, 0.1)
    # Occasional spike
    if random.random() < 0.05:
        rx_factor *= random.uniform(2.0, 4.0)
    return {
        "rx_bytes-r": max(0, BASE_RX_RATE * rx_factor),
        "tx_bytes-r": max(0, BASE_TX_RATE * tx_factor),
    }


class UniFiHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        print(f"  [{self.command}] {args[0]}" if args else "")

    def send_json(self, data, status=200, cookies=None):
        body = json.dumps(data).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        if cookies:
            for c in cookies:
                self.send_header("Set-Cookie", c)
        self.end_headers()
        self.wfile.write(body)

    def check_auth(self):
        cookie = self.headers.get("Cookie", "")
        for part in cookie.split(";"):
            part = part.strip()
            if part.startswith("TOKEN="):
                token = part[6:]
                if token in sessions and sessions[token] > time.time():
                    return True
        return False

    def do_POST(self):
        content_length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(content_length) if content_length else b""

        # --- Login endpoint (UniFi OS style) ---
        if self.path == "/api/auth/login":
            try:
                data = json.loads(body)
            except json.JSONDecodeError:
                self.send_json({"meta": {"rc": "error", "msg": "bad request"}}, 400)
                return

            if data.get("username") == USERNAME and data.get("password") == PASSWORD:
                token = uuid.uuid4().hex
                sessions[token] = time.time() + 1800  # 30 min
                print(f"  Login OK — token: {token[:8]}...")
                self.send_json(
                    {"meta": {"rc": "ok"}, "data": []},
                    200,
                    cookies=[f"TOKEN={token}; Path=/; HttpOnly; Secure"],
                )
            else:
                print(f"  Login FAILED — bad credentials")
                self.send_json(
                    {"meta": {"rc": "error", "msg": "api.err.Invalid"}}, 401
                )
            return

        # --- Logout ---
        if self.path == "/api/auth/logout":
            self.send_json({"meta": {"rc": "ok"}, "data": []})
            return

        self.send_json({"meta": {"rc": "error", "msg": "not found"}}, 404)

    def do_GET(self):
        # --- Device stats (UniFi OS proxy path) ---
        if self.path == "/proxy/network/api/s/default/stat/device":
            if not self.check_auth():
                self.send_json(
                    {"meta": {"rc": "error", "msg": "api.err.LoginRequired"}}, 401
                )
                return

            bw = generate_bandwidth()
            uptime = int(time.time()) % 1_000_000

            device_data = {
                "meta": {"rc": "ok"},
                "data": [
                    {
                        "type": "udm",
                        "model": "UDM",
                        "name": "Mock UDM Pro",
                        "serial": "MOCK000000001",
                        "version": "7.1.68",
                        "adopted": True,
                        "state": 1,
                        "sys_stats": {
                            "cpu": round(random.uniform(5, 25), 1),
                            "mem": round(random.uniform(30, 60), 1),
                            "uptime": uptime,
                        },
                        "uplink": {
                            "type": "wire",
                            "up": True,
                            "speed": MAX_SPEED_MBPS,
                            "full_duplex": True,
                            "rx_bytes": int(time.time() * BASE_RX_RATE / 10),
                            "tx_bytes": int(time.time() * BASE_TX_RATE / 10),
                            "rx_bytes-r": bw["rx_bytes-r"],
                            "tx_bytes-r": bw["tx_bytes-r"],
                        },
                        "wan1": {
                            "up": True,
                            "type": "dhcp",
                            "ip": "203.0.113.42",
                            "gateway": "203.0.113.1",
                            "max_speed": MAX_SPEED_MBPS,
                            "rx_bytes": int(time.time() * BASE_RX_RATE / 10),
                            "tx_bytes": int(time.time() * BASE_TX_RATE / 10),
                            "rx_bytes-r": bw["rx_bytes-r"],
                            "tx_bytes-r": bw["tx_bytes-r"],
                        },
                        "port_table": [
                            {
                                "name": "WAN",
                                "ifname": "eth8",
                                "up": True,
                                "speed": MAX_SPEED_MBPS,
                                "full_duplex": True,
                            }
                        ],
                    },
                    # Also include an AP so the gateway filter is tested
                    {
                        "type": "uap",
                        "model": "U6LR",
                        "name": "Mock AP",
                        "state": 1,
                    },
                ],
            }

            bw_mbps_rx = bw["rx_bytes-r"] / 1_000_000 * 8
            bw_mbps_tx = bw["tx_bytes-r"] / 1_000_000 * 8
            print(
                f"  → Down: {bw_mbps_rx:.1f} Mb/s  Up: {bw_mbps_tx:.1f} Mb/s"
            )

            self.send_json(device_data)
            return

        # --- Health endpoint (bonus) ---
        if self.path == "/proxy/network/api/s/default/stat/health":
            if not self.check_auth():
                self.send_json(
                    {"meta": {"rc": "error", "msg": "api.err.LoginRequired"}}, 401
                )
                return
            bw = generate_bandwidth()
            self.send_json(
                {
                    "meta": {"rc": "ok"},
                    "data": [
                        {
                            "subsystem": "wan",
                            "status": "ok",
                            "rx_bytes-r": bw["rx_bytes-r"],
                            "tx_bytes-r": bw["tx_bytes-r"],
                        }
                    ],
                }
            )
            return

        self.send_json({"meta": {"rc": "error", "msg": "not found"}}, 404)


def generate_self_signed_cert(certfile, keyfile):
    """Generate a self-signed cert using openssl."""
    subprocess.run(
        [
            "openssl", "req", "-x509", "-newkey", "rsa:2048",
            "-keyout", keyfile, "-out", certfile,
            "-days", "1", "-nodes",
            "-subj", "/CN=localhost",
        ],
        capture_output=True,
        check=True,
    )


def main():
    tmpdir = tempfile.mkdtemp()
    certfile = os.path.join(tmpdir, "cert.pem")
    keyfile = os.path.join(tmpdir, "key.pem")

    print("Generating self-signed certificate...")
    generate_self_signed_cert(certfile, keyfile)

    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(certfile, keyfile)

    server = http.server.HTTPServer((HOST, PORT), UniFiHandler)
    server.socket = ctx.wrap_socket(server.socket, server_side=True)

    print(f"""
╔══════════════════════════════════════════════════╗
║         Mock UniFi Controller Running            ║
╠══════════════════════════════════════════════════╣
║  URL:         https://localhost:{PORT}              ║
║  Username:    {USERNAME:<36s}║
║  Password:    {PASSWORD:<36s}║
║                                                  ║
║  In Netfluss Preferences:                        ║
║    1. Enable "UniFi Bandwidth"                   ║
║    2. Set host to: localhost:{PORT}                 ║
║    3. Set credentials: {USERNAME} / {PASSWORD}              ║
╚══════════════════════════════════════════════════╝
""")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down mock server.")
        server.shutdown()
    finally:
        os.unlink(certfile)
        os.unlink(keyfile)
        os.rmdir(tmpdir)


if __name__ == "__main__":
    main()
