using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Bingo_Back.Models;

namespace Bingo_Back.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IConfiguration _config;

        public UsersController(IConfiguration config)
        {
            _config = config;
        }

        private NpgsqlConnection GetConnection()
            => new(_config.GetConnectionString("DefaultConnection"));

        // CREATE USER
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] string username)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"INSERT INTO users (user_id, username)
                        VALUES (gen_random_uuid(), @username)";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@username", username);

            await cmd.ExecuteNonQueryAsync();
            return Ok(new { message = "User created" });
        }

        // GET USERS
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = new List<User>();

            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = "SELECT user_id, username, created_at FROM users";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add(new User
                {
                    UserId = reader.GetGuid(0),
                    Username = reader.GetString(1),
                    CreatedAt = reader.GetDateTime(2)
                });
            }

            return Ok(users);
        }

        // DELETE USER
        [HttpDelete("{userId}")]
        public async Task<IActionResult> DeleteUser(Guid userId)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = "DELETE FROM users WHERE user_id = @userId";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                return NotFound("User not found");

            return Ok("User deleted");
        }
    }
}
