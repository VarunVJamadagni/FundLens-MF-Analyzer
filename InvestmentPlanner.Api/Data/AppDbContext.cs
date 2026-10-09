using InvestmentPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentPlanner.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<FundAnalytics> FundAnalytics => Set<FundAnalytics>();

    public DbSet<FundAnalyticsFailure> FundAnalyticsFailures =>
        Set<FundAnalyticsFailure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FundAnalytics>(entity =>
        {
            entity.HasKey(x => x.SchemeCode);

            entity.Property(x => x.FundHouse)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.SchemeName)
                .HasMaxLength(300)
                .IsRequired();

            entity.Property(x => x.Plan)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Option)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Category)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.SubCategory)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.LastNavDate)
                .HasColumnType("date");

            entity.Property(x => x.CurrentNAV)
                .HasPrecision(18, 4);

            entity.Property(x => x.Return1Month)
                .HasPrecision(10, 4);

            entity.Property(x => x.Return3Month)
                .HasPrecision(10, 4);

            entity.Property(x => x.Return6Month)
                .HasPrecision(10, 4);

            entity.Property(x => x.CAGR1Year)
                .HasPrecision(10, 4);

            entity.Property(x => x.CAGR3Year)
                .HasPrecision(10, 4);

            entity.Property(x => x.CAGR5Year)
                .HasPrecision(10, 4);

            entity.Property(x => x.CAGR10Year)
                .HasPrecision(10, 4);

            entity.Property(x => x.AnalyticsDate)
                .HasColumnType("timestamp with time zone");

            entity.Property(x => x.DataAsOfDate)
                .HasColumnType("date");

            entity.HasIndex(x => x.CAGR3Year);
        });

        modelBuilder.Entity<FundAnalyticsFailure>(entity =>
        {
            entity.HasKey(x => x.SchemeCode);

            entity.Property(x => x.FailureReason)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(x => x.IsPermanent)
                .IsRequired();

            entity.Property(x => x.LastAttemptedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();
        });
    }
}