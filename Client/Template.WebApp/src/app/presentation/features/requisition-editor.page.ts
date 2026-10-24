import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { MasterDataItem, MasterDataKind } from '@application/master-data/master-data';
import {
  RequisitionUseCases,
  type BudgetAvailability,
  type Requisition,
  type RequisitionLine,
} from '@application/procurement/procurement';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ActionConfirmation, type ActionConfirmationRequest } from '../shared/action-confirmation';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { MASTER_DATA_PORT, REQUISITION_PORT } from '../state/ports';

const CURRENCIES = ['BRL', 'USD', 'EUR'] as const;
const MEDIA_TYPES = [
  'application/pdf',
  'image/png',
  'image/jpeg',
  'text/plain',
  'text/csv',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
] as const;

type PendingAction = 'submit' | 'discard' | undefined;

/**
 * Draft editor (REQ-001..004): header, lines with a client preview of totals (CALC-002, advisory), attachment metadata
 * (FIL-002), own-draft budget availability (BUD-012) and a submission confirmation. Every write sends the version it
 * read; a 412 shows the conflict with an explicit reload. Server totals and allowed actions are authoritative.
 */
@Component({
  selector: 'app-requisition-editor-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    ActionConfirmation,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  templateUrl: './requisition-editor.page.html',
  styleUrl: './data-pages.css',
})
export class RequisitionEditorPage {
  private readonly port = inject(REQUISITION_PORT);
  private readonly masterData = inject(MASTER_DATA_PORT);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  readonly currencies = CURRENCIES;
  readonly mediaTypes = MEDIA_TYPES;
  readonly requisition = signal<Requisition | undefined>(undefined);
  readonly availability = signal<BudgetAvailability | undefined>(undefined);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly costCenters = signal<readonly MasterDataItem[]>([]);
  readonly categories = signal<readonly MasterDataItem[]>([]);
  readonly units = signal<readonly MasterDataItem[]>([]);
  readonly catalogItems = signal<readonly MasterDataItem[]>([]);
  readonly editingLine = signal<number | undefined>(undefined);
  readonly pending = signal<PendingAction>(undefined);

  readonly headerModel = signal({ costCenterId: '', currency: 'BRL', purpose: '', neededBy: '' });
  readonly headerForm = form(this.headerModel, (path) => {
    required(path.costCenterId);
    required(path.currency);
  });
  readonly lineModel = signal({
    catalogItemId: '',
    description: '',
    kind: 'Good' as 'Good' | 'Service',
    categoryId: '',
    unitOfMeasureId: '',
    quantity: '1',
    unitPrice: '',
  });
  readonly lineForm = form(this.lineModel, (path) => {
    required(path.description);
    required(path.categoryId);
    required(path.unitOfMeasureId);
    required(path.quantity);
    required(path.unitPrice);
  });
  readonly attachmentModel = signal({ fileName: '', mediaType: 'application/pdf', length: '' });
  readonly attachmentForm = form(this.attachmentModel, (path) => {
    required(path.fileName);
    required(path.length);
  });

  readonly editable = computed(() => {
    const current = this.requisition();
    return current === undefined || RequisitionUseCases.can(current, 'edit');
  });
  readonly linePreview = computed(() => {
    const line = this.lineModel();
    return RequisitionUseCases.preview([{ quantity: line.quantity, unitPrice: line.unitPrice }])
      .lineTotals[0];
  });
  readonly previewTotal = computed(() => {
    const current = this.requisition();
    if (!current) return '—';
    const line = this.lineModel();
    const saved = current.lines
      .filter((existing) => existing.lineNumber !== this.editingLine())
      .map((existing) => ({
        quantity: existing.quantity,
        unitPrice: existing.estimatedUnitPrice.amount,
      }));
    const draftLine = line.unitPrice
      ? [{ quantity: line.quantity, unitPrice: line.unitPrice }]
      : [];
    return `${RequisitionUseCases.preview([...saved, ...draftLine]).total} ${current.currency}`;
  });
  readonly fits = computed(() => {
    const current = this.requisition();
    return current ? RequisitionUseCases.fitsBudget(current, this.availability()) : undefined;
  });
  readonly confirmation = computed<ActionConfirmationRequest | undefined>(() => {
    const action = this.pending();
    const current = this.requisition();
    if (!action || !current) return undefined;
    if (action === 'discard')
      return {
        title: this.transloco.translate('requisition.confirmDiscard'),
        actionLabel: this.transloco.translate('requisition.discard'),
        requireReason: false,
      };
    const available = this.availability()?.available;
    return {
      title: this.transloco.translate('requisition.confirmSubmit'),
      actionLabel: this.transloco.translate('requisition.submit'),
      description: this.transloco.translate('requisition.submitSummary', {
        total: `${current.estimatedTotal.amount} ${current.currency}`,
        lines: current.lines.length,
        available: available ? `${available.amount} ${available.currency}` : '—',
      }),
      requireReason: false,
    };
  });

  constructor() {
    void this.loadLookups();
    if (this.id) void this.reload();
  }

