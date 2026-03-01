using Microsoft.EntityFrameworkCore;
using Revix.Core.Entities;

namespace Revix.Infrastructure;

public class RevixDbContext : DbContext 
{
    public RevixDbContext(DbContextOptions<RevixDbContext> options) : base(options)
    {}

    public DbSet<User> Users { get; set; }
    public DbSet<Repository> Repositories { get; set; }
    public DbSet<Review> Reviews { get; set; }
    public DbSet<ReviewComment> ReviewComments { get; set; }    
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasMany(u => u.Repositories)
            .WithOne(r => r.User)
            .HasForeignKey(r => r.UserId);

        modelBuilder.Entity<Repository>()
            .HasMany(r => r.Reviews)
            .WithOne(rv => rv.Repository)
            .HasForeignKey(rv => rv.RepositoryId);

        modelBuilder.Entity<Review>()
            .HasMany(rv => rv.ReviewComments)
            .WithOne(rc => rc.Review)
            .HasForeignKey(rc => rc.ReviewId);
    }
}