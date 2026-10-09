using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFundMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "FundAnalytics",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FundHouse",
                table: "FundAnalytics",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastNavDate",
                table: "FundAnalytics",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Option",
                table: "FundAnalytics",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "FundAnalytics",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SchemeName",
                table: "FundAnalytics",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubCategory",
                table: "FundAnalytics",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "FundAnalytics");

            migrationBuilder.DropColumn(
                name: "FundHouse",
                table: "FundAnalytics");

            migrationBuilder.DropColumn(
                name: "LastNavDate",
                table: "FundAnalytics");

            migrationBuilder.DropColumn(
                name: "Option",
                table: "FundAnalytics");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "FundAnalytics");

            migrationBuilder.DropColumn(
                name: "SchemeName",
                table: "FundAnalytics");

            migrationBuilder.DropColumn(
                name: "SubCategory",
                table: "FundAnalytics");
        }
    }
}
