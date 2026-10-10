#!/bin/bash
# First boot of the public test build's game server, on Amazon Linux 2023 (ARM). tools/shapeland-testbuild.sh
# fills in the {{…}} values and passes this as the instance's user data; its output goes to the console log
# (tools/shapeland-testbuild.sh log). The instance has no AWS permissions: the package comes through a pre-signed link.
set -euxo pipefail

HOST='{{HOST}}'
ACME_EMAIL='{{ACME_EMAIL}}'
PACKAGE_URL='{{PACKAGE_URL}}'
HOURS='{{HOURS}}'
CADDY_VERSION=2.10.0
CADDY_SHA512=6d100bfd609e8cfe6a51afe1b86066ac68f53ac56670c74a7d7537d93c643aa7f0e82b9f3083218eee1d369a427bfcdafcb445c7a730f9fc0ddf546401d95484

# A safety net against a forgotten server: shut down (which terminates it) after the given hours; 0 for never.
if [ "$HOURS" -gt 0 ]; then
  shutdown -h "+$((HOURS * 60))"
fi

useradd --system --home-dir /opt/shapeland --shell /sbin/nologin shapeland
useradd --system --home-dir /var/lib/caddy --create-home --shell /sbin/nologin caddy
mkdir -p /opt/shapeland
curl -fsSL "$PACKAGE_URL" | tar -xz -C /opt/shapeland
chown -R shapeland: /opt/shapeland

curl -fsSL -o /tmp/caddy.tar.gz "https://github.com/caddyserver/caddy/releases/download/v$CADDY_VERSION/caddy_${CADDY_VERSION}_linux_arm64.tar.gz"
echo "$CADDY_SHA512  /tmp/caddy.tar.gz" | sha512sum -c -
tar -xzf /tmp/caddy.tar.gz -C /usr/local/bin caddy

cat > /etc/systemd/system/shapeland.service <<UNIT
[Unit]
Description=ShapeLand game server
After=network-online.target

[Service]
User=shapeland
WorkingDirectory=/opt/shapeland
ExecStart=/opt/shapeland/server/ShapeLand.GameServer --urls http://127.0.0.1:5080 --ShapeLand:ContentFile=/opt/shapeland/content.bin
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
Restart=on-failure

[Install]
WantedBy=multi-user.target
UNIT

cat > /etc/systemd/system/caddy.service <<UNIT
[Unit]
Description=Caddy, TLS for the ShapeLand game server
After=network-online.target shapeland.service

[Service]
User=caddy
Environment=SHAPELAND_HOST=$HOST ACME_EMAIL=$ACME_EMAIL XDG_DATA_HOME=/var/lib/caddy XDG_CONFIG_HOME=/var/lib/caddy
ExecStart=/usr/local/bin/caddy run --config /opt/shapeland/Caddyfile --adapter caddyfile
AmbientCapabilities=CAP_NET_BIND_SERVICE
Restart=on-failure

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now shapeland.service

# Caddy asks for a certificate as it starts, which only works once the hostname points here: wait for the
# script's DNS update (up to 15 minutes) rather than start into backoff.
ip=$(TOKEN=$(curl -fsS -X PUT -H 'X-aws-ec2-metadata-token-ttl-seconds: 60' http://169.254.169.254/latest/api/token) \
  && curl -fsS -H "X-aws-ec2-metadata-token: $TOKEN" http://169.254.169.254/latest/meta-data/public-ipv4)
for _ in $(seq 1 90); do
  if getent ahostsv4 "$HOST" | grep -q "^$ip "; then
    break
  fi
  sleep 10
done
systemctl enable --now caddy.service
echo "shapeland-testbuild: ready"
