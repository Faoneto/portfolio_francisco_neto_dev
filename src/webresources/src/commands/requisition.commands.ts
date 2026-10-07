import { ApprovalLevelLabels, editableStages, FormType, IntegrationStatus, Requisition, RequisitionStage, SubmitRequisitionApi } from "../shared/schema";
import { getChoice } from "../shared/form";

/**
 * Command bar (modern commanding) handlers for the purchase requisition form.
 * Library: fno_/js/requisition.commands.js – parameter: PrimaryControl.
 *   "Enviar para aprovação" -> SupplyFlow.RequisitionCommands.submit         | visibility: isSubmitVisible
 *   "Reenviar ao SAP"       -> SupplyFlow.RequisitionCommands.resendToSap    | visibility: isResendVisible
 */

/** Shape expected by Xrm.WebApi.online.execute (typed as "any" in @types/xrm). */
export interface WebApiRequest {
  entity: { entityType: string; id: string };
  getMetadata(): {
    boundParameter: string | null;
    parameterTypes: Record<string, { typeName: string; structuralProperty: number }>;
    operationType: 0 | 1 | 2;
    operationName: string;
  };
}

export interface SubmitResult {
  ApprovalLevel: number;
  IsOverBudget: boolean;
  Message: string;
}

/** Builds the Xrm.WebApi request for the bound Custom API fno_SubmitRequisition. */
export function buildSubmitRequest(requisitionId: string): WebApiRequest {
  return {
    entity: { entityType: Requisition.entityName, id: requisitionId.replace(/[{}]/g, "") },
    getMetadata: () => ({
      boundParameter: "entity",
      parameterTypes: {
        entity: { typeName: `mscrm.${Requisition.entityName}`, structuralProperty: 5 },
      },
      operationType: 0, // Action
      operationName: SubmitRequisitionApi,
    }),
  };
}

export function isSubmitVisible(primaryControl: Xrm.FormContext): boolean {
  const stage = getChoice(primaryControl, Requisition.stage);
  return primaryControl.ui.getFormType() === FormType.Update && stage !== null && editableStages.has(stage);
}

export function isResendVisible(primaryControl: Xrm.FormContext): boolean {
  return (
    getChoice(primaryControl, Requisition.stage) === RequisitionStage.SentToSap &&
    getChoice(primaryControl, Requisition.integrationStatus) === IntegrationStatus.Failed
  );
}

export async function submit(primaryControl: Xrm.FormContext): Promise<void> {
  const confirm = await Xrm.Navigation.openConfirmDialog({
    title: "Enviar para aprovação",
    text: "A requisição será validada e enviada para o aprovador da alçada correspondente. Deseja continuar?",
    confirmButtonLabel: "Enviar",
    cancelButtonLabel: "Cancelar",
  });

  if (!confirm.confirmed) {
    return;
  }

  // Persist pending edits first so the server validates what the user sees.
  if (primaryControl.data.entity.getIsDirty()) {
    await primaryControl.data.save();
  }

  Xrm.Utility.showProgressIndicator("Validando e enviando para aprovação…");
  try {
    const response = await Xrm.WebApi.online.execute(buildSubmitRequest(primaryControl.data.entity.getId()));
    const result = (await response.json()) as SubmitResult;
    Xrm.Utility.closeProgressIndicator();

    await Xrm.Navigation.openAlertDialog({
      title: "Requisição enviada",
      text: describeResult(result),
    });
    await primaryControl.data.refresh(false);
  } catch (error) {
    Xrm.Utility.closeProgressIndicator();
    await Xrm.Navigation.openErrorDialog({ message: errorMessage(error) });
  }
}

export async function resendToSap(primaryControl: Xrm.FormContext): Promise<void> {
  // SentToSap -> Approved re-triggers the Service Bus step; budget is not committed twice (fno_budgetcommitted).
  primaryControl.getAttribute<Xrm.Attributes.OptionSetAttribute>(Requisition.stage)?.setValue(RequisitionStage.Approved);
  try {
    await primaryControl.data.save();
  } catch (error) {
    await Xrm.Navigation.openErrorDialog({ message: errorMessage(error) });
  }
}

export function describeResult(result: SubmitResult): string {
  const level = ApprovalLevelLabels[result.ApprovalLevel] ?? "—";
  const budget = result.IsOverBudget ? " Atenção: valor acima do orçamento disponível." : "";
  return `Aprovador necessário: ${level}.${budget}\n\n${result.Message}`;
}

export function errorMessage(error: unknown): string {
  if (error && typeof error === "object" && "message" in error) {
    return String((error as { message: unknown }).message);
  }

  return "Não foi possível concluir a operação.";
}
