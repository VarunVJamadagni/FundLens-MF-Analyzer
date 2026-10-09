using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InvestmentPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFundAnalyticsFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundAnalyticsFailures",
                columns: table => new
                {
                    SchemeCode = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsPermanent = table.Column<bool>(type: "boolean", nullable: false),
                    LastAttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundAnalyticsFailures", x => x.SchemeCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundAnalyticsFailures");
        }
    }
}
