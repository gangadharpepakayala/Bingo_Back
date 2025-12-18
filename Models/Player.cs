namespace Bingo_Back.Models
{
    public class Player
    {
        public Guid PlayerId { get; set; }
        public Guid GameRoomId { get; set; }
        public Guid UserId { get; set; }
        public DateTime JoinedAt { get; set; }
    }
}
