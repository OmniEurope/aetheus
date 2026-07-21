// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

public enum DeploymentStrategyType
{
    RunOnce = 0,
    Rolling = 1,
    Canary = 2,
    BlueGreen = 3
}
