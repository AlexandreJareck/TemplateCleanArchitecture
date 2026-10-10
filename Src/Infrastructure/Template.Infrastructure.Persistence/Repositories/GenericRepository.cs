#nullable disable
using Microsoft.EntityFrameworkCore;
using Template.Application.DTOs;
using Template.Application.Interfaces.Repositories;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;
public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    public GenericRepository(ApplicationDbContext dbContext)
    {
        DbContext = dbContext;
    }

    protected ApplicationDbContext DbContext { get; }

    public virtual async Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<T>().FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await DbContext.Set<T>().AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(T entity)
    {
        DbContext.Entry(entity).State = EntityState.Modified;
    }

    public void Delete(T entity)
    {
        DbContext.Set<T>().Remove(entity);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext
             .Set<T>()
             .AsNoTracking()
             .ToListAsync(cancellationToken);
    }

    protected async Task<PaginationResponseDto<TEntity>> Paged<TEntity>(IQueryable<TEntity> query, int pageNumber, int pageSize, CancellationToken cancellationToken = default) where TEntity : class
    {
        var count = await query.CountAsync(cancellationToken);

        var pagedResult = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new(pagedResult, count, pageNumber, pageSize);
    }
}

