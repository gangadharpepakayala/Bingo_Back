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
                        playerId = Guid.Empty,
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

        [HttpPost("restart")]
        public async Task<IActionResult> RestartGame([FromBody] DrawRequest request)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            // Start a transaction to ensure all operations complete together
            await using var transaction = await conn.BeginTransactionAsync();
            try
            {
                // 1. Verify room exists and get players
                var roomCheckSql = "SELECT status FROM game_rooms WHERE game_room_id = @roomId";
                string roomStatus = "";
                
                await using (var checkCmd = new NpgsqlCommand(roomCheckSql, conn, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result == null)
                    {
                        await transaction.RollbackAsync();
                        return NotFound("Room not found");
                    }
                    roomStatus = result.ToString();
                }

                // 2. Get all players in the room
                var playersSql = "SELECT player_id FROM players WHERE game_room_id = @roomId";
                var playerIds = new List<Guid>();
                
                await using (var playersCmd = new NpgsqlCommand(playersSql, conn, transaction))
                {
                    playersCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    await using var reader = await playersCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        playerIds.Add(reader.GetGuid(0));
                    }
                }

                if (playerIds.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest("No players found in room");
                }

                // 3. Delete all drawn numbers for this room
                var deleteDrawnSql = "DELETE FROM drawn_numbers WHERE game_room_id = @roomId";
                await using (var deleteDrawnCmd = new NpgsqlCommand(deleteDrawnSql, conn, transaction))
                {
                    deleteDrawnCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    await deleteDrawnCmd.ExecuteNonQueryAsync();
                }

                // 4. Delete all existing tickets for this room
                var deleteTicketsSql = "DELETE FROM tickets WHERE game_room_id = @roomId";
                await using (var deleteTicketsCmd = new NpgsqlCommand(deleteTicketsSql, conn, transaction))
                {
                    deleteTicketsCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    await deleteTicketsCmd.ExecuteNonQueryAsync();
                }

                // 5. Generate new tickets for all players
                var rnd = new Random();
                foreach (var playerId in playerIds)
                {
                    // Generate new ticket (1-25 shuffled)
                    var numbers = Enumerable.Range(1, 25).ToArray();
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

                    var insertTicketSql = @"INSERT INTO tickets 
                                          (ticket_id, player_id, game_room_id, ticket_data)
                                          VALUES (gen_random_uuid(), @playerId, @roomId, @ticketData)";

                    await using var insertTicketCmd = new NpgsqlCommand(insertTicketSql, conn, transaction);
                    insertTicketCmd.Parameters.AddWithValue("@playerId", playerId);
                    insertTicketCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    insertTicketCmd.Parameters.Add(new NpgsqlParameter("@ticketData", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = ticketData });
                    await insertTicketCmd.ExecuteNonQueryAsync();
                }

                // 6. Reset game room status to active and set first player's turn
                var updateRoomSql = @"UPDATE game_rooms 
                                    SET status = 'active', 
                                        current_turn_player_id = @firstPlayerId 
                                    WHERE game_room_id = @roomId";

                await using (var updateRoomCmd = new NpgsqlCommand(updateRoomSql, conn, transaction))
                {
                    updateRoomCmd.Parameters.AddWithValue("@firstPlayerId", playerIds[0]);
                    updateRoomCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                    await updateRoomCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                return Ok(new 
                { 
                    message = "Game restarted successfully",
                    roomId = request.RoomId,
                    playerCount = playerIds.Count
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[RestartGame] Error: {ex.Message}");
                return StatusCode(500, $"Error restarting game: {ex.Message}");
            }
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
