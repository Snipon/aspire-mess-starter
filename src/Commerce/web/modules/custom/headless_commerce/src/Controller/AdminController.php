<?php

declare(strict_types=1);

namespace Drupal\headless_commerce\Controller;

use Drupal\Core\Controller\ControllerBase;
use Symfony\Component\HttpFoundation\RedirectResponse;
use Symfony\Component\HttpKernel\Exception\NotFoundHttpException;

/**
 * Admin shortcuts for the website's staff bar, which knows products by SKU only (not by Drupal id).
 */
final class AdminController extends ControllerBase {

  /**
   * Redirects to the edit form of the product with this SKU.
   */
  public function editProductBySku(string $sku): RedirectResponse {
    $product = $this->entityTypeManager()->getStorage('commerce_product_variation')->loadBySku($sku)?->getProduct();
    if (!$product) {
      throw new NotFoundHttpException();
    }
    return $this->redirect('entity.commerce_product.edit_form', ['commerce_product' => $product->id()]);
  }

}
