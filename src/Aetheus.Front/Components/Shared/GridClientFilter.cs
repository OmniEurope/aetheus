// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Front.Components.Shared;

internal static class GridClientFilter
{
    public static bool Matches(object? value, GridFilterDescriptor filter) =>
        MatchesCondition(value, filter.Operator, filter.FilterValue?.ToString())
        && (filter.SecondOperator is not { } second || MatchesCondition(value, second, filter.SecondValue));

    private static bool MatchesCondition(object? value, OmniDataGridFilterOperator operation, string? expected)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (operation == OmniDataGridFilterOperator.IsNull) return value is null;
        if (operation == OmniDataGridFilterOperator.IsNotNull) return value is not null;
        if (operation == OmniDataGridFilterOperator.IsEmpty) return text.Length == 0;
        if (operation == OmniDataGridFilterOperator.IsNotEmpty) return text.Length > 0;
        if (string.IsNullOrEmpty(expected)) return true;
        if (OmniDataGridFilterValues.IsMultiValued(operation))
        {
            var found = OmniDataGridFilterValues.Split(expected).Contains(text, StringComparer.OrdinalIgnoreCase);
            return operation == OmniDataGridFilterOperator.In ? found : !found;
        }
        if (value is null) return false;
        return TryCompare(value, text, expected, out var comparison) && Evaluate(operation, comparison, text, expected);
    }

    /// <summary>Compares a typed value (date, date with offset, number) with the filter text parsed to the
    /// same type; any other value compares as text. False when the filter text does not parse.</summary>
    private static bool TryCompare(object value, string text, string expected, out int comparison)
    {
        comparison = string.Compare(text, expected, StringComparison.OrdinalIgnoreCase);
        if (value is DateTime date)
        {
            if (!DateTime.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.None, out var bound)) return false;
            comparison = date.CompareTo(bound);
        }
        else if (value is DateTimeOffset offset)
        {
            if (!DateTimeOffset.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var bound)) return false;
            comparison = offset.CompareTo(bound);
        }
        else if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            if (!decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var bound)) return false;
            comparison = Convert.ToDecimal(value, CultureInfo.InvariantCulture).CompareTo(bound);
        }
        return true;
    }

    private static bool Evaluate(OmniDataGridFilterOperator operation, int comparison, string text, string expected) =>
        operation switch
        {
            OmniDataGridFilterOperator.Equals => comparison == 0,
            OmniDataGridFilterOperator.NotEquals => comparison != 0,
            OmniDataGridFilterOperator.GreaterThan => comparison > 0,
            OmniDataGridFilterOperator.GreaterThanOrEquals => comparison >= 0,
            OmniDataGridFilterOperator.LessThan => comparison < 0,
            OmniDataGridFilterOperator.LessThanOrEquals => comparison <= 0,
            OmniDataGridFilterOperator.StartsWith => text.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            OmniDataGridFilterOperator.EndsWith => text.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            OmniDataGridFilterOperator.DoesNotContain => !text.Contains(expected, StringComparison.OrdinalIgnoreCase),
            OmniDataGridFilterOperator.Contains => text.Contains(expected, StringComparison.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
}
