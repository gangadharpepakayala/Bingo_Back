namespace Bingo_Back.Models
{
    public class DrawRequest
    {
        public Guid RoomId { get; set; }
        public Guid PlayerId { get; set; }
        public int Number { get; set; }
    }
}
