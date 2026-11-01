#!/usr/bin/env bash
# The shared development PostgreSQL: one container, comet-postgres, for every local stack (the dev script, the
# editor's local server, Unity's play-mode tests, each worktree), each with its own database in it. It listens on
# 127.0.0.1:55432 (apart from a PostgreSQL of your own on 5432), keeps its data in the comet-postgres-data volume,
# and stays running. It's also on the Docker network "comet", where containers (the --net runs) reach it as
# comet-postgres:5432. The password is fixed and local only. .NET tests don't use it: they start their own.
#
#   tools/comet-postgres.sh start                 start it if it isn't running, and wait until it answers
#   tools/comet-postgres.sh database NAME         start it, make the database NAME if missing, and print its
#                                                 connection string (for programs on this machine)
#   tools/comet-postgres.sh fresh NAME            the same, emptying the database first
#   tools/comet-postgres.sh drop NAME             drop the database NAME
#   tools/comet-postgres.sh name CHECKOUT PORT    a database name for a stack: the checkout's folder and its port
set -euo pipefail

container=comet-postgres
volume=comet-postgres-data
network=comet
port=55432
user=comet
password=comet-dev-only

start() {
  command -v docker > /dev/null || { echo "Docker is needed for local play (PostgreSQL runs in it)." >&2; exit 1; }
  docker network inspect "$network" > /dev/null 2>&1 || docker network create "$network" > /dev/null
  if [ -z "$(docker ps -q --filter "name=^${container}$")" ]; then
    if [ -n "$(docker ps -aq --filter "name=^${container}$")" ]; then
      docker start "$container" > /dev/null
    else
      docker run -d --name "$container" --restart unless-stopped --network "$network" \
        -p "127.0.0.1:$port:5432" -v "$volume:/var/lib/postgresql" \
        -e POSTGRES_USER="$user" -e POSTGRES_PASSWORD="$password" postgres:18 > /dev/null
    fi
  fi

  for _ in $(seq 1 60); do
    docker exec "$container" pg_isready -q -U "$user" 2> /dev/null && return 0
    sleep 0.5
  done
  echo "comet-postgres didn't start; see docker logs $container." >&2
  exit 1
}

psql() {
  docker exec -i "$container" psql -q -v ON_ERROR_STOP=1 -U "$user" -d postgres "$@"
}

check_name() {
  [[ "$1" =~ ^[a-z_][a-z0-9_]*$ ]] || { echo "Database names here are lowercase letters, digits and '_': $1" >&2; exit 2; }
}

connection() {
  echo "Host=127.0.0.1;Port=$port;Database=$1;Username=$user;Password=$password"
}

case "${1:-}" in
  start)
    start
    ;;
  database|fresh)
    check_name "${2:?a database name}"
    start
    if [ "$1" = fresh ]; then
      psql -c "DROP DATABASE IF EXISTS $2 WITH (FORCE)"
    fi
    # The comet user owns it, as the Login server needs (its migrations make a collation in public).
    [ "$(psql -tAc "SELECT 1 FROM pg_database WHERE datname = '$2'")" = 1 ] || psql -c "CREATE DATABASE $2"
    connection "$2"
    ;;
  drop)
    check_name "${2:?a database name}"
    start
    psql -c "DROP DATABASE IF EXISTS $2 WITH (FORCE)"
    ;;
  name)
    folder=$(basename "${2:?a checkout folder}" | tr '[:upper:]' '[:lower:]' | tr -c 'a-z0-9\n' '_')
    echo "shapeland_${folder}_${3:?a port}"
    ;;
  *)
    awk 'NR > 1 && !/^#/ { exit } NR > 1 { sub(/^# ?/, ""); print }' "$0"
    exit 2
    ;;
esac
