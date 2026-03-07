using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace revix.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class updatedusertoken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AccessToken",
                table: "Users",
                newName: "EncryptedAccessToken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EncryptedAccessToken",
                table: "Users",
                newName: "AccessToken");
        }
    }
}
