using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Text.Json;

namespace Bingo_Back.Controllers
{
    [ApiController]
    [Route("api/game")]
    public class GameController : ControllerBase
    {
        private readonly IConfiguration _config;

        public GameController(IConfiguration config)
        {
            _config = config;
        }

        private NpgsqlConnection GetConnection()
            => new(_config.GetConnectionString("DefaultConnection"));

        // CHECK WINNER (5 completed lines)
        [HttpPost("check-winner")]
        public async Task<IActionResult> CheckWinner(Guid roomId)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            // 1. Get drawn numbers
            var drawn = new HashSet<int>();
            var drawSql = "SELECT number FROM drawn_numbers WHERE game_room_id=@roomId";
            await using (var drawCmd = new NpgsqlCommand(drawSql, conn))
            {
                drawCmd.Parameters.AddWithValue("@roomId", roomId);
                await using var r = await drawCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    drawn.Add(r.GetInt32(0));
            }

            // 2. Get all tickets in room
            var ticketSql = @"SELECT player_id, ticket_data
                              FROM tickets
                              WHERE game_room_id=@roomId";

            await using var ticketCmd = new NpgsqlCommand(ticketSql, conn);
            ticketCmd.Parameters.AddWithValue("@roomId", roomId);

            await using var reader = await ticketCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var playerId = reader.GetGuid(0);
                var ticket = JsonSerializer.Deserialize<int[][]>(reader.GetString(1));

                if (CountCompletedLines(ticket, drawn) >= 5)
                {
                    return Ok(new
                    {
                        winnerPlayerId = playerId,
                        message = "Winner found"
                    });
                }
            }

            return Ok("No winner yet");
        }

        private int CountCompletedLines(int[][] t, HashSet<int> d)
        {
            int lines = 0;

            // Rows
            for (int i = 0; i < 5; i++)
                if (t[i].All(n => d.Contains(n))) lines++;

            // Columns
            for (int c = 0; c < 5; c++)
                if (Enumerable.Range(0, 5).All(r => d.Contains(t[r][c])))
                    lines++;


            return lines;
        }
    }
}
