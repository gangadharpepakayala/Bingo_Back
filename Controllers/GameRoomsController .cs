using Bingo_Back.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

[ApiController]
[Route("api/rooms")]
public class GameRoomsController : ControllerBase
{
    private readonly IConfiguration _config;

    public GameRoomsController(IConfiguration config)
    {
        _config = config;
    }

    private NpgsqlConnection GetConnection()
        => new(_config.GetConnectionString("DefaultConnection"));

    // GET ALL ROOMS
    [HttpGet]
    public async Task<IActionResult> GetAllRooms()
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        // Delete expired rooms first
        var deleteSql = "DELETE FROM game_rooms WHERE expires_at < NOW()";
        await using var deleteCmd = new NpgsqlCommand(deleteSql, conn);
        await deleteCmd.ExecuteNonQueryAsync();

        var sql = @"SELECT gr.game_room_id, gr.room_name, gr.status, gr.created_at, gr.created_by_user_id, gr.expires_at,
                    COUNT(p.player_id) as player_count
                    FROM game_rooms gr
                    LEFT JOIN players p ON gr.game_room_id = p.game_room_id
                    WHERE gr.expires_at > NOW()
                    GROUP BY gr.game_room_id, gr.room_name, gr.status, gr.created_at, gr.created_by_user_id, gr.expires_at
                    ORDER BY gr.created_at DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rooms = new List<object>();
        while (await reader.ReadAsync())
        {
            rooms.Add(new
            {
                gameRoomId = reader.GetGuid(0),
                roomName = reader.GetString(1),
                status = reader.GetString(2),
                createdAt = reader.GetDateTime(3),
                createdByUserId = reader.GetGuid(4),
                expiresAt = reader.GetDateTime(5),
                playerCount = reader.GetInt32(6)
            });
        }

        return Ok(rooms);
    }

    // GET ROOM BY ID
    [HttpGet("{roomId}")]
    public async Task<IActionResult> GetRoom(Guid roomId)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"SELECT gr.game_room_id, gr.room_name, gr.status, gr.created_at, gr.current_turn_player_id,
                    COUNT(p.player_id) as player_count
                    FROM game_rooms gr
                    LEFT JOIN players p ON gr.game_room_id = p.game_room_id
                    WHERE gr.game_room_id = @roomId
                    GROUP BY gr.game_room_id, gr.room_name, gr.status, gr.created_at, gr.current_turn_player_id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@roomId", roomId);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return Ok(new
            {
                gameRoomId = reader.GetGuid(0),
                roomName = reader.GetString(1),
                status = reader.GetString(2),
                createdAt = reader.GetDateTime(3),
                currentTurnPlayerId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                playerCount = reader.GetInt32(5)
            });
        }

        return NotFound("Room not found");
    }

    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var roomId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddDays(1); // 1 day expiry

        var sql = @"INSERT INTO game_rooms (game_room_id, room_name, status, created_by_user_id, created_at, expires_at)
                VALUES (@roomId, @roomName, 'pending', @userId, NOW(), @expiresAt)";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@roomName", request.RoomName);
        cmd.Parameters.AddWithValue("@userId", request.UserId);
        cmd.Parameters.AddWithValue("@expiresAt", expiresAt);

        await cmd.ExecuteNonQueryAsync();
        return Ok(new { 
            id = roomId, 
            roomName = request.RoomName, 
            status = "pending", 
            playerCount = 0,
            createdByUserId = request.UserId,
            expiresAt = expiresAt
        });

    }

    [HttpPut("{roomId}")]
    public async Task<IActionResult> UpdateRoomStatus(
    Guid roomId,
    [FromBody] string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return BadRequest("Status is required");

        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"UPDATE game_rooms 
                SET status=@status 
                WHERE game_room_id=@roomId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@roomId", roomId);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Room updated");
    }



    // DELETE ROOM (only by creator)
    [HttpDelete("{roomId}")]
    public async Task<IActionResult> DeleteRoom(Guid roomId, [FromQuery] Guid userId)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        // Check if user is the creator
        var checkSql = "SELECT created_by_user_id FROM game_rooms WHERE game_room_id = @roomId";
        await using var checkCmd = new NpgsqlCommand(checkSql, conn);
        checkCmd.Parameters.AddWithValue("@roomId", roomId);
        var result = await checkCmd.ExecuteScalarAsync();

        if (result == null)
        {
            return NotFound("Room not found");
        }

        var creatorId = (Guid)result;
        if (creatorId != userId)
        {
            return Forbid("Only the room creator can delete this room");
        }

        await using var transaction = await conn.BeginTransactionAsync();
        try
        {
            // 1. Delete drawn numbers
            using (var cmd1 = new NpgsqlCommand("DELETE FROM drawn_numbers WHERE game_room_id=@roomId", conn, transaction))
            {
                cmd1.Parameters.AddWithValue("@roomId", roomId);
                await cmd1.ExecuteNonQueryAsync();
            }

            // 2. Delete tickets
            using (var cmd2 = new NpgsqlCommand("DELETE FROM tickets WHERE game_room_id=@roomId", conn, transaction))
            {
                cmd2.Parameters.AddWithValue("@roomId", roomId);
                await cmd2.ExecuteNonQueryAsync();
            }

            // 3. Delete players
            using (var cmd3 = new NpgsqlCommand("DELETE FROM players WHERE game_room_id=@roomId", conn, transaction))
            {
                cmd3.Parameters.AddWithValue("@roomId", roomId);
                await cmd3.ExecuteNonQueryAsync();
            }

            // 4. Delete room
            using (var cmd4 = new NpgsqlCommand("DELETE FROM game_rooms WHERE game_room_id=@roomId", conn, transaction))
            {
                cmd4.Parameters.AddWithValue("@roomId", roomId);
                await cmd4.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Ok(new { message = "Room deleted" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, $"Error deleting room: {ex.Message}");
        }
    }

    // GET USER'S ACTIVE ROOMS
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserRooms(Guid userId)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"SELECT gr.game_room_id, gr.room_name, gr.status, gr.created_at, gr.expires_at,
                    COUNT(p.player_id) as player_count
                    FROM game_rooms gr
                    LEFT JOIN players p ON gr.game_room_id = p.game_room_id
                    WHERE gr.created_by_user_id = @userId AND gr.expires_at > NOW()
                    GROUP BY gr.game_room_id, gr.room_name, gr.status, gr.created_at, gr.expires_at
                    ORDER BY gr.created_at DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rooms = new List<object>();
        while (await reader.ReadAsync())
        {
            rooms.Add(new
            {
                gameRoomId = reader.GetGuid(0),
                roomName = reader.GetString(1),
                status = reader.GetString(2),
                createdAt = reader.GetDateTime(3),
                expiresAt = reader.GetDateTime(4),
                playerCount = reader.GetInt32(5)
            });
        }

        return Ok(rooms);
    }

    // UPDATE CURRENT TURN
    [HttpPut("{roomId}/turn")]
    public async Task<IActionResult> UpdateTurn(Guid roomId, [FromBody] string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return BadRequest("Player ID is required");

        Guid playerGuid;
        if (!Guid.TryParse(playerId.Trim('"'), out playerGuid))
            return BadRequest("Invalid player ID format");

        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"UPDATE game_rooms 
                    SET current_turn_player_id = @playerId 
                    WHERE game_room_id = @roomId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@playerId", playerGuid);
        cmd.Parameters.AddWithValue("@roomId", roomId);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Turn updated");
    }
    /* FixSchema removed */
}
