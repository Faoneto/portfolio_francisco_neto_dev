import {
  buildSubmitRequest,
  describeResult,
  errorMessage,
  isResendVisible,
  isSubmitVisible,
  resendToSap,
  submit,
} from "../src/commands/requisition.commands";
import { IntegrationStatus, Requisition, RequisitionStage } from "../src/shared/schema";
import { FakeForm } from "./fakeForm";

type XrmMock = {
  Navigation: Record<string, jest.Mock>;
  Utility: Record<string, jest.Mock>;
  WebApi: { online: { execute: jest.Mock } };
};

function installXrm(confirmed: boolean, execute: jest.Mock): XrmMock {
  const xrm: XrmMock = {
    Navigation: {
      openConfirmDialog: jest.fn().mockResolvedValue({ confirmed }),
      openAlertDialog: jest.fn().mockResolvedValue(undefined),
      openErrorDialog: jest.fn().mockResolvedValue(undefined),
    },
    Utility: { showProgressIndicator: jest.fn(), closeProgressIndicator: jest.fn() },
    WebApi: { online: { execute } },
  };
  (globalThis as unknown as { Xrm: XrmMock }).Xrm = xrm;
  return xrm;
}

const form = (stage: RequisitionStage, integration = IntegrationStatus.NotSent): FakeForm =>
  new FakeForm({ [Requisition.stage]: stage, [Requisition.integrationStatus]: integration });

describe("requisition commands", () => {
  it("builds the bound Custom API request", () => {
    const request = buildSubmitRequest("{6F9619FF-8B86-D011-B42D-00C04FC964FF}");
    const metadata = request.getMetadata();

    expect(request.entity).toEqual({ entityType: "fno_purchaserequisition", id: "6F9619FF-8B86-D011-B42D-00C04FC964FF" });
    expect(metadata.operationName).toBe("fno_SubmitRequisition");
    expect(metadata.boundParameter).toBe("entity");
    expect(metadata.parameterTypes.entity.typeName).toBe("mscrm.fno_purchaserequisition");
    expect(metadata.operationType).toBe(0);
  });

  it.each([
    [RequisitionStage.Draft, 2, true],
    [RequisitionStage.Rejected, 2, true],
    [RequisitionStage.PendingApproval, 2, false],
    [RequisitionStage.Draft, 1, false],
  ])("submit visibility for stage %s / form type %s = %s", (stage, formType, expected) => {
    const f = form(stage);
    f.formType = formType;
    expect(isSubmitVisible(f.asXrm())).toBe(expected);
  });

  it("shows resend only for failed SAP integrations", () => {
    expect(isResendVisible(form(RequisitionStage.SentToSap, IntegrationStatus.Failed).asXrm())).toBe(true);
    expect(isResendVisible(form(RequisitionStage.SentToSap, IntegrationStatus.Queued).asXrm())).toBe(false);
  });

  it("submits, shows the approval level and refreshes", async () => {
    const execute = jest.fn().mockResolvedValue({
      json: async () => ({ ApprovalLevel: 100000003, IsOverBudget: true, Message: "Excede o orçamento." }),
    });
    const xrm = installXrm(true, execute);
    const f = form(RequisitionStage.Draft);
    f.dirty = true;

    await submit(f.asXrm());

    expect(f.saved).toBe(1);
    expect(execute).toHaveBeenCalledTimes(1);
    expect(xrm.Navigation.openAlertDialog.mock.calls[0][0].text).toContain("CFO");
    expect(f.refreshed).toBe(1);
    expect(xrm.Utility.closeProgressIndicator).toHaveBeenCalled();
  });

  it("does nothing when the user cancels the confirmation", async () => {
    const execute = jest.fn();
    installXrm(false, execute);

    await submit(form(RequisitionStage.Draft).asXrm());

    expect(execute).not.toHaveBeenCalled();
  });

  it("shows the server validation message on failure", async () => {
    const execute = jest.fn().mockRejectedValue({ message: "A requisição não pode ser enviada para aprovação" });
    const xrm = installXrm(true, execute);

    await submit(form(RequisitionStage.Draft).asXrm());

    expect(xrm.Navigation.openErrorDialog).toHaveBeenCalledWith({ message: "A requisição não pode ser enviada para aprovação" });
    expect(xrm.Utility.closeProgressIndicator).toHaveBeenCalled();
  });

  it("resend moves the requisition back to approved and saves", async () => {
    installXrm(true, jest.fn());
    const f = form(RequisitionStage.SentToSap, IntegrationStatus.Failed);

    await resendToSap(f.asXrm());

    expect(f.getAttribute(Requisition.stage)!.value).toBe(RequisitionStage.Approved);
    expect(f.saved).toBe(1);
  });

  it("formats results and errors", () => {
    expect(describeResult({ ApprovalLevel: 100000001, IsOverBudget: false, Message: "ok" })).toContain("Gestor");
    expect(errorMessage("boom")).toBe("Não foi possível concluir a operação.");
  });
});
