namespace Bingo_Back.Models
{
    public class GameRoom
    {
        public Guid GameRoomId { get; set; }
        public string RoomName { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
