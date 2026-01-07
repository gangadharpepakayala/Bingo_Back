using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Text.Json;
using Bingo_Back.Models;

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
        public async Task<IActionResult> CheckWinner(
            [FromBody] DrawRequest request)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            // 1. Get drawn numbers
            var drawn = new HashSet<int>();
            var drawSql = "SELECT number FROM drawn_numbers WHERE game_room_id=@roomId";

            await using (var cmd = new NpgsqlCommand(drawSql, conn))
            {
                cmd.Parameters.AddWithValue("@roomId", request.RoomId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    drawn.Add(r.GetInt32(0));
            }

            // 2. Check each ticket
            // 2. Load all tickets first (to close reader before update)
            var tickets = new List<(Guid PlayerId, int[][] Ticket)>();

            var ticketSql = @"SELECT player_id, ticket_data
                              FROM tickets
                              WHERE game_room_id=@roomId";

            await using (var ticketCmd = new NpgsqlCommand(ticketSql, conn))
            {
                ticketCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                await using var reader = await ticketCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var pid = reader.GetGuid(0);
                    var tData = JsonSerializer.Deserialize<int[][]>(reader.GetString(1));
                    tickets.Add((pid, tData));
                }
            }

            // 3. Check for winner
            Console.WriteLine($"[CheckWinner] Checking {tickets.Count} tickets against {drawn.Count} drawn numbers.");
            
            foreach (var (playerId, ticket) in tickets)
            {
                int lines = CountCompletedLines(ticket, drawn);
                Console.WriteLine($"[CheckWinner] Player {playerId} has {lines} completed lines.");

                if (lines >= 5)
                {
                    // Update room status
                    // REUSE current_turn_player_id to store the winner ID when status is 'completed'
                    var updateSql = @"UPDATE game_rooms 
                                      SET status='completed', current_turn_player_id=@winnerId 
                                      WHERE game_room_id=@roomId";
                    
                    await using var updateCmd = new NpgsqlCommand(updateSql, conn);
                    updateCmd.Parameters.AddWithValue("@winnerId", playerId);
                    updateCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    await updateCmd.ExecuteNonQueryAsync();

                    return Ok(new
                    {
                        winner = true,
                        playerId = playerId
                    });
                }
            }

            // 3. No winner
            return Ok(new
            {
                winner = false
            });
        }

        [HttpGet("winner/{roomId}")]
        public async Task<IActionResult> GetWinner(Guid roomId)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT p.player_id, u.username 
                FROM game_rooms g
                JOIN players p ON g.current_turn_player_id = p.player_id
                JOIN users u ON p.user_id = u.user_id
                WHERE g.game_room_id = @roomId AND g.status = 'completed'";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@roomId", roomId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return Ok(new
                {
                    winner = true,
                    playerId = reader.GetGuid(0),
                    winnerName = reader.GetString(1)
                });
            }

            return Ok(new { winner = false });
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
