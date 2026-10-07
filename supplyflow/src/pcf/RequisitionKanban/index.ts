import * as React from "react";
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { KanbanBoard } from "./KanbanBoard";
import { CardData, StageOption, buildLanes, moveCard, parseHiddenStages, serverMessage } from "./boardModel";

type DataSet = ComponentFramework.PropertyTypes.DataSet;

/**
 * Dataset (view) control that renders requisitions as a Kanban board.
 * Moving a card calls context.webAPI.updateRecord – the RequisitionLifecyclePlugin decides if the move is legal.
 */
export class RequisitionKanban implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private context!: ComponentFramework.Context<IInputs>;
  private notifyOutputChanged!: () => void;
  private options: StageOption[] = [];
  private optionsLoadedFor: string | null = null;
  private overrides = new Map<string, number>();
  private busyCardId: string | null = null;
  private error: string | null = null;

  public init(context: ComponentFramework.Context<IInputs>, notifyOutputChanged: () => void): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    context.mode.trackContainerResize(true);
    context.parameters.records.paging.setPageSize(250);
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    this.context = context;
    const dataset = context.parameters.records;
    const stageColumn = context.parameters.stageColumn.raw ?? "fno_stage";

    void this.ensureOptions(dataset.getTargetEntityType(), stageColumn);

    // Load every page so lane totals reflect the whole view, not only the first page.
    if (!dataset.loading && dataset.paging.hasNextPage) {
      dataset.paging.loadNextPage();
    }

    const cards = moveCardsWithOverrides(this.toCards(dataset, stageColumn), this.overrides);
    const lanes = buildLanes(this.options, cards, parseHiddenStages(context.parameters.hiddenStages.raw));

    return React.createElement(KanbanBoard, {
      lanes,
      busyCardId: this.busyCardId,
      error: this.error,
      onMove: (id: string, stage: number) => void this.move(id, stage, stageColumn),
      onOpen: (id: string) => dataset.openDatasetItem(dataset.records[id].getNamedReference()),
      onDismissError: () => {
        this.error = null;
        this.notifyOutputChanged();
      },
    });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    this.overrides.clear();
  }

  private toCards(dataset: DataSet, stageColumn: string): CardData[] {
    const amountColumn = this.context.parameters.amountColumn.raw ?? "fno_totalamount";
    return dataset.sortedRecordIds.map((id) => {
      const record = dataset.records[id];
      const stage = record.getValue(stageColumn);
      const amount = record.getValue(amountColumn);
      return {
        id,
        title: record.getFormattedValue("fno_name") || record.getNamedReference().name || id,
        stage: stage === null || stage === undefined ? null : Number(stage),
        amount: typeof amount === "number" ? amount : Number(amount ?? 0),
        amountFormatted: record.getFormattedValue(amountColumn) ?? "",
        subtitle: record.getFormattedValue("fno_supplierid") ?? "",
        dueDate: record.getFormattedValue("fno_needbydate") ?? "",
      };
    });
  }

  private async ensureOptions(entityName: string, stageColumn: string): Promise<void> {
    const key = `${entityName}.${stageColumn}`;
    if (this.optionsLoadedFor === key) {
      return;
    }

    this.optionsLoadedFor = key;
    const metadata = await this.context.utils.getEntityMetadata(entityName, [stageColumn]);
    const optionSet = metadata.Attributes.get(stageColumn)?.attributeDescriptor?.OptionSet as
      | Array<{ Value: number; Label: string; Color?: string }>
      | undefined;

    this.options = (optionSet ?? []).map((o) => ({ value: o.Value, label: o.Label, color: o.Color }));
    this.notifyOutputChanged();
  }

  private async move(cardId: string, toStage: number, stageColumn: string): Promise<void> {
    const dataset = this.context.parameters.records;
    const current = dataset.records[cardId]?.getValue(stageColumn);
    if (current !== null && Number(current) === toStage) {
      return;
    }

    this.overrides.set(cardId, toStage);
    this.busyCardId = cardId;
    this.error = null;
    this.notifyOutputChanged();

    try {
      await this.context.webAPI.updateRecord(dataset.getTargetEntityType(), cardId, { [stageColumn]: toStage });
      dataset.refresh();
    } catch (error) {
      this.error = serverMessage(error);
    } finally {
      this.overrides.delete(cardId);
      this.busyCardId = null;
      this.notifyOutputChanged();
    }
  }
}

function moveCardsWithOverrides(cards: CardData[], overrides: Map<string, number>): CardData[] {
  let result = cards;
  overrides.forEach((stage, id) => (result = moveCard(result, id, stage)));
  return result;
}
