import { CardData, buildLanes, formatTotal, moveCard, parseHiddenStages, serverMessage } from "../RequisitionKanban/boardModel";

const options = [
  { value: 100000000, label: "Rascunho" },
  { value: 100000001, label: "Em aprovação" },
  { value: 100000002, label: "Aprovada" },
  { value: 100000006, label: "Cancelada" },
];

const card = (id: string, stage: number | null, amount: number): CardData => ({
  id,
  title: id,
  stage,
  amount,
  amountFormatted: String(amount),
  subtitle: "",
  dueDate: "",
});

describe("board model", () => {
  it("groups cards in choice order and sums totals", () => {
    const lanes = buildLanes(options, [card("a", 100000001, 10), card("b", 100000001, 5.5), card("c", 100000000, 1)], new Set());

    expect(lanes.map((l) => l.stage.label)).toEqual(["Rascunho", "Em aprovação", "Aprovada", "Cancelada"]);
    expect(lanes[1].cards.map((c) => c.id)).toEqual(["a", "b"]);
    expect(lanes[1].total).toBe(15.5);
    expect(lanes[2].cards).toHaveLength(0);
  });

  it("skips hidden lanes and cards without stage", () => {
    const lanes = buildLanes(options, [card("x", 100000006, 1), card("y", null, 1)], parseHiddenStages("100000006, abc"));

    expect(lanes.map((l) => l.stage.value)).not.toContain(100000006);
    expect(lanes.every((l) => l.cards.length === 0)).toBe(true);
  });

  it("moves a card immutably", () => {
    const cards = [card("a", 100000000, 1)];
    const moved = moveCard(cards, "a", 100000001);

    expect(moved[0].stage).toBe(100000001);
    expect(cards[0].stage).toBe(100000000);
  });

  it("surfaces the plugin message from Web API errors", () => {
    expect(serverMessage({ message: "Transição inválida: \"Rascunho\" → \"Aprovada\"." })).toContain("Transição inválida");
    expect(serverMessage(new Error(""))).toBe("Não foi possível mover a requisição.");
    expect(serverMessage(undefined)).toBe("Não foi possível mover a requisição.");
  });

  it("formats BRL totals", () => {
    expect(formatTotal(1234.5)).toMatch(/1\.234,50/);
  });
});
