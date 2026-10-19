using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourierService.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientDefaultZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultZoneId",
                table: "Clients",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_DefaultZoneId",
                table: "Clients",
                column: "DefaultZoneId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clients_Zones_DefaultZoneId",
                table: "Clients",
                column: "DefaultZoneId",
                principalTable: "Zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Демо-клієнтам у вже створених базах — район за їхньою адресою з демо-набору (D-41).
            // Інші клієнти отримають район, коли оператор відредагує картку
            (string Phone, string Zone)[] demoClients =
            [
                ("+380670000201", "Печерський"),
                ("+380670000202", "Подільський"),
                ("+380670000203", "Голосіївський"),
                ("+380670000206", "Оболонський"),
                ("+380930000204", "Дніпровський"),
                ("+380930000205", "Святошинський"),
            ];
            foreach (var (phone, zone) in demoClients)
            {
                migrationBuilder.Sql(
                    $"UPDATE Clients SET DefaultZoneId = (SELECT Id FROM Zones WHERE Name = '{zone}') " +
                    $"WHERE Phone = '{phone}' AND DefaultZoneId IS NULL;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clients_Zones_DefaultZoneId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_DefaultZoneId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "DefaultZoneId",
                table: "Clients");
        }
    }
}
