import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  type OnInit,
  output,
  signal,
  untracked,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmInput } from '@presentation/shared-ui/input';
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';

/** One selectable business entity: a stable ID submitted to the API and a business label shown to people. */
export interface EntityOption {
  readonly id: string;
  readonly label: string;
}

export type EntitySearch = (query: string) => Promise<readonly EntityOption[]>;
export type EntityResolve = (id: string) => Promise<EntityOption | undefined>;

/**
 * Searchable entity selector (F-07): a search box narrows the options through the API (debounced), the native select
 * shows business identifiers and names and emits the stable ID. A value set from outside (for example a query
 * parameter) is resolved to its label so a raw ID is never the visible text.
 */
@Component({
  selector: 'app-entity-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...HlmNativeSelectImports, HlmInput, TranslocoPipe],
  template: `
    <div class="entity-picker">
      <label [for]="pickerId()" class="picker-label">
        {{ label() }}
        @if (!required()) {
          <span class="optional">({{ 'common.optional' | transloco }})</span>
        }
      </label>
      <input
        hlmInput
        type="search"
        autocomplete="off"
        [id]="pickerId() + '-search'"
        [value]="query()"
        [placeholder]="searchPlaceholder()"
        [attr.aria-label]="'common.searchIn' | transloco: { field: label() }"
        [attr.aria-controls]="pickerId()"
        (input)="onSearch($any($event.target).value)"
      />
      <hlm-native-select
        class="w-full"
        [selectId]="pickerId()"
        [value]="value() ?? ''"
        [aria-invalid]="error() ? true : undefined"
        (valueChange)="pick($any($event))"
      >
        <option hlmNativeSelectOption value="">
          {{ (required() ? 'common.choose' : 'common.any') | transloco }}
        </option>
        @for (option of options(); track option.id) {
          <option hlmNativeSelectOption [value]="option.id">{{ option.label }}</option>
        }
      </hlm-native-select>
      @if (loading()) {
        <small class="picker-status" role="status">{{ 'common.loading' | transloco }}</small>
      } @else if (options().length === 0) {
        <small class="picker-status">{{ 'common.empty' | transloco }}</small>
      }
      @if (error(); as message) {
        <small class="picker-error" role="alert" [id]="pickerId() + '-error'">{{ message }}</small>
      }
    </div>
  `,
  styles: [
    `
      .entity-picker {
        display: grid;
        gap: 0.25rem;
        min-width: 0;
      }
      .optional,
      .picker-status {
        color: var(--muted-foreground);
        font-weight: 400;
        font-size: 0.8125rem;
      }
      .picker-error {
        color: var(--destructive);
        font-size: 0.8125rem;
        font-weight: 600;
      }
    `,
  ],
})
export class EntityPicker implements OnInit {
  readonly pickerId = input.required<string>();
  readonly label = input.required<string>();
  readonly searchPlaceholder = input('');
  readonly required = input(false);
  readonly value = input<string | undefined>(undefined);
  readonly error = input<string | undefined>(undefined);
  readonly search = input.required<EntitySearch>();
  readonly resolve = input<EntityResolve | undefined>(undefined);
  readonly valueChange = output<string>();

  readonly query = signal('');
  readonly options = signal<readonly EntityOption[]>([]);
  readonly loading = signal(false);
  private timer?: ReturnType<typeof setTimeout>;
  private sequence = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    // Keep an externally supplied selection visible with its business label.
    effect(() => {
      const id = this.value();
      const resolve = this.resolve();
      if (!id || !resolve) return;
      untracked(() => {
        if (this.options().some((option) => option.id === id)) return;
        void resolve(id).then((option) => {
          if (option && !this.options().some((item) => item.id === option.id))
            this.options.update((current) => [option, ...current]);
        });
      });
    });
  }

  ngOnInit() {
    void this.run('');
  }

  onSearch(text: string) {
    this.query.set(text);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.run(text.trim()), 250);
  }

  pick(id: string) {
    this.valueChange.emit(id);
  }

  private async run(text: string) {
    const ticket = ++this.sequence;
    this.loading.set(true);
    const found = await this.search()(text).catch(() => [] as readonly EntityOption[]);
    if (ticket !== this.sequence) return;
    this.loading.set(false);
    const selected = this.options().find((option) => option.id === this.value());
    this.options.set(
      selected && !found.some((option) => option.id === selected.id) ? [selected, ...found] : found,
    );
  }
}
