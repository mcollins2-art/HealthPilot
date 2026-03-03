using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthPilot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilityNaturalUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_facilities_Name_City_State_Zip",
                table: "facilities",
                columns: new[] { "Name", "City", "State", "Zip" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_facilities_Name_City_State_Zip",
                table: "facilities");
        }
    }
}