  can(action: Parameters<typeof RequisitionUseCases.can>[1]) {
    return RequisitionUseCases.can(this.requisition(), action);
  }

  async reload() {
    if (!this.id && !this.requisition()) return;
    const result = await this.port.get(this.id ?? this.requisition()!.id);
    if (!result.ok) {
      this.error.set(result.error);
      return;
    }
    this.error.set(undefined);
    this.apply(result.value);
  }

  async saveHeader(event: Event) {
    event.preventDefault();
    if (this.headerForm().invalid()) return;
    const current = this.requisition();
    const result = current
      ? await this.port.update(current, this.headerModel())
      : await this.port.create(this.headerModel());
    if (!result.ok) {
      this.error.set(result.error);
      return;
    }
    this.error.set(undefined);
    this.apply(result.value);
    if (!current) await this.router.navigate(['/requisitions', result.value.id, 'edit']);
  }

  applyCatalogItem() {
    const item = this.catalogItems().find(
      (candidate) => candidate.id === this.lineModel().catalogItemId,
    );
    if (!item) return;
    this.lineModel.update((line) => ({
      ...line,
      description: item.name,
      kind: item.detail === 'Service' ? 'Service' : 'Good',
      categoryId: item.relatedId ?? '',
      unitOfMeasureId: item.secondaryRelatedId ?? '',
    }));
  }

  editLine(line: RequisitionLine) {
    this.editingLine.set(line.lineNumber);
    this.lineModel.set({
      catalogItemId: line.catalogItemId ?? '',
      description: line.description,
      kind: line.kind,
      categoryId: line.categoryId,
      unitOfMeasureId: line.unitOfMeasureId,
      quantity: line.quantity,
      unitPrice: line.estimatedUnitPrice.amount,
    });
  }

  resetLine() {
    this.editingLine.set(undefined);
    this.lineModel.set({
      catalogItemId: '',
      description: '',
      kind: 'Good',
      categoryId: '',
      unitOfMeasureId: '',
      quantity: '1',
      unitPrice: '',
    });
  }

  async saveLine(event: Event) {
    event.preventDefault();
    const current = this.requisition();
    if (!current || this.lineForm().invalid()) return;
    const input = { ...this.lineModel(), currency: current.currency };
    const lineNumber = this.editingLine();
    const result = lineNumber
      ? await this.port.updateLine(current, lineNumber, input)
      : await this.port.addLine(current, input);
    if (this.handle(result)) this.resetLine();
  }

  async removeLine(line: RequisitionLine) {
    const current = this.requisition();
    if (current) this.handle(await this.port.removeLine(current, line.lineNumber));
  }

  async addAttachment(event: Event) {
    event.preventDefault();
    const current = this.requisition();
    if (!current || this.attachmentForm().invalid()) return;
    const model = this.attachmentModel();
    const result = await this.port.addAttachment(current, {
      fileName: model.fileName,
      mediaType: model.mediaType,
      length: Number(model.length),
    });
    if (this.handle(result))
      this.attachmentModel.set({ fileName: '', mediaType: 'application/pdf', length: '' });
  }

  async removeAttachment(attachmentId: string) {
    const current = this.requisition();
    if (current) this.handle(await this.port.removeAttachment(current, attachmentId));
  }

  async confirm() {
    const action = this.pending();
    const current = this.requisition();
    this.pending.set(undefined);
    if (!current || !action) return;
    if (action === 'discard') {
      const result = await this.port.discard(current);
      if (result.ok) await this.router.navigateByUrl('/requisitions/mine');
      else this.error.set(result.error);
      return;
    }
    const result = await this.port.submit(current);
    if (result.ok) await this.router.navigate(['/requisitions', result.value.id]);
    else this.error.set(result.error);
  }

  private handle(result: Awaited<ReturnType<typeof this.port.get>>) {
    if (!result.ok) {
      this.error.set(result.error);
      return false;
    }
    this.error.set(undefined);
    this.apply(result.value);
    return true;
  }

  private apply(requisition: Requisition) {
    this.requisition.set(requisition);
    this.headerModel.set({
      costCenterId: requisition.costCenter.id,
      currency: requisition.currency,
      purpose: requisition.purpose ?? '',
      neededBy: requisition.neededBy ?? '',
    });
    if (RequisitionUseCases.can(requisition, 'submit')) void this.loadAvailability(requisition.id);
  }

  private async loadAvailability(id: string) {
    const result = await this.port.availability(id);
    this.availability.set(result.ok ? result.value : undefined);
  }

  private async loadLookups() {
    const load = async (kind: MasterDataKind) => {
      const result = await this.masterData.list(kind, { page: 1, pageSize: 100, sort: 'code' });
      return result.ok ? result.value.items.filter((item) => item.active) : [];
    };
    const [costCenters, categories, units, items] = await Promise.all([
      load('cost-centers'),
      load('categories'),
      load('units'),
      load('catalog-items'),
    ]);
    this.costCenters.set(costCenters);
    this.categories.set(categories);
    this.units.set(units);
    this.catalogItems.set(items);
  }
}
