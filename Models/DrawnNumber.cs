namespace Bingo_Back.Models
{
    public class DrawnNumber
    {
        public Guid DrawId { get; set; }
        public Guid GameRoomId { get; set; }
        public int Number { get; set; }
        public DateTime DrawTime { get; set; }
    }
}
