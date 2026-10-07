import * as React from "react";
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { CnpjField, CnpjFieldProps } from "./CnpjField";

/**
 * Virtual (React) control: the platform provides React/Fluent, keeping the bundle small and the
 * look consistent with the model-driven app.
 */
export class CnpjInput implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private notifyOutputChanged!: () => void;
  private value: string | undefined;

  public init(context: ComponentFramework.Context<IInputs>, notifyOutputChanged: () => void): void {
    this.notifyOutputChanged = notifyOutputChanged;
    this.value = context.parameters.value.raw ?? undefined;
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    const props: CnpjFieldProps = {
      value: context.parameters.value.raw ?? null,
      disabled: context.mode.isControlDisabled,
      showStatusBadge: context.parameters.showStatusBadge?.raw !== false,
      onChange: (value) => {
        this.value = value ?? undefined;
        this.notifyOutputChanged();
      },
    };

    return React.createElement(CnpjField, props);
  }

  public getOutputs(): IOutputs {
    return { value: this.value };
  }

  public destroy(): void {
    // Nothing to clean up: React tree is unmounted by the platform for virtual controls.
  }
}
