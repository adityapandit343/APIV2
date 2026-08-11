using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatbotApi.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OAuthState",
                table: "Tenants",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppConnectedAt",
                table: "Tenants",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OAuthState",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "WhatsAppConnectedAt",
                table: "Tenants");
        }
    }
}
