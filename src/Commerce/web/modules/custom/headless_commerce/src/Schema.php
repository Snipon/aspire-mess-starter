<?php

declare(strict_types=1);

namespace Drupal\headless_commerce;

use Drupal\commerce_order\Entity\OrderType;
use Drupal\commerce_payment\Entity\PaymentGateway;
use Drupal\commerce_price\CurrencyImporterInterface;
use Drupal\commerce_price\Entity\Currency;
use Drupal\commerce_store\Entity\Store;
use Drupal\Core\Config\ConfigFactoryInterface;
use Drupal\Core\Entity\EntityDisplayRepositoryInterface;
use Drupal\Core\Entity\EntityTypeManagerInterface;
use Drupal\Core\Extension\ModuleHandlerInterface;
use Drupal\Core\Extension\ModuleInstallerInterface;
use Drupal\Core\Extension\ThemeHandlerInterface;
use Drupal\Core\Extension\ThemeInstallerInterface;
use Drupal\field\Entity\FieldConfig;
use Drupal\field\Entity\FieldStorageConfig;
use Drupal\openid_connect\Entity\OpenIDConnectClientEntity;
use Drupal\taxonomy\Entity\Vocabulary;
use Drupal\user\Entity\Role;
use Symfony\Component\DependencyInjection\Attribute\Autowire;

/**
 * The commerce schema in code instead of exported config: a USD store, the product and variation fields, the category
 * and brand vocabularies, the fake card payment gateway, the staff sign-in through the identity provider and the Gin
 * admin theme. ensure() only creates what is missing, so it runs from hook_install() and can run again from update hooks
 * when fields are added.
 */
final class Schema {

  public const GATEWAY = 'fake_card';

  public const OIDC_CLIENT = 'sso';

  public function __construct(
    private readonly EntityTypeManagerInterface $entityTypeManager,
    private readonly EntityDisplayRepositoryInterface $displayRepository,
    private readonly ConfigFactoryInterface $configFactory,
    #[Autowire(service: 'commerce_price.currency_importer')]
    private readonly CurrencyImporterInterface $currencyImporter,
    private readonly ModuleHandlerInterface $moduleHandler,
    private readonly ModuleInstallerInterface $moduleInstaller,
    private readonly ThemeHandlerInterface $themeHandler,
    private readonly ThemeInstallerInterface $themeInstaller,
  ) {}

  public function ensure(): void {
    if (!Currency::load('USD')) {
      $this->currencyImporter->import('USD');
    }

    $stores = $this->entityTypeManager->getStorage('commerce_store');
    if (!$stores->loadDefault()) {
      $store = Store::create([
        'type' => 'online',
        'uid' => 1,
        'name' => 'Dev Gear Store',
        'mail' => 'store@example.com',
        'default_currency' => 'USD',
        'timezone' => 'UTC',
        'address' => ['country_code' => 'US', 'administrative_area' => 'CA', 'locality' => 'San Francisco', 'postal_code' => '94103', 'address_line1' => '1 Demo Street'],
        'billing_countries' => [],
      ]);
      $store->setDefault(TRUE);
      $store->save();
    }

    // No receipt emails: the container has no mail transport and the demo shows the receipt itself.
    $orderType = OrderType::load('default');
    if ($orderType?->shouldSendReceipt()) {
      $orderType->setSendReceipt(FALSE)->save();
    }

    foreach (['product_category' => 'Product category', 'product_brand' => 'Brand'] as $id => $label) {
      if (!Vocabulary::load($id)) {
        Vocabulary::create(['vid' => $id, 'name' => $label])->save();
      }
    }

    $this->field('commerce_product', 'default', 'field_description', 'string_long', 'Description (Markdown)');
    $this->field('commerce_product', 'default', 'field_image', 'image', 'Image', [], ['file_directory' => 'products', 'file_extensions' => 'png jpg jpeg webp', 'alt_field_required' => FALSE]);
    $this->field('commerce_product', 'default', 'field_category', 'entity_reference', 'Category', ['target_type' => 'taxonomy_term'], $this->terms('product_category'));
    $this->field('commerce_product', 'default', 'field_brand', 'entity_reference', 'Brand', ['target_type' => 'taxonomy_term'], $this->terms('product_brand'));
    $this->field('commerce_product', 'default', 'field_specs', 'string_long', 'Specifications (JSON: [{"label", "value", "unit"}])');
    $this->field('commerce_product', 'default', 'field_rating', 'decimal', 'Average rating (0-5)', ['precision' => 3, 'scale' => 2]);
    $this->field('commerce_product', 'default', 'field_review_count', 'integer', 'Review count');
    $this->field('commerce_product', 'default', 'field_units_sold', 'integer', 'Units sold');
    $this->field('commerce_product', 'default', 'field_return_rate', 'decimal', 'Return rate (%)', ['precision' => 5, 'scale' => 2]);
    $this->field('commerce_product', 'default', 'field_weight_grams', 'integer', 'Weight (g)');
    $this->field('commerce_product', 'default', 'field_warranty_months', 'integer', 'Warranty (months)');
    $this->field('commerce_product', 'default', 'field_release_date', 'datetime', 'Release date', ['datetime_type' => 'date']);
    $this->field('commerce_product_variation', 'default', 'field_stock', 'integer', 'Stock');

    if (!PaymentGateway::load(self::GATEWAY)) {
      // Manual gateway: payments are recorded as completed by CartController::checkout() after the fake card check.
      PaymentGateway::create([
        'id' => self::GATEWAY,
        'label' => 'Fake card (demo, no money moves)',
        'plugin' => 'manual',
        'configuration' => [
          'display_label' => 'Card (test)',
          'mode' => 'test',
          'instructions' => ['value' => 'Demo payment: card details are checked by fixed test rules and never stored.', 'format' => 'plain_text'],
        ],
      ])->save();
    }

    $this->staffSignIn();
  }

