using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ApiTokenDTOEntityConfiguration : IEntityTypeConfiguration<ApiTokenDTO>
{
    public void Configure(EntityTypeBuilder<ApiTokenDTO> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
        entity.Property(e => e.Name).IsRequired().HasMaxLength(ApiTokenRules.MaxNameLength);
        entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength();
        entity.Property(e => e.Prefix).IsRequired().HasMaxLength(16);
        entity.HasIndex(e => e.TokenHash).IsUnique();
        // A name is unique among a user's live tokens; a revoked token frees its name.
        entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique().HasFilter("\"RevokedUtc\" IS NULL");
        entity.HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.ToTable("ApiTokens");
    }
}
