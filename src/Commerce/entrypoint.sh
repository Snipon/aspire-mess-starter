#!/usr/bin/env bash
# Runs on every container start, idempotently: installs Drupal into an empty database, installs/updates the
# headless_commerce module (store, product fields, fake card gateway), seeds the demo catalog when DEMO_CONTENT=true,
# then starts Apache.
set -euo pipefail
cd /opt/drupal

db() {
  php -r '
    $dsn = sprintf("pgsql:host=%s;port=%s;dbname=%s", getenv("COMMERCEDB_HOST"), getenv("COMMERCEDB_PORT"), getenv("COMMERCEDB_DATABASENAME"));
    $pdo = new PDO($dsn, getenv("COMMERCEDB_USERNAME"), getenv("COMMERCEDB_PASSWORD"), [PDO::ATTR_TIMEOUT => 3]);
    if (isset($argv[1])) { $pdo->exec($argv[1]); }
  ' "$@"
}

for _ in $(seq 1 60); do db 2>/dev/null && break; sleep 2; done
db 'CREATE EXTENSION IF NOT EXISTS pg_trgm' # required by Drupal on PostgreSQL

if ! drush status --field=bootstrap 2>/dev/null | grep -q Successful; then
  echo "Installing Drupal"
  drush site:install minimal --yes --site-name="Dev Gear Store" \
    --account-name=admin --account-mail="${DRUPAL_ADMIN_EMAIL:-admin@example.com}" --account-pass="$DRUPAL_ADMIN_PASSWORD"
fi

drush pm:install --yes headless_commerce
drush updatedb --yes
drush cache:rebuild

if [[ ${DEMO_CONTENT:-false} == true ]]; then
  drush headless-commerce:seed
fi

chown -R www-data:www-data web/sites/default/files
exec docker-php-entrypoint "$@"
