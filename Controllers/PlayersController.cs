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
    [HttpPost]
    public async Task<IActionResult> JoinRoom(Guid userId, Guid roomId)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"INSERT INTO players (player_id, game_room_id, user_id)
                    VALUES (gen_random_uuid(), @roomId, @userId)";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@userId", userId);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Player joined room");
    }

    // LEAVE ROOM
    [HttpDelete]
    public async Task<IActionResult> LeaveRoom(Guid userId, Guid roomId)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"DELETE FROM players
                    WHERE user_id=@userId AND game_room_id=@roomId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roomId", roomId);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Player removed");
    }
}
