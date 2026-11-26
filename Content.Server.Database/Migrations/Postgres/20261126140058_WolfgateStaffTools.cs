using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateStaffTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_ahelp_ticket",
                columns: table => new
                {
                    wolfgate_ahelp_ticket_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    round_id = table.Column<int>(type: "integer", nullable: false),
                    server_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    character_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_reply_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    macros = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_ahelp_ticket", x => x.wolfgate_ahelp_ticket_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_player_position",
                columns: table => new
                {
                    wolfgate_player_position_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    round_id = table.Column<int>(type: "integer", nullable: false),
                    sampled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    entity_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    map_id = table.Column<int>(type: "integer", nullable: false),
                    grid_id = table.Column<int>(type: "integer", nullable: true),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    alive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_player_position", x => x.wolfgate_player_position_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_ahelp_message",
                columns: table => new
                {
                    wolfgate_ahelp_message_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ticket_id = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    admin_only = table.Column<bool>(type: "boolean", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_ahelp_message", x => x.wolfgate_ahelp_message_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_ahelp_message_wolfgate_ahelp_ticket_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "wolfgate_ahelp_ticket",
                        principalColumn: "wolfgate_ahelp_ticket_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_ahelp_message_ticket_id_sent_at",
                table: "wolfgate_ahelp_message",
                columns: new[] { "ticket_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_ahelp_ticket_player_user_id_opened_at",
                table: "wolfgate_ahelp_ticket",
                columns: new[] { "player_user_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_ahelp_ticket_round_id",
                table: "wolfgate_ahelp_ticket",
                column: "round_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_player_position_round_id_sampled_at",
                table: "wolfgate_player_position",
                columns: new[] { "round_id", "sampled_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_ahelp_message");

            migrationBuilder.DropTable(
                name: "wolfgate_player_position");

            migrationBuilder.DropTable(
                name: "wolfgate_ahelp_ticket");
        }
    }
}
