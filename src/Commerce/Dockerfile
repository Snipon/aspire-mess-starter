# Drupal 11 + Commerce 3 as a headless commerce backend. Only the JSON endpoints of the headless_commerce module are
# used by the API (see README "Commerce"); the Drupal admin UI at /user/login stays available for editors.
FROM docker.io/library/drupal:11-php8.4-apache

# Commerce's price calculations need bcmath.
RUN docker-php-ext-install -j"$(nproc)" bcmath

WORKDIR /opt/drupal
COPY composer.json composer.lock ./
RUN COMPOSER_ALLOW_SUPERUSER=1 composer install --no-interaction --no-progress --no-dev --optimize-autoloader

COPY settings.php web/sites/default/settings.php
COPY web/modules/custom web/modules/custom
# Fails the build on PHP syntax errors in the custom module.
RUN find web/modules/custom -type f \( -name '*.php' -o -name '*.install' \) -print0 | xargs -0 -n1 php -l > /dev/null
COPY entrypoint.sh /usr/local/bin/commerce-entrypoint
RUN chmod +x /usr/local/bin/commerce-entrypoint \
    && mkdir -p web/sites/default/files \
    && chown -R www-data:www-data web/sites/default/files

ENV PATH=/opt/drupal/vendor/bin:$PATH
ENTRYPOINT ["commerce-entrypoint"]
CMD ["apache2-foreground"]
