using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class CacheGenerationDTOEntityConfiguration : IEntityTypeConfiguration<CacheGenerationDTO>
{
    public void Configure(EntityTypeBuilder<CacheGenerationDTO> entity)
    {
        entity.ToTable("CacheGenerations");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        // One row for the whole database, created lazily by the bump statement (an upsert) rather
        // than a model HasData seed: host migration contexts exclude this table but EF still diffs
        // seed data of excluded tables, so a HasData row would demand a spurious host migration.
    }
}
