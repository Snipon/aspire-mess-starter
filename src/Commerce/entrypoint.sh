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

# Drupal's HTTP client (PHP curl) reads only the system CA bundle, not SSL_CERT_DIR, where Aspire adds the CA of its dev
# certificate (the local identity provider's TLS). Add those certificates to the bundle; elsewhere there are none.
aspire_cas=(/usr/lib/ssl/aspire/certs/*.pem)
if [[ -f ${aspire_cas[0]} ]]; then
  for pem in "${aspire_cas[@]}"; do cp "$pem" "/usr/local/share/ca-certificates/aspire-$(basename "$pem" .pem).crt"; done
  update-ca-certificates >/dev/null 2>&1
fi

for _ in $(seq 1 60); do db 2>/dev/null && break; sleep 2; done
db 'CREATE EXTENSION IF NOT EXISTS pg_trgm' # required by Drupal on PostgreSQL

if ! drush status --field=bootstrap 2>/dev/null | grep -q Successful; then
  echo "Installing Drupal"
  drush site:install minimal --yes --site-name="Dev Gear Store" \
    --account-name=admin --account-mail="${DRUPAL_ADMIN_EMAIL:-admin@example.com}" --account-pass="$DRUPAL_ADMIN_PASSWORD"
fi

drush pm:install --yes headless_commerce
drush updatedb --yes

if [[ -n ${OIDC_CLIENT_ID:-} ]]; then
  # Staff sign in through the IdP, which never takes over an existing account by email. The local admin (uid 1) stays
  # as a break-glass account for drush uli, so free its email for the same person's IdP account.
  drush php:eval '$admin = \Drupal\user\Entity\User::load(1); if ($admin->getEmail() !== "admin@localhost") { $admin->setEmail("admin@localhost")->save(); }'
fi

drush cache:rebuild

if [[ ${DEMO_CONTENT:-false} == true ]]; then
  drush headless-commerce:seed
fi

chown -R www-data:www-data web/sites/default/files
exec docker-php-entrypoint "$@"
