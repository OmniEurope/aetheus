// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;

public interface ICredentialProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}
