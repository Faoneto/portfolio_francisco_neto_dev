/** Pure board logic (no PCF/React dependencies) – unit tested in test/boardModel.test.ts. */

export interface StageOption {
  value: number;
  label: string;
  color?: string;
}

export interface CardData {
  id: string;
  title: string;
  stage: number | null;
  amount: number;
  amountFormatted: string;
  subtitle: string;
  dueDate: string;
}

export interface Lane {
  stage: StageOption;
  cards: CardData[];
  total: number;
}

export function parseHiddenStages(value: string | null | undefined): Set<number> {
  return new Set(
    (value ?? "")
      .split(",")
      .map((v) => Number(v.trim()))
      .filter((v) => Number.isInteger(v) && v > 0),
  );
}

/** Groups cards into lanes following the choice order; cards with unknown/hidden stages are skipped. */
export function buildLanes(options: StageOption[], cards: CardData[], hidden: Set<number>): Lane[] {
  const lanes = options
    .filter((o) => !hidden.has(o.value))
    .map<Lane>((stage) => ({ stage, cards: [], total: 0 }));

  const byStage = new Map(lanes.map((l) => [l.stage.value, l]));
  for (const card of cards) {
    const lane = card.stage === null ? undefined : byStage.get(card.stage);
    if (lane) {
      lane.cards.push(card);
      lane.total += card.amount;
    }
  }

  return lanes;
}

/**
 * Optimistic move used for instant feedback; reverted if the server rejects the update.
 * Returns a new array (immutability keeps React rendering predictable).
 */
export function moveCard(cards: CardData[], cardId: string, toStage: number): CardData[] {
  return cards.map((c) => (c.id === cardId ? { ...c, stage: toStage } : c));
}

/** Extracts the user-facing message from a Web API error (plugin InvalidPluginExecutionException text). */
export function serverMessage(error: unknown): string {
  if (error && typeof error === "object" && "message" in error) {
    const message = String((error as { message: unknown }).message);
    if (message.trim().length > 0) {
      return message;
    }
  }

  return "Não foi possível mover a requisição.";
}

export function formatTotal(value: number): string {
  return value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}
