namespace Zilean.Database.ModelConfiguration;

public class FileAuditLogConfiguration : IEntityTypeConfiguration<FileAuditLog>
{
    public void Configure(EntityTypeBuilder<FileAuditLog> builder)
    {
        builder.ToTable("FileAuditLogs");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("integer")
            .ValueGeneratedOnAdd()
            .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        builder.Property(t => t.Operation)
            .IsRequired()
            .HasColumnType("text")
            .HasMaxLength(50)
            .HasAnnotation("Relational:JsonPropertyName", "operation");

        builder.Property(t => t.FilePath)
            .HasColumnType("text")
            .HasMaxLength(1000)
            .HasAnnotation("Relational:JsonPropertyName", "file_path");

        builder.Property(t => t.Status)
            .IsRequired()
            .HasColumnType("text")
            .HasMaxLength(20)
            .HasAnnotation("Relational:JsonPropertyName", "status");

        builder.Property(t => t.Timestamp)
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasDefaultValueSql("now() at time zone 'utc'")
            .HasAnnotation("Relational:JsonPropertyName", "timestamp");

        builder.Property(t => t.Details)
            .HasColumnName("DetailsJson")
            .HasColumnType("text")
            .HasAnnotation("Relational:JsonPropertyName", "details");

        builder.Property(t => t.DurationMs)
            .HasColumnType("integer")
            .IsRequired()
            .HasDefaultValue(0)
            .HasAnnotation("Relational:JsonPropertyName", "duration_ms");

        builder.HasIndex(t => t.Timestamp)
            .HasDatabaseName("IX_FileAuditLogs_Timestamp")
            .IsDescending();

        builder.HasIndex(t => t.Operation)
            .HasDatabaseName("IX_FileAuditLogs_Operation");
    }
}
