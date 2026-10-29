<?php

declare(strict_types=1);

namespace Drupal\headless_commerce;

use Drupal\commerce_price\Price;
use Drupal\commerce_product\Entity\Product;
use Drupal\commerce_product\Entity\ProductVariation;
use Drupal\Component\Utility\Html;
use Drupal\Core\Database\Connection;
use Drupal\Core\Entity\EntityTypeManagerInterface;
use Drupal\Core\File\FileExists;
use Drupal\Core\File\FileSystemInterface;
use Drupal\file\FileRepositoryInterface;
use Random\Engine\Mt19937;
use Random\Randomizer;

/**
 * COUNT deterministic demo products (fixed seed): developer gear in ten categories, each with category-specific specs
 * and performance stats (rating, reviews, units sold, return rate, stock). The same SKUs and values on every install;
 * seed() only creates the SKUs that are missing, so it is safe on every container start. Product images are 30
 * generated placeholders (three per category), so the repo holds no binary assets.
 */
final class DemoCatalog {

  public const COUNT = 2000;

  private const SEED = 20261029;
  private const REFERENCE_DATE = '2026-10-01';

  private const BRANDS = [
    'Arcfield', 'Bitforge', 'Corewave', 'Driftline', 'Ember Labs', 'Fluxpoint',
    'Gridstone', 'Halcyon', 'Ionix', 'Juniper', 'Keystone', 'Lumen',
  ];

  /**
   * Category => [singular noun, series names, min price, max price, min weight (g), max weight (g), palette].
   */
  private const CATEGORIES = [
    'Laptops' => ['laptop', ['Aero', 'Pro', 'Studio', 'Air', 'Edge', 'Flex', 'Nomad', 'Titan'], 699, 3499, 1100, 2600, [[30, 60, 170], [120, 200, 255]]],
    'Monitors' => ['monitor', ['View', 'Vista', 'Canvas', 'Prism', 'Horizon', 'Pixel'], 159, 1899, 3200, 9800, [[90, 20, 120], [250, 120, 160]]],
    'Keyboards' => ['keyboard', ['Type', 'Tactile', 'Keys', 'Board', 'Glide', 'Strike'], 39, 349, 450, 1300, [[10, 90, 80], [140, 230, 170]]],
    'Mice' => ['mouse', ['Glide', 'Swift', 'Point', 'Orbit', 'Dart', 'Pulse'], 19, 179, 49, 140, [[160, 60, 20], [255, 200, 90]]],
    'Headphones' => ['pair of headphones', ['Sonic', 'Quiet', 'Studio', 'Pulse', 'Echo', 'Wave'], 49, 549, 180, 390, [[20, 30, 60], [90, 130, 200]]],
    'Docks & Hubs' => ['dock', ['Port', 'Hub', 'Dock', 'Bridge', 'Link', 'Station'], 29, 399, 80, 700, [[120, 20, 40], [250, 150, 90]]],
    'Desk Chairs' => ['desk chair', ['Ergo', 'Posture', 'Task', 'Lounge', 'Support', 'Balance'], 149, 1599, 14000, 28000, [[30, 100, 140], [200, 240, 220]]],
    'Standing Desks' => ['standing desk', ['Rise', 'Lift', 'Elevate', 'Summit', 'Stand', 'Motion'], 299, 1499, 25000, 55000, [[60, 40, 140], [150, 210, 250]]],
    'Webcams' => ['webcam', ['Cam', 'Vision', 'Lens', 'Frame', 'Focus', 'Stream'], 39, 299, 70, 180, [[40, 80, 30], [220, 230, 110]]],
    'Microphones' => ['microphone', ['Voice', 'Cast', 'Podium', 'Vocal', 'Signal', 'Tone'], 49, 399, 250, 900, [[50, 50, 55], [190, 190, 200]]],
  ];

  private const IMAGES_PER_CATEGORY = 3;

  public function __construct(
    private readonly EntityTypeManagerInterface $entityTypeManager,
    private readonly Connection $database,
    private readonly FileSystemInterface $fileSystem,
    private readonly FileRepositoryInterface $fileRepository,
  ) {}

