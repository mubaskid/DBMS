using System.Linq.Expressions;
using System.Reflection;
using Model.Common.Contracts;
using Model.Entities;
using Microsoft.EntityFrameworkCore;

namespace Persistence.Extensions;

public static class DbContextExtension
{

    /// <summary>
    ///     Auto initialize or update currently date time for any class based on class ModelBase
    /// </summary>
    public static void AddAuditInfo(this DbContext dbContext)
    {
        var entries = dbContext.ChangeTracker.Entries().Where(e =>
            (e.Entity is IAuditableEntity) && (e.State is EntityState.Added || e.State is EntityState.Modified || e.State is EntityState.Deleted));

        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Added)
            {
                ((IAuditableEntity)entry.Entity).CreatedDate = DateTime.UtcNow;

            }

            ((IAuditableEntity)entry.Entity).LastModifiedDate = DateTime.UtcNow;
        }
    }

    /// <summary>
    ///     Auto find and apply all IEntityTypeConfiguration to modelBuilder
    /// </summary>
    public static void ApplyAllConfigurations<TDbContext>(this ModelBuilder modelBuilder)
        where TDbContext : DbContext
    {
        var applyConfigurationMethodInfo = modelBuilder
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m => m.Name.Equals("ApplyConfiguration", StringComparison.OrdinalIgnoreCase));

        var ret = typeof(TDbContext).Assembly
            .GetTypes()
            .Select(t => (t,
                i: t.GetInterfaces().FirstOrDefault(i =>
                    i.Name.Equals(typeof(IEntityTypeConfiguration<>).Name, StringComparison.Ordinal))))
            .Where(it => it.i != null)
            .Select(it => (et: it.i.GetGenericArguments()[0], cfgObj: Activator.CreateInstance(it.t)))
            .Select(it =>
                applyConfigurationMethodInfo.MakeGenericMethod(it.et).Invoke(modelBuilder, new[] { it.cfgObj }))
            .ToList();
    }

    public static void ConfigureDeletableEntities(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entity.ClrType))
            {
                modelBuilder
                   .Entity(entity.ClrType)
                   .HasQueryFilter(GetIsDeletedRestriction(entity.ClrType));
            }
        }
    }

    private static LambdaExpression GetIsDeletedRestriction(Type type)
    {
        var param = Expression.Parameter(type,
                                         "it");
        var prop = Expression.Call(_propertyMethod,
                                   param,
                                   Expression.Constant(IsDeletedProperty));
        var condition = Expression.MakeBinary(ExpressionType.Equal,
                                              prop,
                                              Expression.Constant(false));
        var lambda = Expression.Lambda(condition,
                                       param);
        return lambda;
    }

    private const string IsDeletedProperty = "IsDeleted";

    private static readonly MethodInfo _propertyMethod = typeof(EF).GetMethod(nameof(EF.Property),
                                                                              BindingFlags.Static |
                                                                              BindingFlags.Public)
                                                                   .MakeGenericMethod(typeof(bool));
}