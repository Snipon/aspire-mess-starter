<?php

declare(strict_types=1);

namespace Drupal\headless_commerce;

/**
 * The fake card processor: validates the card like a real form would and then decides with fixed test numbers.
 * No card data is stored; the order only keeps the last four digits on its payment's remote id.
 *
 *   4242 4242 4242 4242   approved (any other valid number too)
 *   4000 0000 0000 0002   declined: card_declined
 *   4000 0000 0000 9995   declined: insufficient_funds
 *   4000 0000 0000 0069   declined: expired_card
 */
final class FakeCard {

  public const DECLINES = [
    '4000000000000002' => ['card_declined', 'The card was declined.'],
    '4000000000009995' => ['insufficient_funds', 'The card has insufficient funds.'],
    '4000000000000069' => ['expired_card', 'The card has expired.'],
  ];

  /**
   * Field errors keyed by field name (number, expiry, cvc); empty when the card is well-formed.
   */
  public static function validate(string $number, int $expMonth, int $expYear, string $cvc, ?\DateTimeImmutable $now = NULL): array {
    $now ??= new \DateTimeImmutable('now', new \DateTimeZone('UTC'));
    $errors = [];
    if (!preg_match('/^\d{13,19}$/', $number) || !self::luhn($number)) {
      $errors['number'] = 'Enter a valid card number.';
    }

    $currentMonth = (int) $now->format('Y') * 12 + (int) $now->format('n');
    if ($expMonth < 1 || $expMonth > 12 || $expYear * 12 + $expMonth < $currentMonth) {
      $errors['expiry'] = 'Enter an expiry date that is not in the past.';
    }

    if (!preg_match('/^\d{3,4}$/', $cvc)) {
      $errors['cvc'] = 'Enter the 3 or 4 digit security code.';
    }

    return $errors;
  }

  /**
   * [code, message] when the processor declines the (valid) card, otherwise NULL.
   */
  public static function decline(string $number): ?array {
    return self::DECLINES[$number] ?? NULL;
  }

  public static function normalize(string $number): string {
    return preg_replace('/[\s-]+/', '', $number) ?? '';
  }

  private static function luhn(string $number): bool {
    $sum = 0;
    $double = FALSE;
    for ($i = strlen($number) - 1; $i >= 0; $i--) {
      $digit = (int) $number[$i];
      if ($double && ($digit *= 2) > 9) {
        $digit -= 9;
      }
      $sum += $digit;
      $double = !$double;
    }
    return $sum % 10 === 0;
  }

}