  /**
   * Creates the missing demo products; returns how many were created. $log receives progress messages.
   */
  public function seed(callable $log): int {
    $existing = array_flip($this->database->select('commerce_product_variation_field_data', 'v')->fields('v', ['sku'])->execute()->fetchCol());
    $missing = array_values(array_filter($this->generate(), fn (array $p) => !isset($existing[$p['sku']])));
    if (!$missing) {
      $log('Demo catalog complete (' . self::COUNT . ' products)');
      return 0;
    }

    $log(sprintf('Creating %d demo products', count($missing)));
    $store = $this->entityTypeManager->getStorage('commerce_store')->loadDefault();
    $images = $this->images();
    $categories = $this->terms('product_category', array_keys(self::CATEGORIES));
    $brands = $this->terms('product_brand', self::BRANDS);

    foreach ($missing as $n => $p) {
      $variation = ProductVariation::create([
        'type' => 'default',
        'sku' => $p['sku'],
        'price' => new Price($p['price'], 'USD'),
        'field_stock' => $p['stock'],
        'status' => 1,
      ]);
      $variation->save();

      $image = $images[$p['category']][$p['image']];
      Product::create([
        'type' => 'default',
        'title' => $p['title'],
        'stores' => [$store->id()],
        'variations' => [$variation],
        'status' => 1,
        'field_description' => $p['description'],
        'field_image' => ['target_id' => $image, 'alt' => $p['title'], 'width' => 800, 'height' => 600],
        'field_category' => $categories[$p['category']],
        'field_brand' => $brands[$p['brand']],
        'field_specs' => json_encode($p['specs'], JSON_UNESCAPED_UNICODE),
        'field_rating' => $p['rating'],
        'field_review_count' => $p['reviewCount'],
        'field_units_sold' => $p['unitsSold'],
        'field_return_rate' => $p['returnRate'],
        'field_weight_grams' => $p['weightGrams'],
        'field_warranty_months' => $p['warrantyMonths'],
        'field_release_date' => $p['releaseDate'],
      ])->save();

      if (($n + 1) % 100 === 0) {
        // Keeps memory flat over thousands of entities.
        $this->entityTypeManager->getStorage('commerce_product')->resetCache();
        $this->entityTypeManager->getStorage('commerce_product_variation')->resetCache();
        $log(sprintf('  %d/%d', $n + 1, count($missing)));
      }
    }

    $log(sprintf('Created %d demo products', count($missing)));
    return count($missing);
  }

  /**
   * The full demo catalog as plain arrays, in SKU order (DG00001..). Pure function of SEED.
   */
  public function generate(): array {
    $r = new Randomizer(new Mt19937(self::SEED));
    $reference = new \DateTimeImmutable(self::REFERENCE_DATE);
    $names = array_keys(self::CATEGORIES);
    $products = [];
    for ($i = 0; $i < self::COUNT; $i++) {
      $category = $names[$i % count($names)];
      [$noun, $series, $minPrice, $maxPrice, $minWeight, $maxWeight] = self::CATEGORIES[$category];
      $brand = self::pick($r, self::BRANDS);
      [$suffix, $specs] = $this->specs($r, $category);
      $title = sprintf('%s %s %s', $brand, self::pick($r, $series), $suffix);

      // Ratings cluster around 4.2; popular products have more reviews, sales follow reviews, poor ratings return more.
      $rating = round(min(5.0, max(2.5, 2.9 + 2.1 * ($r->getFloat(0, 1) + $r->getFloat(0, 1)) / 2 + $r->getFloat(-0.3, 0.3))), 1);
      $reviews = (int) floor(exp($r->getFloat(0, log(6000)))) - 1;
      $sold = $reviews * $r->getInt(8, 25) + $r->getInt(0, 500);
      $returns = round($r->getFloat(0.4, 4.5) + (5 - $rating) * $r->getFloat(0.5, 2.0), 1);
      $price = $r->getInt($minPrice, $maxPrice);
      $released = $reference->modify('-' . $r->getInt(0, 5 * 365) . ' days');

      $products[] = [
        'sku' => sprintf('DG%05d', $i + 1),
        'title' => $title,
        'category' => $category,
        'brand' => $brand,
        'price' => sprintf('%d.%s', $price, $price < 100 ? '99' : self::pick($r, ['00', '49', '99'])),
        'stock' => $r->getInt(1, 10) === 1 ? 0 : $r->getInt(1, 400),
        'specs' => $specs,
        'rating' => $rating,
        'reviewCount' => $reviews,
        'unitsSold' => $sold,
        'returnRate' => $returns,
        'weightGrams' => $r->getInt($minWeight, $maxWeight),
        'warrantyMonths' => in_array($category, ['Desk Chairs', 'Standing Desks'], TRUE) ? self::pick($r, [36, 60, 120]) : self::pick($r, [12, 24, 36]),
        'releaseDate' => $released->format('Y-m-d'),
        'image' => $r->getInt(0, self::IMAGES_PER_CATEGORY - 1),
        'description' => $this->description($r, $title, $noun, $specs),
      ];
    }

    return $products;
  }

