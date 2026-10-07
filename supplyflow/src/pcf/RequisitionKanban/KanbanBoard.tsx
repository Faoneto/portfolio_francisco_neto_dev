import * as React from "react";
import {
  Badge,
  Caption1,
  Card,
  CardHeader,
  FluentProvider,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
  webLightTheme,
} from "@fluentui/react-components";
import { CardData, Lane, formatTotal } from "./boardModel";

const useStyles = makeStyles({
  board: { display: "flex", ...shorthands.gap("12px"), overflowX: "auto", ...shorthands.padding("8px"), minHeight: "420px" },
  lane: {
    flex: "0 0 280px",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius(tokens.borderRadiusLarge),
    ...shorthands.padding("8px"),
  },
  laneOver: { outlineStyle: "dashed", outlineWidth: "2px", outlineColor: tokens.colorBrandStroke1 },
  laneHeader: { display: "flex", justifyContent: "space-between", alignItems: "center", ...shorthands.padding("4px", "4px", "8px") },
  cards: { display: "flex", flexDirection: "column", ...shorthands.gap("8px"), overflowY: "auto" },
  card: { cursor: "grab" },
  meta: { display: "flex", justifyContent: "space-between", color: tokens.colorNeutralForeground3 },
});

export interface KanbanBoardProps {
  lanes: Lane[];
  busyCardId: string | null;
  error: string | null;
  onMove: (cardId: string, toStage: number) => void;
  onOpen: (cardId: string) => void;
  onDismissError: () => void;
}

export const KanbanBoard: React.FC<KanbanBoardProps> = ({ lanes, busyCardId, error, onMove, onOpen, onDismissError }) => {
  const styles = useStyles();
  const [overLane, setOverLane] = React.useState<number | null>(null);

  return (
    <FluentProvider theme={webLightTheme}>
      {error && (
        <MessageBar intent="error" onClick={onDismissError}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      <div className={styles.board} role="list" aria-label="Requisições por etapa">
        {lanes.map((lane) => (
          <section
            key={lane.stage.value}
            className={`${styles.lane} ${overLane === lane.stage.value ? styles.laneOver : ""}`}
            aria-label={lane.stage.label}
            onDragOver={(e) => {
              e.preventDefault();
              setOverLane(lane.stage.value);
            }}
            onDragLeave={() => setOverLane(null)}
            onDrop={(e) => {
              e.preventDefault();
              setOverLane(null);
              const cardId = e.dataTransfer.getData("text/plain");
              if (cardId) onMove(cardId, lane.stage.value);
            }}
          >
            <header className={styles.laneHeader}>
              <Text weight="semibold">
                <span style={{ color: lane.stage.color }}>●</span> {lane.stage.label}
              </Text>
              <Badge appearance="tint">{lane.cards.length}</Badge>
            </header>
            <Caption1 style={{ padding: "0 4px 8px" }}>{formatTotal(lane.total)}</Caption1>
            <div className={styles.cards}>
              {lane.cards.map((card) => (
                <RequisitionCard key={card.id} card={card} busy={busyCardId === card.id} onOpen={onOpen} className={styles.card} metaClass={styles.meta} />
              ))}
            </div>
          </section>
        ))}
      </div>
    </FluentProvider>
  );
};

const RequisitionCard: React.FC<{ card: CardData; busy: boolean; onOpen: (id: string) => void; className: string; metaClass: string }> = ({
  card,
  busy,
  onOpen,
  className,
  metaClass,
}) => (
  <Card
    className={className}
    draggable={!busy}
    onDragStart={(e: React.DragEvent) => e.dataTransfer.setData("text/plain", card.id)}
    onDoubleClick={() => onOpen(card.id)}
    onKeyDown={(e: React.KeyboardEvent) => e.key === "Enter" && onOpen(card.id)}
    tabIndex={0}
    role="listitem"
    aria-busy={busy}
  >
    <CardHeader header={<Text weight="semibold">{card.title}</Text>} action={busy ? <Spinner size="tiny" /> : undefined} />
    <Caption1>{card.subtitle}</Caption1>
    <div className={metaClass}>
      <Caption1>{card.amountFormatted}</Caption1>
      <Caption1>{card.dueDate}</Caption1>
    </div>
  </Card>
);
