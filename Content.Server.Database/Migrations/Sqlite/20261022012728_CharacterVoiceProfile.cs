using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class CharacterVoiceProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "voice_breathiness",
                table: "profile",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "voice_brightness",
                table: "profile",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "voice_formant",
                table: "profile",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "voice_pitch",
                table: "profile",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "voice_roughness",
                table: "profile",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "voice_breathiness",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "voice_brightness",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "voice_formant",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "voice_pitch",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "voice_roughness",
                table: "profile");
        }
    }
}
