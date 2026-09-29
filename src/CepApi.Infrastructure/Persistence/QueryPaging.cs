namespace CepApi.Infrastructure.Persistence;

public static class QueryPaging
{
    // Compute before converting to EF's int offset: a large valid page must be
    // empty, rather than overflow into a negative SQL OFFSET.
    public static IQueryable<T> Page<T>(this IOrderedQueryable<T> query, int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var offset = ((long)page - 1) * pageSize;
        return offset > int.MaxValue ? query.Where(_ => false) : query.Skip((int)offset).Take(pageSize);
    }
}
