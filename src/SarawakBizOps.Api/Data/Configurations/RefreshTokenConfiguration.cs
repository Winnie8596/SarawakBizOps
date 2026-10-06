using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SarawakBizOps.Api.Models.Entities;

namespace SarawakBizOps.Api.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        // Hex SHA-256 of the token; looked up on every refresh.
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.Property(t => t.SecurityStamp).IsRequired().HasMaxLength(256);

        // A user's sessions go with the user; the index also serves "end all of this user's sessions".
        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
