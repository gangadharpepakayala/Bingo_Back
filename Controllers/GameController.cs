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

            // 3. Check for winner(s)
            Console.WriteLine($"[CheckWinner] Checking {tickets.Count} tickets against {drawn.Count} drawn numbers.");
            
            var winners = new List<Guid>();

            foreach (var (playerId, ticket) in tickets)
            {
                int lines = CountCompletedLines(ticket, drawn);
                Console.WriteLine($"[CheckWinner] Player {playerId} has {lines} completed lines.");

                if (lines >= 5)
                {
                    winners.Add(playerId);
                }
            }

            if (winners.Count > 0)
            {
                // If more than one winner, it's a DRAW
                if (winners.Count > 1)
                {
                    var updateSql = @"UPDATE game_rooms 
                                      SET status='completed', current_turn_player_id=NULL 
                                      WHERE game_room_id=@roomId";
                    
                    await using var updateCmd = new NpgsqlCommand(updateSql, conn);
                    updateCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    await updateCmd.ExecuteNonQueryAsync();

                    return Ok(new
                    {
                        winner = true,
                        isDraw = true,
                        winnerName = "Draw"
                    });
                }
                else
                {
                    // Single Winner
                    var winnerId = winners[0];
                    var updateSql = @"UPDATE game_rooms 
                                      SET status='completed', current_turn_player_id=@winnerId 
                                      WHERE game_room_id=@roomId";
                    
                    await using var updateCmd = new NpgsqlCommand(updateSql, conn);
                    updateCmd.Parameters.AddWithValue("@winnerId", winnerId);
                    updateCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    await updateCmd.ExecuteNonQueryAsync();

                    return Ok(new
                    {
                        winner = true,
                        playerId = winnerId,
                        isDraw = false
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

            var checkSql = "SELECT status, current_turn_player_id FROM game_rooms WHERE game_room_id = @roomId";
            string status = "";
            Guid? winnerId = null;

            await using (var cmd = new NpgsqlCommand(checkSql, conn))
            {
                cmd.Parameters.AddWithValue("@roomId", roomId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    status = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    if (!reader.IsDBNull(1)) winnerId = reader.GetGuid(1);
                }
            }

            if (status == "completed")
            {
                if (winnerId == null)
                {
                    // DRAW
                    return Ok(new
                    {
                        winner = true,
                        isDraw = true,
                        playerId = Guid.Empty,
                        winnerName = "Draw"
                    });
                }
                else
                {
                    // Single Winner - Get Name
                    var sql = @"
                        SELECT u.username 
                        FROM players p 
                        JOIN users u ON p.user_id = u.user_id
                        WHERE p.player_id = @pid";
                    
                    var username = "";
                    await using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@pid", winnerId);
                        var res = await cmd.ExecuteScalarAsync();
                        if (res != null) username = res.ToString();
                    }

                    return Ok(new
                    {
                        winner = true,
                        playerId = winnerId,
                        winnerName = username,
                        isDraw = false
                    });
                }
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
