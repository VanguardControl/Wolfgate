using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateMentors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_mentor",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tier = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    granted_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    granted_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    last_active_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    promoted_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    history = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    staff_notes = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_mentor", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_mentor_application",
                columns: table => new
                {
                    wolfgate_mentor_application_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    playtime_minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    answers = table.Column<string>(type: "TEXT", nullable: false),
                    record_summary = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    reason = table.Column<string>(type: "TEXT", nullable: true),
                    reapply_after = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_mentor_application", x => x.wolfgate_mentor_application_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_mentor_canned",
                columns: table => new
                {
                    wolfgate_mentor_canned_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    title = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false),
                    guide_entry = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    updated_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_mentor_canned", x => x.wolfgate_mentor_canned_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_mentor_ticket",
                columns: table => new
                {
                    wolfgate_mentor_ticket_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    server_id = table.Column<int>(type: "INTEGER", nullable: true),
                    round_id = table.Column<int>(type: "INTEGER", nullable: true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    character_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    topic = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    secret_role = table.Column<bool>(type: "INTEGER", nullable: false),
                    newbie = table.Column<bool>(type: "INTEGER", nullable: false),
                    from_ahelp = table.Column<bool>(type: "INTEGER", nullable: false),
                    opened_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    first_answer_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    claimed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    closed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    close_reason = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    rating = table.Column<int>(type: "INTEGER", nullable: true),
                    rating_comment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_mentor_ticket", x => x.wolfgate_mentor_ticket_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_mentor_ticket_message",
                columns: table => new
                {
                    wolfgate_mentor_ticket_message_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ticket_id = table.Column<int>(type: "INTEGER", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    sender_role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false),
                    sent_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_mentor_ticket_message", x => x.wolfgate_mentor_ticket_message_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_mentor_ticket_message_wolfgate_mentor_ticket_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "wolfgate_mentor_ticket",
                        principalColumn: "wolfgate_mentor_ticket_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_mentor_application_user_id",
                table: "wolfgate_mentor_application",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_mentor_ticket_claimed_by",
                table: "wolfgate_mentor_ticket",
                column: "claimed_by");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_mentor_ticket_opened_at",
                table: "wolfgate_mentor_ticket",
                column: "opened_at");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_mentor_ticket_player_user_id",
                table: "wolfgate_mentor_ticket",
                column: "player_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_mentor_ticket_message_sender_user_id",
                table: "wolfgate_mentor_ticket_message",
                column: "sender_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_mentor_ticket_message_ticket_id",
                table: "wolfgate_mentor_ticket_message",
                column: "ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_mentor");

            migrationBuilder.DropTable(
                name: "wolfgate_mentor_application");

            migrationBuilder.DropTable(
                name: "wolfgate_mentor_canned");

            migrationBuilder.DropTable(
                name: "wolfgate_mentor_ticket_message");

            migrationBuilder.DropTable(
                name: "wolfgate_mentor_ticket");
        }
    }
}