  /**
   * The OpenID Connect client for staff and its settings. Environment-specific values (client ID and secret, endpoints)
   * and whether it is enabled come from settings.php; an IdP user with the "admin" role becomes a Drupal administrator.
   */
  private function staffSignIn(): void {
    // The minimal install profile has no administrator role.
    if (!Role::load('administrator')) {
      Role::create(['id' => 'administrator', 'label' => 'Administrator', 'is_admin' => TRUE])->save();
    }

    if (!OpenIDConnectClientEntity::load(self::OIDC_CLIENT)) {
      OpenIDConnectClientEntity::create([
        'id' => self::OIDC_CLIENT, // the redirect URI is /openid-connect/sso
        'label' => 'Staff sign-in',
        'status' => FALSE,
        'plugin' => 'generic',
        'settings' => [
          'client_id' => '',
          'client_secret' => '',
          'provider_slug' => '',
          'iss_allowed_domains' => '', // no IdP-initiated sign-in
          'prompt' => [],
          'issuer_url' => '',
          'authorization_endpoint' => '',
          'token_endpoint' => '',
          'userinfo_endpoint' => '', // claims (including roles) come from the ID token
          'end_session_endpoint' => '',
          'scopes' => ['openid', 'email', 'profile'],
        ],
      ])->save();
    }

    // Our settings once, over the module's defaults (role_mappings is empty until then).
    $settings = $this->configFactory->getEditable('openid_connect.settings');
    if (!$settings->get('role_mappings')) {
      $settings
        // Never attach an IdP identity to an existing account by email (account takeover).
        ->set('connect_existing_users', FALSE)
        // Staff accounts are created on first sign-in although visitors cannot register.
        ->set('override_registration_settings', TRUE)
        ->set('redirect_logout', 'user/login')
        // The IdP's "roles" claim, merged into "groups" by headless_commerce_openid_connect_userinfo_alter(), is the
        // source of truth: roles are added and removed on every sign-in.
        ->set('role_mappings', ['administrator' => ['admin']])
        ->set('force_reset_role_mappings', TRUE)
        ->save();
    }

    // Admin theme (the minimal install profile has none, so admin pages fall back to Stark). Gin is set once; a theme
    // chosen later in Appearance stays. Gin Toolbar (its sidebar navigation) requires the theme, so it is installed after it.
    if (!$this->themeHandler->themeExists('gin')) {
      $this->themeInstaller->install(['gin']);
    }
    if (!$this->moduleHandler->moduleExists('gin_toolbar')) {
      $this->moduleInstaller->install(['gin_toolbar']);
    }
    $theme = $this->configFactory->getEditable('system.theme');
    if (!$theme->get('admin')) {
      $theme->set('admin', 'gin')->save();
    }
  }

  private function terms(string $vocabulary): array {
    return ['handler' => 'default:taxonomy_term', 'handler_settings' => ['target_bundles' => [$vocabulary => $vocabulary]]];
  }

  /**
   * Creates the field (storage, instance and a form widget for editors) if it does not exist yet.
   */
  private function field(string $entityType, string $bundle, string $name, string $type, string $label, array $storageSettings = [], array $settings = []): void {
    if (!FieldStorageConfig::loadByName($entityType, $name)) {
      FieldStorageConfig::create(['field_name' => $name, 'entity_type' => $entityType, 'type' => $type, 'settings' => $storageSettings])->save();
    }

    if (!FieldConfig::loadByName($entityType, $bundle, $name)) {
      FieldConfig::create(['field_name' => $name, 'entity_type' => $entityType, 'bundle' => $bundle, 'label' => $label, 'settings' => $settings])->save();
      $this->displayRepository->getFormDisplay($entityType, $bundle)->setComponent($name)->save();
    }
  }

}
