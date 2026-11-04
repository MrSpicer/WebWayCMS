using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class UserProfileDTOEntityConfiguration : IEntityTypeConfiguration<UserProfileDTO>
{
    public void Configure(EntityTypeBuilder<UserProfileDTO> entity)
    {
        entity.HasKey(e => e.UserId);
        entity.Property(e => e.UserId).HasMaxLength(450);
        entity.Property(e => e.DisplayName).HasMaxLength(UserProfileRules.MaxNameLength);
        entity.Property(e => e.FirstName).HasMaxLength(UserProfileRules.MaxNameLength);
        entity.Property(e => e.LastName).HasMaxLength(UserProfileRules.MaxNameLength);
        entity.Property(e => e.DeletedBy).HasMaxLength(450);
        entity.Property(e => e.InvitedBy).HasMaxLength(450);
        entity.HasOne<IdentityUser>()
            .WithOne()
            .HasForeignKey<UserProfileDTO>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.ToTable("UserProfiles");
    }
}
