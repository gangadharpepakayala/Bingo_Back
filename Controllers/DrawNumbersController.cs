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
        public IActionResult DrawNumber(Guid roomId, int number)
        {
            using var conn = GetConnection();
            conn.Open();

            var sql = @"
                INSERT INTO drawn_numbers (draw_id, game_room_id, number)
                VALUES (gen_random_uuid(), @roomId, @number);
            ";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@number", number);
            cmd.ExecuteNonQuery();

            return Ok("Number drawn successfully");
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
