using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoBilling.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministratorLoginLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveLockouts",
                table: "Administrators",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Administrators",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFailedLoginAt",
                table: "Administrators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "Administrators",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveLockouts",
                table: "Administrators");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Administrators");

            migrationBuilder.DropColumn(
                name: "LastFailedLoginAt",
                table: "Administrators");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Administrators");
        }
    }
}
