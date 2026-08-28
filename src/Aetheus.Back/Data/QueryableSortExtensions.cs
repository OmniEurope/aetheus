// SPDX-License-Identifier: EUPL-1.2
using System.Linq.Expressions;
using System.Reflection;

namespace Aetheus.Back.Data;

/// <summary>Applies a grid's sort key to a query.
/// <para>Server-paged grids have to sort server-side: ordering the loaded page instead orders 25 rows
/// and presents it as the order of the whole set, which is an affordance that looks like it works
/// while telling the reader something false.</para></summary>
public static class QueryableSortExtensions
{
    /// <summary>Orders by the named public property, falling back to <paramref name="fallback"/> when
    /// the name is missing, unknown, or not a readable property of <typeparamref name="T"/>.
    /// <para>The name never reaches SQL as text: it is resolved against the CLR type and the ordering
    /// is built as a typed expression, so an unknown or hostile key can only miss the lookup and land
    /// on the fallback. Matching is case-insensitive because grids send the column's property name as
    /// declared, which does not always match the entity's casing.</para></summary>
    public static IQueryable<T> OrderByProperty<T, TFallback>(
        this IQueryable<T> query,
        string? sortBy,
        bool descending,
        Expression<Func<T, TFallback>> fallback,
        bool fallbackDescending = true)
    {
        var property = ResolveProperty<T>(sortBy);
        if (property is null)
        {
            // The direction is dropped along with the key. Honouring it on its own would let an
            // unrecognised column silently reverse the list, which reads as a sort nobody asked for.
            return fallbackDescending ? query.OrderByDescending(fallback) : query.OrderBy(fallback);
        }

        return query.OrderByResolved(property, descending);
    }

    /// <summary>Same resolution, for a query whose default order is composite (several keys, or keys
    /// that encode a rule such as pinning drafts below published rows). Collapsing such an order into a
    /// single fallback key would quietly lose the rule, so the caller supplies the whole default order
    /// and it is used untouched whenever no usable sort key was requested.</summary>
    public static IQueryable<T> OrderByProperty<T>(
        this IQueryable<T> query,
        string? sortBy,
        bool descending,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrder)
    {
        var property = ResolveProperty<T>(sortBy);
        return property is null ? defaultOrder(query) : query.OrderByResolved(property, descending);
    }

    private static IQueryable<T> OrderByResolved<T>(this IQueryable<T> query, PropertyInfo property, bool descending)
    {
        var parameter = Expression.Parameter(typeof(T), "row");
        var selector = Expression.Lambda(Expression.Property(parameter, property), parameter);

        var call = Expression.Call(
            typeof(Queryable),
            descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy),
            [typeof(T), property.PropertyType],
            query.Expression,
            Expression.Quote(selector));

        return query.Provider.CreateQuery<T>(call);
    }

    private static PropertyInfo? ResolveProperty<T>(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy)) return null;

        var property = typeof(T).GetProperty(
            sortBy,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // Only readable scalar-ish properties: ordering by a collection navigation is not something a
        // provider can translate, and letting it through would turn a stray key into a runtime failure
        // on a page that was merely sorted.
        if (property is null || !property.CanRead) return null;
        if (property.PropertyType != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType))
        {
            return null;
        }

        return property;
    }
}
