using System.ComponentModel.DataAnnotations;

namespace PizzaBot.Models
{
    public class PizzaResult
    {
        public int Id { get; set; }

        [Range(0, 1000, ErrorMessage = "Pieces can't be negative or over 1000")]
        public int resPiecesMeat { get; set; }

        [Range(0, 1000, ErrorMessage = "Pieces can't be negative or over 1000")]
        public int resPiecesVegetarian { get; set; }

        [Range(0, 1000, ErrorMessage = "Pieces can't be negative or over 1000")]
        public int resPiecesVegan { get; set; }

        public float penaltyMeatVeggi { get; set; }

        public float penaltyVeggieVegan { get; set; }

        public float totalCost {  get; set; }

        public bool hasPaid { get; set; }

    }
}
