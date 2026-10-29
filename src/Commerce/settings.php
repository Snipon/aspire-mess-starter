<?php

/**
 * @file
 * Drupal settings from the environment (apphost.cs): the database from the "commercedb" reference, secrets from
 * Aspire parameters. Nothing secret is stored in the image.
 */

$databases['default']['default'] = [
  'driver' => 'pgsql',
  'namespace' => 'Drupal\\pgsql\\Driver\\Database\\pgsql',
  'autoload' => 'core/modules/pgsql/src/Driver/Database/pgsql/',
  'host' => getenv('COMMERCEDB_HOST'),
  'port' => getenv('COMMERCEDB_PORT'),
  'database' => getenv('COMMERCEDB_DATABASENAME'),
  'username' => getenv('COMMERCEDB_USERNAME'),
  'password' => getenv('COMMERCEDB_PASSWORD'),
  'prefix' => '',
];

$settings['hash_salt'] = getenv('DRUPAL_HASH_SALT');
$settings['config_sync_directory'] = '../config/sync';
$settings['file_public_path'] = 'sites/default/files';
$settings['update_free_access'] = FALSE;
$settings['skip_permissions_hardening'] = TRUE;

// Requests come from the API (server side) and from browsers loading product images, both via localhost locally.
$settings['trusted_host_patterns'] = array_values(array_filter(explode(',', getenv('DRUPAL_TRUSTED_HOSTS') ?: '^localhost$,^127\.0\.0\.1$')));

// Behind a TLS-terminating proxy (Azure Container Apps ingress): trust its X-Forwarded-* headers so absolute URLs, such
// as the OIDC redirect URI, use https and the public host. Only the ingress can reach the container.
if (getenv('DRUPAL_REVERSE_PROXY') === 'true') {
  $settings['reverse_proxy'] = TRUE;
  $settings['reverse_proxy_addresses'] = [$_SERVER['REMOTE_ADDR'] ?? '127.0.0.1'];
  $settings['reverse_proxy_trusted_headers'] = \Symfony\Component\HttpFoundation\Request::HEADER_X_FORWARDED_FOR
    | \Symfony\Component\HttpFoundation\Request::HEADER_X_FORWARDED_HOST
    | \Symfony\Component\HttpFoundation\Request::HEADER_X_FORWARDED_PORT
    | \Symfony\Component\HttpFoundation\Request::HEADER_X_FORWARDED_PROTO;
}

// Staff sign-in through the identity provider (openid_connect client "sso", created by headless_commerce's Schema; any
// OIDC provider with a "roles" claim, admin => Drupal administrator). Off without OIDC_CLIENT_ID: the client is disabled
// and the password form stays. On: the login form is only the IdP button (drush uli still works). Endpoints are set
// explicitly, not discovered: the authorization and end session endpoints are browser-facing, the token endpoint is
// called from this container. Claims come from the ID token (no userinfo endpoint).
$oidcClientId = getenv('OIDC_CLIENT_ID') ?: '';
$config['openid_connect.client.sso']['status'] = $oidcClientId !== '';
if ($oidcClientId !== '') {
  $config['openid_connect.client.sso']['settings']['client_id'] = $oidcClientId;
  $config['openid_connect.client.sso']['settings']['client_secret'] = getenv('OIDC_CLIENT_SECRET') ?: '';
  $config['openid_connect.client.sso']['settings']['authorization_endpoint'] = getenv('OIDC_AUTHORIZATION_ENDPOINT') ?: '';
  $config['openid_connect.client.sso']['settings']['token_endpoint'] = getenv('OIDC_TOKEN_ENDPOINT') ?: '';
  $config['openid_connect.client.sso']['settings']['end_session_endpoint'] = getenv('OIDC_END_SESSION_ENDPOINT') ?: '';
  $config['openid_connect.settings']['user_login_display'] = 'force_replace';
  // Accounts are created on first sign-in only (override_registration_settings); nobody registers on Drupal itself.
  $config['user.settings']['register'] = 'admin_only';
}
