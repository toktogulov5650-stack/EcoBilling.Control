using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoBilling.Control.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorCreationUniquePerDistrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningOperations_DistrictId_DirectorCreationOnly",
                table: "ProvisioningOperations",
                column: "DistrictId",
                unique: true,
                filter: "\"OperationType\" = 'DirectorCreation'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProvisioningOperations_DistrictId_DirectorCreationOnly",
                table: "ProvisioningOperations");
        }
    }
}
