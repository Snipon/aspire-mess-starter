// Product picker property editor: stores one product SKU (a plain string), searched in the shop through the CMS's
// /product-picker/products endpoint (Headless/ProductPickerController.cs), which passes through the API's product search.
import { LitElement, css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

const SEARCH_DELAY_MS = 250;

const formatPrice = (price) => new Intl.NumberFormat(undefined, { style: 'currency', currency: price.currency }).format(price.amount);

export default class ProductPickerElement extends UmbElementMixin(LitElement) {
  static properties = {
    value: { type: String },
    readonly: { type: Boolean, reflect: true },
    _selected: { state: true },
    _query: { state: true },
    _results: { state: true },
    _error: { state: true },
  };

  constructor() {
    super();
    this.value = '';
    this.readonly = false;
    this._selected = undefined; // the product for `value`; null when it is no longer in the shop
    this._query = '';
    this._results = [];
    this._error = '';
    this._timer = undefined;
  }

  updated(changed) {
    if (changed.has('value') && this.value && this._selected?.sku !== this.value) this.#loadSelected();
  }

  async #search(q) {
    const auth = await this.getContext(UMB_AUTH_CONTEXT);
    const config = auth.getOpenApiConfiguration();
    const response = await fetch(`${config.base}/umbraco/management/api/v1/product-picker/products?q=${encodeURIComponent(q)}`, {
      credentials: config.credentials,
      headers: { Authorization: `Bearer ${await config.token()}` },
    });
    if (!response.ok) throw new Error(`Product search returned ${response.status}`);
    return (await response.json()).items;
  }

  async #loadSelected() {
    const sku = this.value;
    try {
      const items = await this.#search(sku);
      if (this.value === sku) this._selected = items.find((p) => p.sku === sku) ?? null;
    } catch {
      if (this.value === sku) this._selected = null;
    }
  }

  #onInput(e) {
    this._query = e.target.value;
    clearTimeout(this._timer);
    const q = this._query.trim();
    if (!q) {
      this._results = [];
      return;
    }
    this._timer = setTimeout(async () => {
      try {
        const items = await this.#search(q);
        if (this._query.trim() === q) [this._results, this._error] = [items, ''];
      } catch {
        [this._results, this._error] = [[], 'The shop is not reachable. Try again later.'];
      }
    }, SEARCH_DELAY_MS);
  }

  #select(product) {
    this._selected = product;
    this.value = product?.sku ?? '';
    [this._query, this._results] = ['', []];
    this.dispatchEvent(new UmbChangeEvent());
  }

  #renderProduct(product, selected) {
    return html`<uui-ref-node
      name=${product.title}
      detail=${`${product.sku} · ${product.brand} · ${formatPrice(product.price)}${product.inStock ? '' : ' · out of stock'}`}
      ?readonly=${selected}
      @open=${selected ? nothing : () => this.#select(product)}>
      <uui-icon slot="icon" name="icon-shopping-basket"></uui-icon>
      ${selected && !this.readonly
        ? html`<uui-action-bar slot="actions"><uui-button label="Remove" @click=${() => this.#select(null)}></uui-button></uui-action-bar>`
        : nothing}
    </uui-ref-node>`;
  }

  render() {
    if (this.value) {
      if (this._selected === undefined) return html`<uui-loader></uui-loader>`;
      if (this._selected) return this.#renderProduct(this._selected, true);
      return html`<uui-ref-node name=${this.value} detail="Not found in the shop" readonly>
        ${this.readonly
          ? nothing
          : html`<uui-action-bar slot="actions"><uui-button label="Remove" @click=${() => this.#select(null)}></uui-button></uui-action-bar>`}
      </uui-ref-node>`;
    }
    if (this.readonly) return nothing;
    return html`
      <uui-input label="Search products" placeholder="Search by name, brand or SKU" .value=${this._query} @input=${this.#onInput}>
        <uui-icon slot="prepend" name="icon-search"></uui-icon>
      </uui-input>
      ${this._error ? html`<p class="error">${this._error}</p>` : nothing}
      ${this._results.length ? html`<uui-ref-list>${this._results.map((p) => this.#renderProduct(p, false))}</uui-ref-list>` : nothing}
    `;
  }

  static styles = css`
    uui-input {
      width: 100%;
    }
    uui-ref-list {
      margin-top: var(--uui-size-space-2);
    }
    .error {
      color: var(--uui-color-danger);
    }
  `;
}

customElements.define('headless-product-picker', ProductPickerElement);
