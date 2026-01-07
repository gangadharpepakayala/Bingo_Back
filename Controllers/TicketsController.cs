using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Text.Json;
using Bingo_Back.Models;

namespace Bingo_Back.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    public class TicketsController : ControllerBase
    {
        private readonly IConfiguration _config;
        public TicketsController(IConfiguration config)
        {
            _config = config;
        }

        private NpgsqlConnection GetConnection()
            => new(_config.GetConnectionString("DefaultConnection"));

        // GENERATE TICKET (1–25 shuffled)
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateTicket([FromBody] GenerateTicketRequest request)
        {
            var numbers = Enumerable.Range(1, 25).ToArray();
            var rnd = new Random();

            for (int i = numbers.Length - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (numbers[i], numbers[j]) = (numbers[j], numbers[i]);
            }

            var ticket = new int[][]
            {
                numbers[0..5],
                numbers[5..10],
                numbers[10..15],
                numbers[15..20],
                numbers[20..25]
            };

            var ticketData = JsonSerializer.Serialize(ticket);

            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"INSERT INTO tickets
                        (ticket_id, player_id, game_room_id, ticket_data)
                        VALUES (gen_random_uuid(), @playerId, @roomId, @ticketData)";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@playerId", request.PlayerId);
            cmd.Parameters.AddWithValue("@roomId", request.RoomId);
            cmd.Parameters.Add(new NpgsqlParameter("@ticketData", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = ticketData });

            await cmd.ExecuteNonQueryAsync();

            return Ok(new { message = "Ticket generated", ticket });
        }

        // GET PLAYER TICKET
        [HttpGet("{playerId}")]
        public async Task<IActionResult> GetTicket(Guid playerId)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"SELECT ticket_data::text FROM tickets
                        WHERE player_id=@playerId
                        ORDER BY created_at DESC LIMIT 1";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@playerId", playerId);

            var ticketJson = await cmd.ExecuteScalarAsync();
            
            if (ticketJson == null || ticketJson == DBNull.Value)
            {
                return NotFound("Ticket not found");
            }

            return Ok(ticketJson.ToString());
        }
    }
}
