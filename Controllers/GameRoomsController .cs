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

    // CREATE ROOM
    [HttpPost]
    public async Task<IActionResult> CreateRoom(string roomName)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"INSERT INTO game_rooms (game_room_id, room_name, status)
                    VALUES (gen_random_uuid(), @roomName, 'pending')";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@roomName", roomName);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Room created");
    }

    // UPDATE ROOM STATUS
    [HttpPut("{roomId}")]
    public async Task<IActionResult> UpdateRoomStatus(Guid roomId, string status)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = @"UPDATE game_rooms SET status=@status WHERE game_room_id=@roomId";
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@roomId", roomId);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Room updated");
    }

    // DELETE ROOM
    [HttpDelete("{roomId}")]
    public async Task<IActionResult> DeleteRoom(Guid roomId)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var sql = "DELETE FROM game_rooms WHERE game_room_id=@roomId";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@roomId", roomId);

        await cmd.ExecuteNonQueryAsync();
        return Ok("Room deleted");
    }
}
