#!/usr/bin/env bash
set -euo pipefail

# VZ native UDP relay installer for Oracle Cloud Ubuntu Always Free VMs.
# Supports both Ampere A1 (aarch64) and AMD E2.1.Micro (x86_64).
# Run: curl -fsSL https://raw.githubusercontent.com/RVVZBRO/windowsvps/vz-neptunnel-dotnet/deploy/oracle/setup-vz-relay.sh | sudo bash

if [[ "\${EUID}" -ne 0 ]]; then
  echo "Run this installer as root (use sudo)." >&2
  exit 1
fi
if ! command -v apt-get >/dev/null 2>&1; then
  echo "This installer supports Ubuntu/Debian images with apt." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl golang-go ufw

ARCH="$(uname -m)"
case "$ARCH" in
  aarch64|arm64|x86_64|amd64) ;;
  *) echo "Unsupported VM architecture: $ARCH" >&2; exit 1 ;;
esac

REPO_URL="https://raw.githubusercontent.com/RVVZBRO/windowsvps/vz-neptunnel-dotnet"
INSTALL_DIR="/opt/vz-relay"
SERVICE_USER="vzrelay"
PORT="45000"

id "$SERVICE_USER" >/dev/null 2>&1 || useradd --system --home "$INSTALL_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
install -d -o root -g root -m 0755 "$INSTALL_DIR"
curl -fsSL "$REPO_URL/relay/main.go" -o "$INSTALL_DIR/main.go"
curl -fsSL "$REPO_URL/relay/go.mod" -o "$INSTALL_DIR/go.mod"

cd "$INSTALL_DIR"
GOTOOLCHAIN=local go build -trimpath -ldflags="-s -w" -o vz-relay .
install -o root -g root -m 0755 vz-relay "$INSTALL_DIR/vz-relay"

cat >/etc/systemd/system/vz-relay.service <<EOF
[Unit]
Description=VZ Multiplayer UDP Relay
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$SERVICE_USER
Group=$SERVICE_USER
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/vz-relay -listen 0.0.0.0:$PORT -max-rooms 128 -max-joiners 12
Restart=always
RestartSec=2
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
LockPersonality=true
MemoryDenyWriteExecute=true
RestrictSUIDSGID=true

[Install]
WantedBy=multi-user.target
EOF

ufw allow OpenSSH
ufw allow "$PORT/udp"
ufw --force enable
systemctl daemon-reload
systemctl enable --now vz-relay
sleep 2
if ! systemctl is-active --quiet vz-relay; then
  journalctl -u vz-relay -n 80 --no-pager || true
  echo "VZ relay did not start. See logs above." >&2
  exit 1
fi

PUBLIC_IP="$(curl -4 -fsS --max-time 10 https://api.ipify.org || true)"
echo
echo "VZ UDP relay is running."
echo "Public endpoint: \${PUBLIC_IP:-YOUR_PUBLIC_IPV4}:$PORT"
echo
echo "NEXT — In Oracle Cloud Networking, add an INGRESS rule:"
echo "  Source: 0.0.0.0/0"
echo "  Protocol: UDP"
echo "  Destination port: $PORT"
echo "The VM firewall is already configured; the Oracle VCN/NSG firewall is separate."
echo
echo "After the Oracle ingress rule is saved, update relay/endpoint.txt in the"
echo "RVVZBRO/windowsvps repository to:"
echo "  \${PUBLIC_IP:-YOUR_PUBLIC_IPV4}:$PORT"
echo "VZ clients will discover that endpoint automatically from the repository."
echo
echo "Service commands:"
echo "  sudo systemctl status vz-relay"
echo "  sudo journalctl -u vz-relay -f"
