// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Exceptions;

public sealed class ConflictException(string message) : Exception(message);
