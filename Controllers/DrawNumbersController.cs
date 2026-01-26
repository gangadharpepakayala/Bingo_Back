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

            // 0. Ensure schema exists (Auto-Migration)
            try {
                await using var alterCmd = new NpgsqlCommand("ALTER TABLE drawn_numbers ADD COLUMN IF NOT EXISTS player_id uuid;", conn);
                await alterCmd.ExecuteNonQueryAsync();
            } catch (Exception ex) {
                Console.WriteLine($"Migration warning: {ex.Message}");
            }

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
                (draw_id, game_room_id, number, player_id)
                VALUES (gen_random_uuid(), @roomId, @number, @playerId)";

            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("@roomId", request.RoomId);
            insertCmd.Parameters.AddWithValue("@number", number);
            insertCmd.Parameters.AddWithValue("@playerId", request.PlayerId); // Add PlayerId
            await insertCmd.ExecuteNonQueryAsync();

            // 4. Response
            return Ok(new
            {
                lastNumber = number,
                playerId = request.PlayerId
            });
        }

        // ===============================
        // GET DRAWN NUMBERS
        // ===============================
        [HttpGet("{roomId}")]
        public async Task<IActionResult> GetDrawnNumbers(Guid roomId)
        {
            var numbers = new List<DrawnNumber>();

            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT number, player_id
                FROM drawn_numbers
                WHERE game_room_id = @roomId
                ORDER BY draw_time
            ";

            try {
                // Ensure column exists for GET as well in case it wasn't created
                var checkCmd = new NpgsqlCommand("ALTER TABLE drawn_numbers ADD COLUMN IF NOT EXISTS player_id uuid;", conn);
                await checkCmd.ExecuteNonQueryAsync();
            } catch {}

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@roomId", roomId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                numbers.Add(new DrawnNumber {
                    Number = reader.GetInt32(0),
                    PlayerId = reader.IsDBNull(1) ? null : reader.GetGuid(1)
                });
            }

            return Ok(new { drawnNumbers = numbers });
        }
    }
}
