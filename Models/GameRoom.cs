namespace Bingo_Back.Models
{
    public class GameRoom
    {
        public Guid GameRoomId { get; set; }
        public string RoomName { get; set; }
        public string Status { get; set; } // pending, active, completed
        public int PlayerCount { get; set; }
        public Guid? CurrentTurnPlayerId { get; set; } // Track whose turn it is
        public Guid CreatedByUserId { get; set; } // Room owner
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; } // Auto-delete after 1 day
    }
}