  /**
   * [title suffix, spec rows] for one product of the category.
   */
  private function specs(Randomizer $r, string $category): array {
    $s = static fn (string $label, string|int|float $value, ?string $unit = NULL) => ['label' => $label, 'value' => (string) $value, 'unit' => $unit];
    $p = static fn (array $values) => self::pick($r, $values);
    $yes = static fn () => $r->getInt(0, 1) ? 'Yes' : 'No';

    switch ($category) {
      case 'Laptops':
        $size = $p([13, 14, 15, 16]);
        return [(string) $size, [
          $s('Processor', $p(['Ryzen 7 8840U', 'Ryzen 9 8945HS', 'Core Ultra 7 155H', 'Core Ultra 9 185H', 'Snapdragon X Elite', 'Snapdragon X Plus'])),
          $s('Memory', $p([8, 16, 32, 64]), 'GB'),
          $s('Storage', $p([256, 512, 1024, 2048, 4096]), 'GB'),
          $s('Display size', $size, 'in'),
          $s('Resolution', $p(['1920 × 1200', '2560 × 1600', '2880 × 1800', '3200 × 2000', '3840 × 2400'])),
          $s('Refresh rate', $p([60, 90, 120, 144, 165]), 'Hz'),
          $s('Battery', $r->getInt(50, 99), 'Wh'),
          $s('Ports', $p(['2× USB-C', '2× USB-C, 1× USB-A, HDMI', '3× Thunderbolt 4, SD', '2× Thunderbolt 4, 2× USB-A, HDMI, RJ45'])),
          $s('Operating system', $p(['Windows 11 Pro', 'Windows 11 Home', 'Ubuntu 24.04 LTS', 'No OS'])),
          $s('Backlit keyboard', $yes()),
        ]];

      case 'Monitors':
        $size = $p([24, 27, 32, 34, 38, 42]);
        return [$size . $p(['Q', 'U', 'W', 'X']), [
          $s('Screen size', $size, 'in'),
          $s('Resolution', $p(['1920 × 1080', '2560 × 1440', '3440 × 1440', '3840 × 2160', '5120 × 2160'])),
          $s('Panel', $p(['IPS', 'OLED', 'VA', 'Mini-LED'])),
          $s('Refresh rate', $p([60, 75, 120, 144, 165, 240]), 'Hz'),
          $s('Response time', $p(['0.03', '1', '2', '4', '5']), 'ms'),
          $s('Brightness', $p([250, 300, 350, 400, 600, 1000]), 'nits'),
          $s('HDR', $p(['None', 'HDR400', 'HDR600', 'HDR1000', 'True Black 400'])),
          $s('USB-C power delivery', $p([0, 65, 90, 96]), 'W'),
          $s('Inputs', $p(['2× HDMI, DisplayPort', 'HDMI, DisplayPort, USB-C', '2× HDMI, DisplayPort, USB-C, KVM'])),
          $s('VESA mount', $p(['100 × 100', '75 × 75', 'None'])),
        ]];

      case 'Keyboards':
        return [$p(['K65', 'K75', 'TKL', 'Split', 'LP', 'Ergo']), [
          $s('Layout', $p(['Full size', 'Tenkeyless', '75%', '65%', 'Split'])),
          $s('Switches', $p(['Linear red', 'Tactile brown', 'Clicky blue', 'Low-profile tactile', 'Optical linear'])),
          $s('Hot-swappable', $yes()),
          $s('Connectivity', $p(['USB-C', 'Bluetooth + 2.4 GHz', 'Bluetooth + USB-C', 'Tri-mode'])),
          $s('Battery', $p([0, 2000, 4000]), 'mAh'),
          $s('Backlight', $p(['None', 'White', 'RGB'])),
          $s('Keycaps', $p(['ABS', 'PBT double-shot', 'PBT dye-sub'])),
          $s('Polling rate', $p([125, 1000, 4000, 8000]), 'Hz'),
        ]];

      case 'Mice':
        return [$p(['M1', 'M3', 'M5', 'Pro', 'Lite', 'Ultra']), [
          $s('Sensor', $r->getInt(8, 30) * 1000, 'DPI'),
          $s('Polling rate', $p([125, 1000, 4000, 8000]), 'Hz'),
          $s('Buttons', $r->getInt(3, 12)),
          $s('Connectivity', $p(['USB', 'Bluetooth', 'Bluetooth + 2.4 GHz'])),
          $s('Battery life', $p([0, 70, 120, 250]), 'h'),
          $s('Grip', $p(['Palm', 'Claw', 'Fingertip', 'Vertical'])),
          $s('Ambidextrous', $yes()),
        ]];

      case 'Headphones':
        return [$p(['100', '300', '500', '700', 'Pro', 'Max']), [
          $s('Type', $p(['Over-ear', 'On-ear', 'In-ear'])),
          $s('Noise cancelling', $p(['None', 'Hybrid ANC', 'Adaptive ANC'])),
          $s('Battery life', $r->getInt(20, 60), 'h'),
          $s('Codecs', $p(['SBC, AAC', 'SBC, AAC, LDAC', 'SBC, AAC, aptX Adaptive'])),
          $s('Driver size', $p([10, 40, 50]), 'mm'),
          $s('Microphones', $r->getInt(2, 8)),
          $s('Multipoint', $yes()),
          $s('Water resistance', $p(['None', 'IPX4', 'IP54'])),
        ]];

      case 'Docks & Hubs':
        return [$p(['7-in-1', '9-in-1', '11-in-1', '13-in-1', 'TB4', 'TB5']), [
          $s('Host connection', $p(['USB-C', 'Thunderbolt 4', 'Thunderbolt 5'])),
          $s('Power delivery', $p([60, 85, 96, 100, 140]), 'W'),
          $s('External displays', $r->getInt(1, 3)),
          $s('Max resolution', $p(['4K60', '4K144', '8K30'])),
          $s('USB-A ports', $r->getInt(2, 4)),
          $s('USB-C ports', $r->getInt(1, 4)),
          $s('Ethernet', $p(['None', '1 GbE', '2.5 GbE'])),
          $s('Card reader', $p(['None', 'SD', 'SD + microSD'])),
        ]];

      case 'Desk Chairs':
        return [$p(['S', 'M', 'L', 'XL', 'Plus', 'Elite']), [
          $s('Max load', $r->getInt(110, 160), 'kg'),
          $s('Seat height', $p(['42–52', '45–55', '47–57']), 'cm'),
          $s('Armrests', $p(['2D', '3D', '4D'])),
          $s('Lumbar support', $p(['Fixed', 'Adjustable', 'Dynamic'])),
          $s('Material', $p(['Mesh', 'Fabric', 'Leather', 'Mesh + fabric'])),
          $s('Recline', $r->getInt(105, 135), '°'),
          $s('Headrest', $yes()),
        ]];

      case 'Standing Desks':
        $width = $p([120, 140, 160, 180]);
        return [(string) $width, [
          $s('Top width', $width, 'cm'),
          $s('Top depth', $p([60, 70, 80]), 'cm'),
          $s('Height range', $p(['62–127', '65–130', '60–125']), 'cm'),
          $s('Motors', $p([1, 2])),
          $s('Max load', $r->getInt(70, 160), 'kg'),
          $s('Lift speed', $r->getInt(25, 45), 'mm/s'),
          $s('Memory presets', $r->getInt(2, 4)),
          $s('Noise level', $r->getInt(38, 50), 'dB'),
        ]];

      case 'Webcams':
        return [$p(['C1', 'C2', 'HD', '4K', 'Pro', 'Stream']), [
          $s('Video', $p(['1080p30', '1080p60', '4K30'])),
          $s('Field of view', $r->getInt(65, 95), '°'),
          $s('Focus', $p(['Fixed focus', 'Autofocus', 'Autofocus + AI framing'])),
          $s('Microphones', $p([1, 2, 4])),
          $s('Privacy shutter', $yes()),
          $s('HDR', $yes()),
          $s('Mount', $p(['Clip', 'Clip + tripod thread'])),
        ]];

      default:
        return [$p(['One', 'Duo', 'Pro', 'X', 'Mini', 'Studio']), [
          $s('Capsule', $p(['Dynamic', 'Condenser'])),
          $s('Polar pattern', $p(['Cardioid', 'Supercardioid', 'Multi-pattern'])),
          $s('Connection', $p(['USB-C', 'XLR', 'USB-C + XLR'])),
          $s('Sample rate', $p(['48 kHz / 24-bit', '96 kHz / 24-bit', '192 kHz / 32-bit float'])),
          $s('Frequency response', $p(['50 Hz–15 kHz', '20 Hz–20 kHz'])),
          $s('Headphone output', $yes()),
          $s('Gain control', $p(['Analog', 'Digital', 'Automatic'])),
        ]];
    }
  }

