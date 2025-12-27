namespace Bingo_Back.Models
{
    public class JoinRoomRequest
    {
        public Guid UserId { get; set; }
        public Guid RoomId { get; set; }
    }
}
