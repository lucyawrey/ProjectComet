#!/bin/bash
# First boot of the public test build's server, on Amazon Linux 2023 (ARM): PostgreSQL, the Login server and the game
# server behind Caddy. tools/shapeland-testbuild.sh fills in the {{…}} values, passes this as the instance's user data
# and attaches the kept data volume once the instance runs; the output goes to the console log
# (tools/shapeland-testbuild.sh log). The instance has no AWS permissions: the package comes through a pre-signed link.
set -euxo pipefail

HOST='{{HOST}}'
PAGE_ORIGIN='{{PAGE_ORIGIN}}'
ACME_EMAIL='{{ACME_EMAIL}}'
PACKAGE_URL='{{PACKAGE_URL}}'
VOLUME_ID='{{VOLUME_ID}}'
HOURS='{{HOURS}}'
BOTS='{{BOTS}}'
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

dnf install -y postgresql16-server

# The kept data volume, which outlives the instance: PostgreSQL's data, its password, the ticket signing key and the
# bots' tokens. The script attaches it once the instance runs; it's formatted only the first time.
device="/dev/disk/by-id/nvme-Amazon_Elastic_Block_Store_${VOLUME_ID//-/}"
for _ in $(seq 1 60); do
  [ -e "$device" ] && break
  sleep 5
done
[ -e "$device" ] || { echo "shapeland-testbuild: the data volume never appeared"; exit 1; }
blkid "$device" || mkfs.ext4 -L shapeland-data "$device"
mkdir -p /data
echo "$device /data ext4 defaults,nofail 0 2" >> /etc/fstab
mount /data
mkdir -p /data/postgres /data/secrets /data/keys /data/bot-tokens
chown postgres: /data/postgres && chmod 700 /data/postgres
chown shapeland: /data/keys /data/bot-tokens && chmod 700 /data/keys
chmod 700 /data/secrets

# PostgreSQL on the volume, listening on this machine only, tuned small for the instance's memory.
mkdir -p /etc/systemd/system/postgresql.service.d
cat > /etc/systemd/system/postgresql.service.d/data.conf <<UNIT
[Service]
Environment=PGDATA=/data/postgres
UNIT
systemctl daemon-reload
if [ ! -f /data/postgres/PG_VERSION ]; then
  runuser -u postgres -- initdb -D /data/postgres -E UTF8 --auth-local=peer --auth-host=scram-sha-256
  cat >> /data/postgres/postgresql.conf <<CONF
listen_addresses = 'localhost'
max_connections = 40
shared_buffers = 64MB
effective_cache_size = 256MB
work_mem = 2MB
maintenance_work_mem = 32MB
CONF
fi
systemctl enable --now postgresql.service

# The database and its owner (the Login server migrates as the owner), made on the first boot with a random password
# kept on the volume. Not traced, so the password stays out of the console log.
set +x
if [ ! -f /data/secrets/database-password ]; then
  head -c 32 /dev/urandom | base64 | tr -d '/+=' > /data/secrets/database-password
  runuser -u postgres -- psql -v ON_ERROR_STOP=1 \
    -c "CREATE ROLE shapeland LOGIN PASSWORD '$(cat /data/secrets/database-password)'" \
    -c "CREATE DATABASE shapeland OWNER shapeland"
fi
connection="Host=127.0.0.1;Database=shapeland;Username=shapeland;Password=$(cat /data/secrets/database-password)"
printf 'Login__ConnectionString=%s\nShapeLand__ConnectionString=%s\n' "$connection" "$connection" > /data/secrets/servers.env
chown root:shapeland /data/secrets/servers.env && chmod 640 /data/secrets/servers.env
chmod 711 /data/secrets
set -x

# The Login server: accounts and join tickets. It makes the ticket signing key on its first start (kept on the
# volume, so tickets stay valid across redeploys); the game server reads only the public key. Requests through Caddy
# carry the player's address, so only the bots here (127.0.0.1 directly) skip the rate limits.
cat > /etc/systemd/system/shapeland-login.service <<UNIT
[Unit]
Description=ShapeLand Login server
After=network-online.target postgresql.service
Requires=postgresql.service

[Service]
User=shapeland
WorkingDirectory=/opt/shapeland
EnvironmentFile=/data/secrets/servers.env
ExecStart=/opt/shapeland/login/ShapeLand.LoginServer --urls http://127.0.0.1:5090 \
  --ShapeLand:ContentFile=/opt/shapeland/content.bin --ShapeLand:GameServer:Address=wss://$HOST/ws \
  --Login:SigningKeyFile=/data/keys/login.pem --Login:PublicKeysFolder=/data/keys/public --Login:CreateSigningKey=true \
  --Login:AllowedOrigins:0=$PAGE_ORIGIN --Login:RateLimits:ExemptAddresses:0=127.0.0.1 --Login:RateLimits:ExemptAddresses:1=::1
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
UNIT

# The game server, after the Login server (which makes the keys it reads); it restarts until they're there.
# Ordered after PostgreSQL too, so on down or redeploy it stops (and saves its players) before the database does.
cat > /etc/systemd/system/shapeland.service <<UNIT
[Unit]
Description=ShapeLand game server
After=network-online.target postgresql.service shapeland-login.service
Wants=postgresql.service

[Service]
User=shapeland
WorkingDirectory=/opt/shapeland
EnvironmentFile=/data/secrets/servers.env
ExecStart=/opt/shapeland/server/ShapeLand.GameServer --urls http://127.0.0.1:5080 \
  --ShapeLand:ContentFile=/opt/shapeland/content.bin --ShapeLand:TicketKeysFolder=/data/keys/public
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
UNIT

cat > /etc/systemd/system/caddy.service <<UNIT
[Unit]
Description=Caddy, TLS for the ShapeLand Login and game servers
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

# Honest bots on the same instance, so visitors see someone moving: guest accounts whose tokens stay on the volume,
# joining this machine's game server directly. Restarted if they stop (as when the server does).
if [ "$BOTS" -gt 0 ]; then
  cat > /etc/systemd/system/shapeland-bots.service <<UNIT
[Unit]
Description=ShapeLand bots
After=shapeland.service

[Service]
User=shapeland
WorkingDirectory=/opt/shapeland
ExecStart=/opt/shapeland/bots/ShapeLand.Bots --login http://127.0.0.1:5090 --tokens /data/bot-tokens \
  --server ws://127.0.0.1:5080/ws --count $BOTS --cheaters 0 --content /opt/shapeland/content.bin
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
UNIT
fi

systemctl daemon-reload
systemctl enable --now shapeland-login.service shapeland.service
if [ "$BOTS" -gt 0 ]; then
  systemctl enable --now shapeland-bots.service
fi

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
