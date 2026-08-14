using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ChangeSetEntityConfiguration : IEntityTypeConfiguration<ChangeSet>
{
    public void Configure(EntityTypeBuilder<ChangeSet> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.RootNodeId);
        entity.ToTable("ChangeSets");
    }
}
