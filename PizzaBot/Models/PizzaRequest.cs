using System.ComponentModel.DataAnnotations;

namespace PizzaBot.Models
{
    public class PizzaRequest
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Range(0, 1000, ErrorMessage = "Pieces can't be negative or over 1000")]
        public int reqPiecesMeat { get; set; }

        [Range(0, 1000, ErrorMessage = "Pieces can't be negative or over 1000")]
        public int reqPiecesVegetarian { get; set; }

        [Range(0, 1000, ErrorMessage = "Pieces can't be negative or over 1000")]
        public int reqPiecesVegan { get; set; }

        [Range(0.0f, 1.0f)]
        public float priority { get; set; } = 0.5f;

        public PizzaRequest GetShallowCopy()
        {
            return (PizzaRequest)this.MemberwiseClone();
        }
    }
}
