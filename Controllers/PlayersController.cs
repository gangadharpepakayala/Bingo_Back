using Bingo_Back.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

[ApiController]
[Route("api/players")]
public class PlayersController : ControllerBase
{
    private readonly IConfiguration _config;

    public PlayersController(IConfiguration config)
    {
        _config = config;
    }

    private NpgsqlConnection GetConnection()
        => new(_config.GetConnectionString("DefaultConnection"));

    // JOIN ROOM
    [HttpPost("join")]
    public async Task<IActionResult> JoinRoom([FromBody] JoinRoomRequest request)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        // Check current player count
        var countSql = @"SELECT COUNT(*) FROM players WHERE game_room_id = @roomId";
        await using var countCmd = new NpgsqlCommand(countSql, conn);
        countCmd.Parameters.AddWithValue("@roomId", request.RoomId);
        var currentCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // Don't allow more than 2 players
        if (currentCount >= 2)
        {
            return BadRequest(new { message = "Room is full. Maximum 2 players allowed." });
        }

        var playerId = Guid.NewGuid();
        var sql = @"INSERT INTO players (player_id, game_room_id, user_id)
                VALUES (@playerId, @roomId, @userId)";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@playerId", playerId);
        cmd.Parameters.AddWithValue("@roomId", request.RoomId);
        cmd.Parameters.AddWithValue("@userId", request.UserId);

        await cmd.ExecuteNonQueryAsync();

        // If this is the 2nd player, auto-start the game
        if (currentCount == 1)
        {
            var updateSql = @"UPDATE game_rooms SET status = 'active' WHERE game_room_id = @roomId";
            await using var updateCmd = new NpgsqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("@roomId", request.RoomId);
            await updateCmd.ExecuteNonQueryAsync();
        }

        return Ok(new { playerId = playerId, playerCount = currentCount + 1 });
    }

    [HttpPost("leave")]
    public async Task<IActionResult> LeaveRoom([FromBody] JoinRoomRequest request)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"DELETE FROM players
                WHERE user_id=@userId AND game_room_id=@roomId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@userId", request.UserId);
        cmd.Parameters.AddWithValue("@roomId", request.RoomId);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Player removed");
    }

    // GET PLAYERS IN ROOM
    [HttpGet("room/{roomId}")]
    public async Task<IActionResult> GetPlayersInRoom(Guid roomId)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"SELECT p.player_id, p.user_id, u.username
                    FROM players p
                    JOIN users u ON p.user_id = u.user_id
                    WHERE p.game_room_id = @roomId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@roomId", roomId);
        await using var reader = await cmd.ExecuteReaderAsync();

        var players = new List<object>();
        while (await reader.ReadAsync())
        {
            players.Add(new
            {
                playerId = reader.GetGuid(0),
                userId = reader.GetGuid(1),
                username = reader.GetString(2)
            });
        }

        return Ok(players);
    }

}
