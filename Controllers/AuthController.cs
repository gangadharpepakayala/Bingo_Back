using Bingo_Back.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace Bingo_Back.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;

        public AuthController(IConfiguration config)
        {
            _config = config;
        }

        private NpgsqlConnection GetConnection()
            => new(_config.GetConnectionString("DefaultConnection"));

        // REGISTER
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || 
                string.IsNullOrWhiteSpace(request.Email) || 
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("All fields are required");
            }

            await using var conn = GetConnection();
            await conn.OpenAsync();

            // Check if email already exists
            var checkSql = "SELECT COUNT(*) FROM users WHERE email = @email";
            await using var checkCmd = new NpgsqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("@email", request.Email.ToLower());
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (count > 0)
            {
                return BadRequest(new { message = "Email already registered" });
            }

            // Hash password
            var passwordHash = HashPassword(request.Password);

            // Create user
            var userId = Guid.NewGuid();
            var sql = @"INSERT INTO users (user_id, username, email, password_hash, created_at)
                        VALUES (@userId, @username, @email, @passwordHash, NOW())";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@username", request.Username);
            cmd.Parameters.AddWithValue("@email", request.Email.ToLower());
            cmd.Parameters.AddWithValue("@passwordHash", passwordHash);

            await cmd.ExecuteNonQueryAsync();

            return Ok(new
            {
                userId = userId,
                username = request.Username,
                email = request.Email.ToLower()
            });
        }

        // LOGIN
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || 
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Email and password are required");
            }

            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"SELECT user_id, username, email, password_hash 
                        FROM users 
                        WHERE email = @email";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@email", request.Email.ToLower());
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var userId = reader.GetGuid(0);
                var username = reader.GetString(1);
                var email = reader.GetString(2);
                var storedHash = reader.GetString(3);

                // Verify password
                if (VerifyPassword(request.Password, storedHash))
                {
                    return Ok(new
                    {
                        userId = userId,
                        username = username,
                        email = email
                    });
                }
            }

            return Unauthorized(new { message = "Invalid email or password" });
        }

        // GET USER BY ID
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUser(Guid userId)
        {
            await using var conn = GetConnection();
            await conn.OpenAsync();

            var sql = @"SELECT user_id, username, email, created_at 
                        FROM users 
                        WHERE user_id = @userId";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return Ok(new
                {
                    userId = reader.GetGuid(0),
                    username = reader.GetString(1),
                    email = reader.GetString(2),
                    createdAt = reader.GetDateTime(3)
                });
            }

            return NotFound("User not found");
        }

        // Helper methods for password hashing
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private bool VerifyPassword(string password, string storedHash)
        {
            var hash = HashPassword(password);
            return hash == storedHash;
        }
    }
}
