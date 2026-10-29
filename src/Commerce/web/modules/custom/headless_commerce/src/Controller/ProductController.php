<?php

declare(strict_types=1);

namespace Drupal\headless_commerce\Controller;

use Drupal\headless_commerce\ProductExporter;
use Symfony\Component\DependencyInjection\ContainerInterface;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpFoundation\Request;

/**
 * Catalog endpoints: GET /headless/products?offset=&limit= (published products in id order, for indexing) and
 * GET /headless/products/{sku}.
 */
final class ProductController extends ApiControllerBase {

  private const MAX_LIMIT = 200;

  public function __construct(private readonly ProductExporter $exporter) {}

  public static function create(ContainerInterface $container): static {
    return new static($container->get(ProductExporter::class));
  }

  /**
   * Liveness for Aspire and the API's status page; no key needed, reveals nothing but the product count.
   */
  public function health(): JsonResponse {
    $count = $this->entityTypeManager()->getStorage('commerce_product')->getQuery()->accessCheck(FALSE)->count()->execute();
    return $this->json(['status' => 'ok', 'products' => (int) $count]);
  }

  public function list(Request $request): JsonResponse {
    if ($denied = $this->unauthorized($request)) {
      return $denied;
    }

    $offset = max(0, (int) $request->query->get('offset', 0));
    $limit = min(self::MAX_LIMIT, max(1, (int) $request->query->get('limit', 100)));
    $storage = $this->entityTypeManager()->getStorage('commerce_product');
    $query = fn () => $storage->getQuery()->accessCheck(FALSE)->condition('status', 1);
    $total = (int) $query()->count()->execute();
    $ids = $query()->sort('product_id')->range($offset, $limit)->execute();

    $items = array_values(array_filter(array_map($this->exporter->export(...), $storage->loadMultiple($ids))));
    $storage->resetCache($ids);
    return $this->json(['total' => $total, 'offset' => $offset, 'items' => $items]);
  }

  public function get(Request $request, string $sku): JsonResponse {
    if ($denied = $this->unauthorized($request)) {
      return $denied;
    }

    $variation = $this->entityTypeManager()->getStorage('commerce_product_variation')->loadBySku($sku);
    $product = $variation?->getProduct();
    if (!$product || !$product->isPublished()) {
      return $this->error(404, 'not_found', "No product with SKU '$sku'.");
    }

    return $this->json($this->exporter->export($product));
  }

}
