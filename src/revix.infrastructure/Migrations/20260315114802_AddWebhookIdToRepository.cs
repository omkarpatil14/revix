using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace revix.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookIdToRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GitHubWebhookId",
                table: "Repositories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitHubWebhookId",
                table: "Repositories");
        }
    }
}
