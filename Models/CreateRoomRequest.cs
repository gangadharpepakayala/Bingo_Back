namespace Bingo_Back.Models
{
    public class CreateRoomRequest
    {
        public string RoomName { get; set; }
        public Guid UserId { get; set; }
    }
}
