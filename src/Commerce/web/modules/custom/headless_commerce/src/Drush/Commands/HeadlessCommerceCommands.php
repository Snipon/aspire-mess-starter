<?php

declare(strict_types=1);

namespace Drupal\headless_commerce\Drush\Commands;

use Drupal\headless_commerce\DemoCatalog;
use Drush\Attributes as CLI;
use Drush\Commands\AutowireTrait;
use Drush\Commands\DrushCommands;

final class HeadlessCommerceCommands extends DrushCommands {

  use AutowireTrait;

  public function __construct(private readonly DemoCatalog $catalog) {
    parent::__construct();
  }

  /**
   * Creates the demo products that are missing (run by the container entrypoint when DEMO_CONTENT=true).
   */
  #[CLI\Command(name: 'headless-commerce:seed')]
  #[CLI\Usage(name: 'drush headless-commerce:seed', description: 'Create the missing demo products.')]
  public function seed(): void {
    $this->catalog->seed(fn (string $message) => $this->logger()->notice($message));
  }

}
