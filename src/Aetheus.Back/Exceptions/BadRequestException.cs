// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Exceptions;

public sealed class BadRequestException(string message) : Exception(message);
