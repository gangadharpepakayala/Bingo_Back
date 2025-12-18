namespace Bingo_Back.Models
{
    public class Ticket
    {
        public Guid TicketId { get; set; }
        public Guid PlayerId { get; set; }
        public Guid GameRoomId { get; set; }
        public string TicketData { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
