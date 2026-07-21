// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Validation;

/// <summary>Accepts an empty optional value, or an absolute HTTPS URL. Repository URLs are cloned
/// by agents and can carry credentials, so allowing plain HTTP here would downgrade transport
/// security before the Git client has a chance to enforce its own policy.</summary>
public sealed class HttpsUrlAttribute : ValidationAttribute
{
    public HttpsUrlAttribute()
        : base("The {0} field must be an absolute HTTPS URL.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.ToString())) return true;
        return Uri.TryCreate(value.ToString(), UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
