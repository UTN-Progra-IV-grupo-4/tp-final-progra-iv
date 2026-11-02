using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Availability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UseSnakeCaseNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReservationRequests_Seats_SeatId",
                table: "ReservationRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Seats_Events_EventId",
                table: "Seats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Seats",
                table: "Seats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Events",
                table: "Events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReservationRequests",
                table: "ReservationRequests");

            migrationBuilder.RenameTable(
                name: "Seats",
                newName: "seats");

            migrationBuilder.RenameTable(
                name: "Events",
                newName: "events");

            migrationBuilder.RenameTable(
                name: "ReservationRequests",
                newName: "reservation_requests");

            migrationBuilder.RenameColumn(
                name: "Number",
                table: "seats",
                newName: "number");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "seats",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "EventId",
                table: "seats",
                newName: "event_id");

            migrationBuilder.RenameIndex(
                name: "IX_Seats_EventId_Number",
                table: "seats",
                newName: "IX_seats_event_id_number");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "events",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "events",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "StartsAt",
                table: "events",
                newName: "starts_at");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "reservation_requests",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "reservation_requests",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "SeatId",
                table: "reservation_requests",
                newName: "seat_id");

            migrationBuilder.RenameColumn(
                name: "RequestedAt",
                table: "reservation_requests",
                newName: "requested_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReservationRequests_SeatId",
                table: "reservation_requests",
                newName: "IX_reservation_requests_seat_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_seats",
                table: "seats",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_events",
                table: "events",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_reservation_requests",
                table: "reservation_requests",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_requests_seats_seat_id",
                table: "reservation_requests",
                column: "seat_id",
                principalTable: "seats",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_seats_events_event_id",
                table: "seats",
                column: "event_id",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservation_requests_seats_seat_id",
                table: "reservation_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_seats_events_event_id",
                table: "seats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_seats",
                table: "seats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_events",
                table: "events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_reservation_requests",
                table: "reservation_requests");

            migrationBuilder.RenameTable(
                name: "seats",
                newName: "Seats");

            migrationBuilder.RenameTable(
                name: "events",
                newName: "Events");

            migrationBuilder.RenameTable(
                name: "reservation_requests",
                newName: "ReservationRequests");

            migrationBuilder.RenameColumn(
                name: "number",
                table: "Seats",
                newName: "Number");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Seats",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "event_id",
                table: "Seats",
                newName: "EventId");

            migrationBuilder.RenameIndex(
                name: "IX_seats_event_id_number",
                table: "Seats",
                newName: "IX_Seats_EventId_Number");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Events",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Events",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "starts_at",
                table: "Events",
                newName: "StartsAt");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReservationRequests",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "ReservationRequests",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "seat_id",
                table: "ReservationRequests",
                newName: "SeatId");

            migrationBuilder.RenameColumn(
                name: "requested_at",
                table: "ReservationRequests",
                newName: "RequestedAt");

            migrationBuilder.RenameIndex(
                name: "IX_reservation_requests_seat_id",
                table: "ReservationRequests",
                newName: "IX_ReservationRequests_SeatId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Seats",
                table: "Seats",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Events",
                table: "Events",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReservationRequests",
                table: "ReservationRequests",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ReservationRequests_Seats_SeatId",
                table: "ReservationRequests",
                column: "SeatId",
                principalTable: "Seats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Seats_Events_EventId",
                table: "Seats",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
