<?php

declare(strict_types=1);

namespace Drupal\headless_commerce\Routing;

use Drupal\Core\Routing\RouteSubscriberBase;
use Symfony\Component\Routing\RouteCollection;

/**
 * With staff sign-in through the identity provider (OIDC_CLIENT_ID, see settings.php) Drupal passwords are not used:
 * the password reset form and the JSON login are closed (openid_connect replaces the login form). One-time login links
 * (drush uli, user.reset*) keep working for the local admin.
 */
final class RouteSubscriber extends RouteSubscriberBase {

  protected function alterRoutes(RouteCollection $collection): void {
    if (!getenv('OIDC_CLIENT_ID')) {
      return;
    }

    foreach (['user.pass', 'user.pass.http', 'user.login.http'] as $name) {
      $collection->get($name)?->setRequirements(['_access' => 'FALSE']);
    }
  }

}
