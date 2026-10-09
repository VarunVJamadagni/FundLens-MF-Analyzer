using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InvestmentPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundAnalytics",
                columns: table => new
                {
                    SchemeCode = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CurrentNAV = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Return1Month = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    Return3Month = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    Return6Month = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    CAGR1Year = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    CAGR3Year = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    CAGR5Year = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    CAGR10Year = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    AnalyticsDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataAsOfDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundAnalytics", x => x.SchemeCode);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundAnalytics_CAGR3Year",
                table: "FundAnalytics",
                column: "CAGR3Year");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundAnalytics");
        }
    }
}
