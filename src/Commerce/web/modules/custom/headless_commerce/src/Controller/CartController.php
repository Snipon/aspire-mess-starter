<?php

declare(strict_types=1);

namespace Drupal\headless_commerce\Controller;

use Drupal\commerce_cart\CartManagerInterface;
use Drupal\commerce_order\Entity\OrderInterface;
use Drupal\commerce_order\Entity\OrderItemInterface;
use Drupal\commerce_payment\Entity\Payment;
use Drupal\commerce_product\Entity\ProductVariationInterface;
use Drupal\Core\Database\Connection;
use Drupal\headless_commerce\Customers;
use Drupal\headless_commerce\FakeCard;
use Drupal\headless_commerce\ProductExporter;
use Drupal\headless_commerce\Schema;
use Drupal\profile\Entity\Profile;
use Symfony\Component\DependencyInjection\ContainerInterface;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpFoundation\Request;

/**
 * Carts are Commerce draft orders, addressed by their UUID and guarded by a random token (X-Cart-Token) that is only
 * returned on creation and stored hashed in the order data. Checkout runs the fake card check, records a completed
 * payment on the fake_card (manual) gateway, takes the items out of stock and places the order, all in one
 * transaction. A checkout with a customer (the identity provider's subject, see Customers) makes the order theirs;
 * GET /headless/orders?customer= lists a customer's placed orders.
 */
final class CartController extends ApiControllerBase {

  private const MAX_QUANTITY = 99;
  private const MAX_ORDERS = 50;
  private const TOKEN_KEY = 'headless_token_sha256';

  public function __construct(
    private readonly CartManagerInterface $cartManager,
    private readonly ProductExporter $exporter,
    private readonly Connection $database,
    private readonly Customers $customers,
  ) {}

  public static function create(ContainerInterface $container): static {
    return new static(
      $container->get('commerce_cart.cart_manager'),
      $container->get(ProductExporter::class),
      $container->get('database'),
      $container->get(Customers::class),
    );
  }

  public function createCart(Request $request): JsonResponse {
    if ($denied = $this->unauthorized($request)) {
      return $denied;
    }

    $token = bin2hex(random_bytes(24));
    $store = $this->entityTypeManager()->getStorage('commerce_store')->loadDefault();
    /** @var \Drupal\commerce_order\Entity\OrderInterface $order */
    $order = $this->entityTypeManager()->getStorage('commerce_order')->create([
      'type' => 'default',
      'store_id' => $store->id(),
      'uid' => 0,
      'cart' => TRUE,
      'state' => 'draft',
    ]);
    $order->setData(self::TOKEN_KEY, hash('sha256', $token));
    $order->save();
    return $this->json(['token' => $token] + $this->cartJson($order), 201);
  }

  public function get(Request $request, string $cart): JsonResponse {
    $order = $this->load($request, $cart);
    return $order instanceof JsonResponse ? $order : $this->json($this->cartJson($order));
  }

  public function addItem(Request $request, string $cart): JsonResponse {
    $order = $this->load($request, $cart);
    if ($order instanceof JsonResponse) {
      return $order;
    }

    $body = $this->body($request);
    $quantity = (int) ($body['quantity'] ?? 1);
    if ($quantity < 1 || $quantity > self::MAX_QUANTITY) {
      return $this->error(400, 'invalid_quantity', 'Quantity must be between 1 and ' . self::MAX_QUANTITY . '.');
    }

    $variation = $this->variation((string) ($body['sku'] ?? ''));
    if (!$variation) {
      return $this->error(404, 'product_not_found', 'No published product with this SKU.');
    }

    $inCart = 0;
    foreach ($order->getItems() as $item) {
      if ($item->getPurchasedEntityId() == $variation->id()) {
        $inCart += (int) $item->getQuantity();
      }
    }

    if ($denied = $this->outOfStock($variation, $inCart + $quantity)) {
      return $denied;
    }

    $this->cartManager->addEntity($order, $variation, (string) $quantity);
    return $this->json($this->cartJson($order));
  }

  public function updateItem(Request $request, string $cart, string $item): JsonResponse {
    $order = $this->load($request, $cart);
    if ($order instanceof JsonResponse) {
      return $order;
    }

    $orderItem = $this->item($order, $item);
    if (!$orderItem) {
      return $this->error(404, 'item_not_found', 'No such item in this cart.');
    }

    $quantity = (int) ($this->body($request)['quantity'] ?? -1);
    if ($quantity < 0 || $quantity > self::MAX_QUANTITY) {
      return $this->error(400, 'invalid_quantity', 'Quantity must be between 0 and ' . self::MAX_QUANTITY . '.');
    }

    if ($quantity === 0) {
      $this->cartManager->removeOrderItem($order, $orderItem);
      return $this->json($this->cartJson($order));
    }

    if ($denied = $this->outOfStock($orderItem->getPurchasedEntity(), $quantity)) {
      return $denied;
    }

    $orderItem->setQuantity((string) $quantity);
    $this->cartManager->updateOrderItem($order, $orderItem);
    return $this->json($this->cartJson($order));
  }

