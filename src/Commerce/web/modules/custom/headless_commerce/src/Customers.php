<?php

declare(strict_types=1);

namespace Drupal\headless_commerce;

use Drupal\Core\Entity\EntityTypeManagerInterface;
use Drupal\user\UserInterface;

/**
 * Shop customers sign in at the identity provider, not in Drupal. Each one owns their orders through a Drupal user
 * keyed by the provider's subject: named "shop-" plus a hash of the subject (fixed length, any subject format), blocked
 * and without roles or password, so it can never sign in here. The customer's email (self-asserted at registration) goes
 * into the user's initial email, not its email: staff sign-in may match accounts on the email.
 */
final class Customers {

  public function __construct(private readonly EntityTypeManagerInterface $entityTypeManager) {}

  public static function isValidSubject(string $subject): bool {
    return $subject !== '' && strlen($subject) <= 255 && !preg_match('/[\x00-\x1F\x7F]/', $subject);
  }

  public function find(string $subject): ?UserInterface {
    $users = $this->entityTypeManager->getStorage('user')->loadByProperties(['name' => self::name($subject)]);
    return reset($users) ?: NULL;
  }

  public function findOrCreate(string $subject, string $email): UserInterface {
    if ($user = $this->find($subject)) {
      return $user;
    }

    /** @var \Drupal\user\UserInterface $user */
    $user = $this->entityTypeManager->getStorage('user')->create([
      'name' => self::name($subject),
      'init' => filter_var($email, FILTER_VALIDATE_EMAIL) ? $email : '',
      'status' => 0,
    ]);
    $user->save();
    return $user;
  }

  private static function name(string $subject): string {
    return 'shop-' . substr(hash('sha256', $subject), 0, 40);
  }

}
