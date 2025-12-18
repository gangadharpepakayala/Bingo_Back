namespace Bingo_Back.Models
{
    public class User
    {
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