  public function removeItem(Request $request, string $cart, string $item): JsonResponse {
    $order = $this->load($request, $cart);
    if ($order instanceof JsonResponse) {
      return $order;
    }

    if ($orderItem = $this->item($order, $item)) {
      $this->cartManager->removeOrderItem($order, $orderItem);
    }

    return $this->json($this->cartJson($order));
  }

  /**
   * Body: email, name {given, family}, address {line1, line2?, locality, postalCode, administrativeArea?, countryCode},
   * card {number, expMonth, expYear, cvc}, customer {subject, email?} or null for a guest. 200 with the receipt; 400
   * with field errors; 402 when the card is declined; 409 when the cart is empty or an item is no longer in stock.
   */
  public function checkout(Request $request, string $cart): JsonResponse {
    $order = $this->load($request, $cart);
    if ($order instanceof JsonResponse) {
      return $order;
    }

    $body = $this->body($request);
    $email = trim((string) ($body['email'] ?? ''));
    $name = (array) ($body['name'] ?? []);
    $address = (array) ($body['address'] ?? []);
    $card = (array) ($body['card'] ?? []);
    $number = FakeCard::normalize((string) ($card['number'] ?? ''));
    $customer = isset($body['customer']) ? (array) $body['customer'] : NULL;
    $subject = (string) ($customer['subject'] ?? '');
    if ($customer !== NULL && !Customers::isValidSubject($subject)) {
      return $this->error(400, 'invalid_customer', 'The customer subject is missing or invalid.');
    }

    $errors = [];
    if (!filter_var($email, FILTER_VALIDATE_EMAIL)) {
      $errors['email'] = 'Enter a valid email address.';
    }
    foreach (['given' => $name, 'family' => $name, 'line1' => $address, 'locality' => $address, 'postalCode' => $address] as $field => $source) {
      if (trim((string) ($source[$field] ?? '')) === '') {
        $errors[$field] = 'Required.';
      }
    }
    if (!preg_match('/^[A-Z]{2}$/', (string) ($address['countryCode'] ?? ''))) {
      $errors['countryCode'] = 'Enter a two-letter country code.';
    }
    $errors += FakeCard::validate($number, (int) ($card['expMonth'] ?? 0), (int) ($card['expYear'] ?? 0), (string) ($card['cvc'] ?? ''));
    if ($errors) {
      return $this->error(400, 'invalid_checkout', 'Some fields are missing or invalid.', ['fields' => $errors]);
    }

    if (!$order->hasItems()) {
      return $this->error(409, 'empty_cart', 'The cart is empty.');
    }
    foreach ($order->getItems() as $item) {
      if ($denied = $this->outOfStock($item->getPurchasedEntity(), (int) $item->getQuantity())) {
        return $denied;
      }
    }

    if ($decline = FakeCard::decline($number)) {
      [$code, $message] = $decline;
      return $this->error(402, $code, $message);
    }

    $transaction = $this->database->startTransaction();
    try {
      $profile = Profile::create([
        'type' => 'customer',
        'uid' => 0,
        'address' => [
          'country_code' => $address['countryCode'],
          'given_name' => trim((string) $name['given']),
          'family_name' => trim((string) $name['family']),
          'address_line1' => trim((string) $address['line1']),
          'address_line2' => trim((string) ($address['line2'] ?? '')),
          'locality' => trim((string) $address['locality']),
          'postal_code' => trim((string) $address['postalCode']),
          'administrative_area' => trim((string) ($address['administrativeArea'] ?? '')),
        ],
      ]);
      $profile->save();
      $order->setEmail($email);
      $order->setBillingProfile($profile);
      if ($customer !== NULL) {
        $order->setCustomer($this->customers->findOrCreate($subject, (string) ($customer['email'] ?? $email)));
      }
      $order->save();

      $reference = 'FAKE-' . strtoupper(bin2hex(random_bytes(6))) . '-' . substr($number, -4);
      Payment::create([
        'type' => 'payment_manual',
        'payment_gateway' => Schema::GATEWAY,
        'order_id' => $order->id(),
        'amount' => $order->getTotalPrice(),
        'state' => 'completed',
        'remote_id' => $reference,
        'completed' => \Drupal::time()->getRequestTime(),
      ])->save();

      foreach ($order->getItems() as $item) {
        $variation = $item->getPurchasedEntity();
        $quantity = (int) $item->getQuantity();
        $variation->set('field_stock', (int) $variation->get('field_stock')->value - $quantity)->save();
        $product = $variation->getProduct();
        $product->set('field_units_sold', (int) $product->get('field_units_sold')->value + $quantity)->save();
      }

      $order->set('cart', FALSE);
      $order->getState()->applyTransitionById('place');
      $order->save();
    }
    catch (\Throwable $e) {
      $transaction->rollBack();
      throw $e;
    }

    return $this->json([
      'orderNumber' => $order->getOrderNumber(),
      'placedAt' => date(DATE_ATOM, (int) $order->getPlacedTime()),
      'email' => $email,
      'paymentReference' => $reference,
      'cardLast4' => substr($number, -4),
    ] + $this->cartJson($order));
  }

