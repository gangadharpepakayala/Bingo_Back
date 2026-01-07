namespace Bingo_Back.Models
{
    public class GenerateTicketRequest
    {
        public Guid PlayerId { get; set; }
        public Guid RoomId { get; set; }
    }
}