  private function description(Randomizer $r, string $title, string $noun, array $specs): string {
    $openers = [
      'The %s is a %s built for long days at the desk.',
      'Meet the %s: a %s that stays out of your way so you can focus on the work.',
      'The %s is the %s we reach for when a setup has to just work.',
      'Designed with developers in mind, the %s is a %s with no compromises where it matters.',
    ];
    $closers = [
      'It ships with everything you need to get started, and firmware updates keep it current.',
      'Backed by a straightforward warranty and support that answers.',
      'Pair it with the rest of your setup for a desk that feels like one system.',
    ];
    $highlights = array_slice($specs, 0, 4);
    $lines = array_map(fn (array $s) => sprintf('- **%s:** %s%s', $s['label'], $s['value'], $s['unit'] ? ' ' . $s['unit'] : ''), $highlights);
    return sprintf(self::pick($r, $openers), $title, $noun) . ' ' . self::pick($r, $closers)
      . "\n\n## Highlights\n\n" . implode("\n", $lines) . "\n";
  }

  /**
   * Term ids by name, creating the terms that are missing.
   */
  private function terms(string $vocabulary, array $names): array {
    $storage = $this->entityTypeManager->getStorage('taxonomy_term');
    $ids = [];
    foreach ($names as $weight => $name) {
      $existing = $storage->loadByProperties(['vid' => $vocabulary, 'name' => $name]);
      $term = reset($existing) ?: $storage->create(['vid' => $vocabulary, 'name' => $name, 'weight' => $weight]);
      if ($term->isNew()) {
        $term->save();
      }
      $ids[$name] = (int) $term->id();
    }
    return $ids;
  }