  /**
   * GET /headless/orders?customer={subject}&offset=&limit=: the customer's placed orders, newest first; none for a
   * customer who has never checked out.
   */
  public function orders(Request $request): JsonResponse {
    if ($denied = $this->unauthorized($request)) {
      return $denied;
    }

    $subject = (string) $request->query->get('customer', '');
    if (!Customers::isValidSubject($subject)) {
      return $this->error(400, 'invalid_customer', 'The customer subject is missing or invalid.');
    }

    $offset = max(0, (int) $request->query->get('offset', 0));
    $limit = min(self::MAX_ORDERS, max(1, (int) $request->query->get('limit', 10)));
    $user = $this->customers->find($subject);
    if (!$user) {
      return $this->json(['total' => 0, 'offset' => $offset, 'items' => []]);
    }

    $storage = $this->entityTypeManager()->getStorage('commerce_order');
    $query = fn () => $storage->getQuery()->accessCheck(FALSE)
      ->condition('uid', $user->id())
      ->condition('state', 'draft', '<>');
    $total = (int) $query()->count()->execute();
    $ids = $query()->sort('placed', 'DESC')->sort('order_id', 'DESC')->range($offset, $limit)->execute();

    $items = [];
    foreach ($storage->loadMultiple($ids) as $order) {
      $items[] = [
        'orderNumber' => $order->getOrderNumber(),
        'placedAt' => date(DATE_ATOM, (int) $order->getPlacedTime()),
        'state' => $order->getState()->getId(),
        'email' => (string) $order->getEmail(),
      ] + $this->cartJson($order);
    }
    return $this->json(['total' => $total, 'offset' => $offset, 'items' => $items]);
  }

  /**
   * The open cart addressed by $uuid if the token matches; otherwise the error response (404 for both unknown carts
   * and wrong tokens, so carts cannot be probed).
   */
  private function load(Request $request, string $uuid): OrderInterface|JsonResponse {
    if ($denied = $this->unauthorized($request)) {
      return $denied;
    }

    $orders = $this->entityTypeManager()->getStorage('commerce_order')->loadByProperties(['uuid' => $uuid]);
    $order = reset($orders);
    $token = (string) $request->headers->get('X-Cart-Token', '');
    $expected = $order ? (string) $order->getData(self::TOKEN_KEY) : '';
    if (!$order || $expected === '' || !hash_equals($expected, hash('sha256', $token))) {
      return $this->error(404, 'cart_not_found', 'No such cart.');
    }

    if (!$order->get('cart')->value || $order->getState()->getId() !== 'draft') {
      return $this->error(409, 'cart_closed', 'This cart has already been checked out.');
    }

    return $order;
  }

  private function variation(string $sku): ?ProductVariationInterface {
    $variation = $sku === '' ? NULL : $this->entityTypeManager()->getStorage('commerce_product_variation')->loadBySku($sku);
    return $variation && $variation->isPublished() && $variation->getProduct()?->isPublished() ? $variation : NULL;
  }

  private function item(OrderInterface $order, string $id): ?OrderItemInterface {
    foreach ($order->getItems() as $item) {
      if ((string) $item->id() === $id) {
        return $item;
      }
    }
    return NULL;
  }

  private function outOfStock(?ProductVariationInterface $variation, int $wanted): ?JsonResponse {
    $available = $variation ? (int) $variation->get('field_stock')->value : 0;
    return $wanted > $available
      ? $this->error(409, 'out_of_stock', $available === 0 ? 'This product is out of stock.' : "Only $available left in stock.", [
        'sku' => $variation?->getSku(),
        'available' => $available,
      ])
      : NULL;
  }

  private function cartJson(OrderInterface $order): array {
    $items = [];
    $count = 0;
    foreach ($order->getItems() as $item) {
      $variation = $item->getPurchasedEntity();
      $product = $variation?->getProduct();
      $export = $product ? $this->exporter->export($product) : NULL;
      $quantity = (int) $item->getQuantity();
      $count += $quantity;
      $items[] = [
        'id' => (string) $item->id(),
        'sku' => $variation?->getSku(),
        'slug' => $export['slug'] ?? NULL,
        'title' => $product?->label() ?? $item->getTitle(),
        'image' => $export['image'] ?? NULL,
        'unitPrice' => ProductExporter::money($item->getUnitPrice()),
        'quantity' => $quantity,
        'total' => ProductExporter::money($item->getTotalPrice()),
        'stock' => $export['stock'] ?? 0,
      ];
    }

    return [
      'id' => $order->uuid(),
      'items' => $items,
      'itemCount' => $count,
      'subtotal' => ProductExporter::money($order->getSubtotalPrice()),
      'total' => ProductExporter::money($order->getTotalPrice()),
    ];
  }

}
