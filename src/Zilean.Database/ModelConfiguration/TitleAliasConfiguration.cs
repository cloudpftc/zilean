namespace Zilean.Database.ModelConfiguration;

public class TitleAliasConfiguration : IEntityTypeConfiguration<TitleAlias>
{
    public void Configure(EntityTypeBuilder<TitleAlias> builder)
    {
        builder.ToTable("title_aliases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RawTitle)
            .IsRequired();

        builder.Property(x => x.CanonicalTitle)
            .IsRequired();

        builder.Property(x => x.AliasType)
            .HasMaxLength(30);

        builder.HasIndex(x => x.CanonicalTitle);
        builder.HasIndex(x => x.RawTitle);
        builder.HasIndex(x => x.AliasType);
        
        // Composite index for fast alias lookup
        builder.HasIndex(x => new { x.RawTitle, x.AliasType });
    }
}
