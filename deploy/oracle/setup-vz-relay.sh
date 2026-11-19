#!/usr/bin/env bash
set -euo pipefail

# VZ / Porthole relay bootstrap for an Ubuntu OCI Always Free VM.
# Usage: sudo bash setup-vz-relay.sh [public-ip-or-dns]

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this script with sudo: sudo bash $0 [public-ip-or-dns]" >&2
  exit 1
fi

PUBLIC_HOST="${1:-}"
CONTROL_PORT="${PORTHOLE_CONTROL_PORT:-7835}"
MIN_PORT="${PORTHOLE_MIN_PORT:-10000}"
MAX_PORT="${PORTHOLE_MAX_PORT:-20000}"
INSTALL_DIR="/opt/vz-porthole"

if ! command -v apt-get >/dev/null 2>&1; then
  echo "This setup script supports Ubuntu/Debian with apt." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl git openssl ufw

if [[ -z "$PUBLIC_HOST" ]]; then
  PUBLIC_HOST="$(curl -4 -fsS --max-time 8 https://api.ipify.org || true)"
fi
if [[ -z "$PUBLIC_HOST" ]]; then
  echo "Could not detect public IPv4. Re-run with: sudo bash $0 YOUR_PUBLIC_IP" >&2
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL https://get.docker.com -o /tmp/vz-get-docker.sh
  sh /tmp/vz-get-docker.sh
  rm -f /tmp/vz-get-docker.sh
fi
if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose plugin is missing. Install docker-compose-plugin and rerun." >&2
  exit 1
fi

if [[ -d "$INSTALL_DIR/.git" ]]; then
  git -C "$INSTALL_DIR" pull --ff-only
else
  git clone --depth 1 https://github.com/sudo-ds/porthole.git "$INSTALL_DIR"
fi

cd "$INSTALL_DIR"
if [[ ! -f .env ]]; then
  ./scripts/setup-docker-server.sh --public-host "$PUBLIC_HOST"
fi
chmod 600 .env

for entry in "PORTHOLE_CONTROL_PORT=$CONTROL_PORT" "PORTHOLE_MIN_PORT=$MIN_PORT" "PORTHOLE_MAX_PORT=$MAX_PORT"; do
  key="${entry%%=*}"
  value="${entry#*=}"
  if grep -q "^$key=" .env; then
    sed -i "s|^$key=.*|$key=$value|" .env
  else
    printf '%s\n' "$entry" >> .env
  fi
done
chmod 600 .env

# UFW is the guest firewall. Oracle's cloud VCN Security List / NSG must
# separately allow these ports (instructions printed below).
ufw allow OpenSSH
ufw allow "$CONTROL_PORT/tcp"
ufw allow "$MIN_PORT:$MAX_PORT/tcp"
ufw allow "$MIN_PORT:$MAX_PORT/udp"
ufw --force enable

docker compose pull
docker compose up -d
sleep 3
if ! docker compose ps --status running --quiet | grep -q .; then
  docker compose logs --tail=80 porthole || true
  echo "Porthole did not stay running; review logs above." >&2
  exit 1
fi

echo
echo "VZ relay is running at $PUBLIC_HOST"
echo "Control listener: $PUBLIC_HOST:$CONTROL_PORT"
echo "Tunnel port range: $MIN_PORT-$MAX_PORT (TCP and UDP)"
echo
echo "IMPORTANT — Add inbound rules to the Oracle VCN Security List or NSG:"
echo "  TCP $CONTROL_PORT"
echo "  TCP $MIN_PORT-$MAX_PORT"
echo "  UDP $MIN_PORT-$MAX_PORT"
echo "Use source 0.0.0.0/0 for tunnel traffic. Restrict the control port and SSH where practical."
echo
echo "After saving the Oracle rules, print the private VZ relay code:"
echo "  cd $INSTALL_DIR && sudo docker compose run --rm porthole invite"
echo
echo "Paste the porthole1_... code into VZ > Host Session. Treat it like a password."
