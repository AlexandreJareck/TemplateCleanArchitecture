import type { BooleanInput, NumberInput } from '@angular/cdk/coercion';
import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  model,
  numberAttribute,
  untracked,
} from '@angular/core';
import { HlmSelectImports } from '@presentation/shared-ui/select';
import { classes } from '@presentation/shared-ui/utils';
import { createPageArray, outOfBoundCorrection } from './hlm-numbered-pagination';
import { HlmPagination } from './hlm-pagination';
import { HlmPaginationContent } from './hlm-pagination-content';
import { HlmPaginationEllipsis } from './hlm-pagination-ellipsis';
import { HlmPaginationItem } from './hlm-pagination-item';
import { HlmPaginationLink } from './hlm-pagination-link';
import { HlmPaginationNext } from './hlm-pagination-next';
import { HlmPaginationPrevious } from './hlm-pagination-previous';
import { injectHlmPaginationConfig } from './hlm-pagination.token';

@Component({
  selector: 'hlm-numbered-pagination-query-params',
  exportAs: 'hlmNumberedPaginationQueryParams',
  imports: [
    HlmPagination,
    HlmPaginationContent,
    HlmPaginationItem,
    HlmPaginationPrevious,
    HlmPaginationNext,
    HlmPaginationLink,
    HlmPaginationEllipsis,
    HlmSelectImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './hlm-numbered-pagination-query-params.html',
})
export class HlmNumberedPaginationQueryParams {
  private readonly _config = injectHlmPaginationConfig();

  /**
   * The current (active) page.
   */
  public readonly currentPage = model.required<number>();

  /**
   * The number of items per paginated page.
   */
  public readonly itemsPerPage = model.required<number>();

  /**
   * The total number of items in the collection. Only useful when
   * doing server-side paging, where the collection size is limited
   * to a single page returned by the server API.
   */
  public readonly totalItems = input.required<number, NumberInput>({
    transform: numberAttribute,
  });

  /**
   * The URL path to use for the pagination links.
   * Defaults to '.' (current path).
   */
  public readonly link = input<string>('.');

  /**
   * The number of page links to show.
   */
  public readonly maxSize = input<number, NumberInput>(7, {
    transform: numberAttribute,
  });

  /**
   * Show the first and last page buttons.
   */
  public readonly showEdges = input<boolean, BooleanInput>(true, {
    transform: booleanAttribute,
  });

  /**
   * The page sizes to show.
   * Defaults to [10, 20, 50, 100]
   */
  public readonly pageSizes = input<number[]>([10, 20, 50, 100]);

  /**
   * The label for total items or pages to show
   * Defaults to config's totalItemsLabel
   */
  public readonly totalItemsLabel = input<string>(this._config.totalItemsLabel);

  /**
   * The label for pages to show
   * Defaults to config's pagesLabel
   */
  public readonly pagesLabel = input<string>(this._config.pagesLabel);

  /**
   * The label to show on the next button
   * Defaults to config's nextLabel
   */
  public readonly nextLabel = input<string>(this._config.nextLabel);

  /**
   * The aria-label to show on the next button
   * Defaults to config's nextAriaLabel
   */
  public readonly nextAriaLabel = input<string>(this._config.nextAriaLabel);

  /**
   * The label to show on the previous button
   * Defaults to config's previousLabel
   */
  public readonly previousLabel = input<string>(this._config.previousLabel);

  /**
   * The aria-label to show on the previous button
   * Defaults to config's previousAriaLabel
   */
  public readonly previousAriaLabel = input<string>(this._config.previousAriaLabel);

  protected readonly _pageSizesWithCurrent = computed(() => {
    const pageSizes = this.pageSizes();
    return pageSizes.includes(this.itemsPerPage())
      ? pageSizes // if current page size is included, return the same array
      : [...pageSizes, this.itemsPerPage()].sort((a, b) => a - b); // otherwise, add current page size and sort the array
  });

  protected readonly _isFirstPageActive = computed(() => this.currentPage() === 1);
  protected readonly _isLastPageActive = computed(
    () => this.currentPage() === this._lastPageNumber(),
  );

  protected readonly _lastPageNumber = computed(() => {
    if (this.totalItems() < 1) {
      // when there are 0 or fewer (an error case) items, there are no "pages" as such,
      // but it makes sense to consider a single, empty page as the last page.
      return 1;
    }
    return Math.ceil(this.totalItems() / this.itemsPerPage());
  });

  protected readonly _pages = computed(() => {
    const correctedCurrentPage = outOfBoundCorrection(
      this.totalItems(),
      this.itemsPerPage(),
      this.currentPage(),
    );

    if (correctedCurrentPage !== this.currentPage()) {
      // update the current page
      untracked(() => this.currentPage.set(correctedCurrentPage));
    }

    return createPageArray(
      correctedCurrentPage,
      this.itemsPerPage(),
      this.totalItems(),
      this.maxSize(),
    );
  });

  constructor() {
    classes(() => 'flex items-center justify-between gap-2 px-4 py-2');
  }
}
