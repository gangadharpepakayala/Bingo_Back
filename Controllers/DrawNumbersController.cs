using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Bingo_Back.Models;

namespace Bingo_Back.Controllers
{
    [ApiController]
    [Route("api/draw")]
    public class DrawNumbersController : ControllerBase
    {
        private readonly IConfiguration _config;

        public DrawNumbersController(IConfiguration config)
        {
            _config = config;
        }

        private NpgsqlConnection GetConnection()
            => new(_config.GetConnectionString("DefaultConnection"));

        // ===============================
        // DRAW NUMBER (POST)
        // ===============================
        [HttpPost]
        public async Task<IActionResult> DrawNumber(
            [FromBody] DrawRequest request)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            // 1. Get already drawn numbers
            var drawn = new HashSet<int>();
            var getSql = "SELECT number FROM drawn_numbers WHERE game_room_id=@roomId";

            await using (var cmd = new NpgsqlCommand(getSql, conn))
            {
                cmd.Parameters.AddWithValue("@roomId", request.RoomId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    drawn.Add(reader.GetInt32(0));
            }

            if (drawn.Count == 25)
                return BadRequest("All numbers drawn");

            // Check if game is already completed
            var statusSql = "SELECT status FROM game_rooms WHERE game_room_id=@roomId";
            await using (var statusCmd = new NpgsqlCommand(statusSql, conn))
            {
                statusCmd.Parameters.AddWithValue("@roomId", request.RoomId);
                var status = await statusCmd.ExecuteScalarAsync();
                if (status != null && status.ToString() == "completed")
                {
                    return BadRequest("Game completed");
                }
            }

            // 2. Use the requested number
            int number = request.Number;

            // Validate number
            if (number < 1 || number > 25)
                return BadRequest("Invalid number. Must be 1-25.");
            
            if (drawn.Contains(number))
                return BadRequest("Number already called.");

            // 3. Insert drawn number
            var insertSql = @"INSERT INTO drawn_numbers
                (draw_id, game_room_id, number)
                VALUES (gen_random_uuid(), @roomId, @number)";

            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("@roomId", request.RoomId);
            insertCmd.Parameters.AddWithValue("@number", number);
            await insertCmd.ExecuteNonQueryAsync();

            // 4. Response
            return Ok(new
            {
                lastNumber = number
            });
        }

        // ===============================
        // GET DRAWN NUMBERS
        // ===============================
        [HttpGet("{roomId}")]
        public async Task<IActionResult> GetDrawnNumbers(Guid roomId)
        {
            var numbers = new List<int>();

            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT number
                FROM drawn_numbers
                WHERE game_room_id = @roomId
                ORDER BY draw_time
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@roomId", roomId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                numbers.Add(reader.GetInt32(0));
            }

            return Ok(new { drawnNumbers = numbers });
        }
    }
}
