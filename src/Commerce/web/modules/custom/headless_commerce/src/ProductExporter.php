<?php

declare(strict_types=1);

namespace Drupal\headless_commerce;

use Drupal\commerce_price\Price;
use Drupal\commerce_product\Entity\ProductInterface;
use Drupal\Component\Utility\Html;
use Drupal\Core\File\FileUrlGeneratorInterface;

/**
 * The JSON shape of a product for the API: one variation per product (its SKU identifies the product), specs as
 * label/value/unit rows, and image URLs relative to the Drupal site (the API makes them absolute).
 */
final class ProductExporter {

  public function __construct(private readonly FileUrlGeneratorInterface $fileUrlGenerator) {}

  public function export(ProductInterface $product): ?array {
    $variation = $product->getDefaultVariation();
    if (!$variation) {
      return NULL;
    }

    $sku = $variation->getSku();
    $image = $product->get('field_image')->first();
    $file = $image?->entity;
    $release = $product->get('field_release_date')->value;

    return [
      'id' => $product->uuid(),
      'sku' => $sku,
      'slug' => self::slug($product->label(), $sku),
      'title' => $product->label(),
      'description' => $product->get('field_description')->value ?? '',
      'category' => $product->get('field_category')->entity?->label(),
      'brand' => $product->get('field_brand')->entity?->label(),
      'image' => $file ? [
        'url' => $this->fileUrlGenerator->generateString($file->getFileUri()),
        'width' => (int) $image->width ?: NULL,
        'height' => (int) $image->height ?: NULL,
        'alt' => $image->alt ?: $product->label(),
      ] : NULL,
      'price' => self::money($variation->getPrice()),
      'stock' => (int) $variation->get('field_stock')->value,
      'specs' => json_decode($product->get('field_specs')->value ?? '[]', TRUE) ?: [],
      'stats' => [
        'rating' => (float) $product->get('field_rating')->value,
        'reviewCount' => (int) $product->get('field_review_count')->value,
        'unitsSold' => (int) $product->get('field_units_sold')->value,
        'returnRate' => (float) $product->get('field_return_rate')->value,
      ],
      'weightGrams' => (int) $product->get('field_weight_grams')->value ?: NULL,
      'warrantyMonths' => (int) $product->get('field_warranty_months')->value ?: NULL,
      'releaseDate' => $release ?: NULL,
      'updatedAt' => date(DATE_ATOM, (int) $product->getChangedTime()),
    ];
  }

  /**
   * "Arcfield Aero 14" + "DG00042" -> "arcfield-aero-14-dg00042": readable, and unique because it ends in the SKU.
   */
  public static function slug(string $title, string $sku): string {
    return Html::getClass($title) . '-' . strtolower($sku);
  }

  public static function money(?Price $price): ?array {
    return $price ? ['amount' => $price->getNumber(), 'currency' => $price->getCurrencyCode()] : NULL;
  }

}