  /**
   * File ids of the placeholder images: category => [fid, ...], generated once (diagonal gradient with rings).
   */
  private function images(): array {
    $directory = 'public://products';
    $this->fileSystem->prepareDirectory($directory, FileSystemInterface::CREATE_DIRECTORY | FileSystemInterface::MODIFY_PERMISSIONS);
    $files = $this->entityTypeManager->getStorage('file');
    $ids = [];
    foreach (self::CATEGORIES as $category => [, , , , , , [$from, $to]]) {
      for ($v = 0; $v < self::IMAGES_PER_CATEGORY; $v++) {
        $uri = sprintf('%s/%s-%d.jpg', $directory, Html::getClass($category), $v + 1);
        $existing = $files->loadByProperties(['uri' => $uri]);
        $file = reset($existing) ?: $this->fileRepository->writeData($this->placeholder($from, $to, $v), $uri, FileExists::Replace);
        $ids[$category][] = (int) $file->id();
      }
    }
    return $ids;
  }

  private function placeholder(array $from, array $to, int $variant): string {
    [$width, $height] = [800, 600];
    $image = imagecreatetruecolor($width, $height);
    $cx = $width * (0.25 + 0.25 * $variant);
    $cy = $height * (0.35 + 0.15 * $variant);
    for ($y = 0; $y < $height; $y += 2) {
      for ($x = 0; $x < $width; $x += 2) {
        $ring = 0.5 + 0.5 * cos(sqrt(($x - $cx) ** 2 + ($y - $cy) ** 2) / (22 + 6 * $variant));
        $t = max(0.0, min(1.0, ($x / $width + $y / $height) / 2 + 0.12 * $ring - 0.06));
        $color = imagecolorallocate($image, ...array_map(fn ($a, $b) => (int) ($a + ($b - $a) * $t), $from, $to));
        imagefilledrectangle($image, $x, $y, $x + 1, $y + 1, $color);
      }
    }
    ob_start();
    imagejpeg($image, NULL, 82);
    imagedestroy($image);
    return (string) ob_get_clean();
  }

  private static function pick(Randomizer $r, array $values): mixed {
    return $values[$r->getInt(0, count($values) - 1)];
  }

}
