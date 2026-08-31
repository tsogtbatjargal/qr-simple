using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Rebuild> Rebuilds => Set<Rebuild>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Equipment>()
            .Property(e => e.Status)
            .HasConversion<string>();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.IsActive)
            .HasDefaultValue(true);

        // Unlike Document.EquipmentId (few rows per equipment, no index needed), rebuilds
        // grow without bound per equipment and every page load filters on exactly this pair.
        modelBuilder.Entity<Rebuild>()
            .HasIndex(r => new { r.EquipmentId, r.RebuildDate });

        modelBuilder.Entity<Rebuild>()
            .Property(r => r.Note)
            .HasMaxLength(RebuildCatalog.MaxNoteLength);

        // No foreign key on EquipmentId, matching Document — there is no hard-delete for
        // Equipment (only retire), so nothing to cascade. Deliberate, not an oversight.
    }
}
