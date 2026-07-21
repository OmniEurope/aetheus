// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using Aetheus.Agent.Core.Services;

namespace Aetheus.Agent.Windows.Services;

public sealed class WindowsCredentialProtector : ICredentialProtector
{
    public byte[] Protect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
    }
}
