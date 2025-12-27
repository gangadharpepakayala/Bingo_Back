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
        {
            return new NpgsqlConnection(
                _config.GetConnectionString("DefaultConnection"));
        }

        // DRAW NUMBER
        [HttpPost]
        public async Task<IActionResult> DrawNumber(Guid roomId)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            // Get already drawn numbers
            var drawn = new HashSet<int>();
            var getSql = "SELECT number FROM drawn_numbers WHERE game_room_id=@roomId";
            await using (var getCmd = new NpgsqlCommand(getSql, conn))
            {
                getCmd.Parameters.AddWithValue("@roomId", roomId);
                await using var reader = await getCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    drawn.Add(reader.GetInt32(0));
            }

            if (drawn.Count == 25)
                return BadRequest("All numbers drawn");

            var remaining = Enumerable.Range(1, 25).Where(n => !drawn.Contains(n)).ToList();
            var rnd = new Random();
            int number = remaining[rnd.Next(remaining.Count)];

            var insertSql = @"INSERT INTO drawn_numbers
                      (draw_id, game_room_id, number)
                      VALUES (gen_random_uuid(), @roomId, @number)";

            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("@roomId", roomId);
            insertCmd.Parameters.AddWithValue("@number", number);
            await insertCmd.ExecuteNonQueryAsync();

            return Ok(new { number });
        }


        // GET DRAWN NUMBERS
        [HttpGet("{roomId}")]
        public IActionResult GetDrawnNumbers(Guid roomId)
        {
            var numbers = new List<DrawnNumber>();

            using var conn = GetConnection();
            conn.Open();

            var sql = @"
                SELECT draw_id, game_room_id, number, draw_time
                FROM drawn_numbers
                WHERE game_room_id = @roomId
                ORDER BY draw_time
            ";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@roomId", roomId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                numbers.Add(new DrawnNumber
                {
                    DrawId = reader.GetGuid(0),
                    GameRoomId = reader.GetGuid(1),
                    Number = reader.GetInt32(2),
                    DrawTime = reader.GetDateTime(3)
                });
            }

            return Ok(numbers);
        }
    }
}
