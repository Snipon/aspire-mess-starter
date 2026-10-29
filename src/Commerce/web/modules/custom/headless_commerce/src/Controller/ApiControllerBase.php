<?php

declare(strict_types=1);

namespace Drupal\headless_commerce\Controller;

use Drupal\Core\Controller\ControllerBase;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpFoundation\Request;

/**
 * Shared plumbing of the JSON endpoints: the X-Api-Key check (COMMERCE_API_KEY, compared in constant time), JSON
 * bodies, and responses that neither Drupal's page cache nor any proxy may keep (carts and stock change per request).
 */
abstract class ApiControllerBase extends ControllerBase {

  protected function unauthorized(Request $request): ?JsonResponse {
    $expected = (string) getenv('COMMERCE_API_KEY');
    $actual = (string) $request->headers->get('X-Api-Key', '');
    return $expected !== '' && hash_equals($expected, $actual)
      ? NULL
      : $this->error(401, 'unauthorized', 'Missing or invalid X-Api-Key header.');
  }

  protected function json(?array $data, int $status = 200): JsonResponse {
    \Drupal::service('page_cache_kill_switch')->trigger();
    $response = new JsonResponse($data, $status);
    $response->headers->set('Cache-Control', 'no-store');
    return $response;
  }

  protected function error(int $status, string $code, string $message, array $extra = []): JsonResponse {
    return $this->json(['error' => $code, 'message' => $message] + $extra, $status);
  }

  protected function body(Request $request): array {
    $data = json_decode($request->getContent(), TRUE);
    return is_array($data) ? $data : [];
  }

}
