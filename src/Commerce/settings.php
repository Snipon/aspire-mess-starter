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
