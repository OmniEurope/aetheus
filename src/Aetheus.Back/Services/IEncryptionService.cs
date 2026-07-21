// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

public interface IEncryptionService
{
    string EncryptValue(string value);
    string DecryptValue(string encryptedBase64);
}
